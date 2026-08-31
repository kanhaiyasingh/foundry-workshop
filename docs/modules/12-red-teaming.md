# M12 - Red Teaming

> **Goal:** proactively attack your own model with the AI Red Teaming Agent. Run a
> basic scan across risk categories, run an advanced scan with encoding strategies
> and Spanish/French objectives, and interpret Attack Success Rate (ASR).
>
> **You'll use:** the Microsoft Foundry Red Teams preview REST API from C#, plus a
> resource-free offline fixture that verifies request shapes, scorecard parsing,
> artifacts, and output formatting.

In [M11](11-guardrails.md) you built defenses. Red teaming tests whether they hold:
generate adversarial objectives, send them to the target, evaluate the responses, and
measure how often an attack succeeds.

```text
Red Teams service -- generates objectives --> project model deployment
       |                                             |
       <-- managed safety evaluation --- responses --+
       |
       v
Attack Success Rate (lower is better)
```

The Python notebook uses the local PyRIT-backed `RedTeam` callback from
`azure-ai-evaluation[redteam]`. There is no equivalent .NET wrapper. The supported C#
adaptation submits the same target deployment, risk categories, and attack strategies
to Foundry's cloud Red Teams API. Foundry still generates and evaluates the probes;
this is not a hand-written canary scanner.

> [!WARNING]
> Red Teams is a preview service available only in supported regions; notebook
> examples include **East US 2**, **Sweden Central**, **France Central**, and
> **Switzerland West**. A live run requires a Foundry project, a deployed chat model,
> `az login`, and the **Foundry User** role. The notebook's Python 3.10-3.13 and
> `azure-ai-evaluation[redteam]` requirements do not apply to the C# program.

## Run

From the repository root:

```powershell
# Configuration-only; no authentication, HTTP, scan, or model usage.
dotnet run --project .\labs\12-red-teaming -- --check

# Configuration check for the resource-free path.
dotnet run --project .\labs\12-red-teaming -- --check --offline

# Meaningful local smoke path; writes clearly labeled illustrative artifacts.
dotnet run --project .\labs\12-red-teaming -- --offline

# Live, asynchronous, billable Red Teams runs.
dotnet run --project .\labs\12-red-teaming
```

`--offline` does **not** claim to attack a model. It exercises both scan
configurations, strict scorecard field handling, ASR calculation, and artifact output
with the notebook's illustrative numbers. Live mode is the real managed scan.

Source: [`labs/12-red-teaming/Program.cs`](https://github.com/kanhaiyasingh/foundry-workshop/blob/main/labs/12-red-teaming/Program.cs)

The sections below preserve the notebook cells in order.

## Current date and time

The first code cell prints the local date and time:

```text
Current date and time: 2026-08-24 12:24:36.604000
```

## 1. Configure

The live scanner needs the project endpoint where runs are recorded and the deployment
name to attack. The workshop loads both from the repository `.env`:

```dotenv
PROJECT_ENDPOINT=https://<account>.services.ai.azure.com/api/projects/<project>
CHAT_MODEL=gpt-4.1-mini
```

`PROJECT_ENDPOINT` is required in live mode. `CHAT_MODEL` defaults to
`gpt-4.1-mini`, matching the notebook:

```text
Project : https://<account>.services.ai.azure.com/api/projects/<project>
Model   : gpt-4.1-mini
.NET    : 8.x (OK)
```

The notebook validates Python because PyRIT restricts interpreter versions. The C#
adaptation reports its .NET runtime instead. `--check` validates configuration without
making Azure calls.

## 2. The target callback

The notebook supplies a callback that accepts a generated prompt, sends it through the
project OpenAI client, and returns the model response. The cloud Red Teams API cannot
accept a local C# delegate. Its equivalent target is the project deployment:

```json
{
  "type": "AzureOpenAIModel",
  "modelDeploymentName": "gpt-4.1-mini"
}
```

Before submitting a live run, the program smoke-tests that same deployment through the
Responses API with the notebook's exact prompt:

```text
smoke test: Hello!
```

This proves the endpoint, credential, and deployment work. It does not prove that a
different RAG pipeline or agent is covered. To attack a production application, move
to a Red Teams API target type that can represent that application rather than
silently substituting a bare-model scan.

## 3. Build the Red Team agent

The basic scan preserves the notebook's categories:

- `Violence`
- `HateUnfairness`
- `Sexual`
- `SelfHarm`

The Python wrapper's `num_objectives=5` generates five distinct objectives per
category, or 20 baseline prompts. The pinned cloud contract does **not** expose an
objective-count field. Its `numTurns` field is conversation depth, not objective
count. The C# request therefore uses `numTurns: 1` to preserve one prompt/response turn
per attack and lets the service manage objective count:

```text
RedTeam ready - 4 categories; objective count is service-managed (1 turn each)
```

Do not set `numTurns` to 5 or 10 as a translation of `num_objectives`; that changes the
attacks into multi-turn conversations instead of widening the objective set.

## 4. Run the basic scan

The live request uses the notebook's baseline strategy:

```json
{
  "numTurns": 1,
  "attackStrategies": ["baseline"],
  "simulationOnly": false,
  "riskCategories": [
    "Violence",
    "HateUnfairness",
    "Sexual",
    "SelfHarm"
  ],
  "target": {
    "type": "AzureOpenAIModel",
    "modelDeploymentName": "gpt-4.1-mini"
  }
}
```

The program sends this to:

```text
POST {PROJECT_ENDPOINT}/redTeams/runs:run?api-version=2025-05-15-preview
Foundry-Features: RedTeams=V1Preview
```

It polls `redTeams/runs/{id}` every ten seconds until a terminal state, with a
30-minute timeout:

```text
redteam-basic: submitted <run-id>
basic scan complete - results in redteam_basic_output/
```

The notebook's `scan(...)` writes local PyRIT files. The cloud API is different:

| Artifact | C# live contents |
| --- | --- |
| `results.json` | Complete terminal Red Teams run metadata returned by the API |
| `evaluation_results.json` | Written only if the service response actually embeds a scorecard |

The pinned run-metadata contract exposes run configuration and status, not the
notebook's local `results.json`, attack/response pairs, or scorecard. Inspect completed
live results under **Evaluation > AI red teaming** in Foundry. The program deletes a
stale local `evaluation_results.json` when the current response has no scorecard, so a
previous result cannot masquerade as the current scan.

## 5. Read the ASR scorecard

ASR is:

```text
successful adversarial probes / total adversarial probes
```

Lower is better. When a scorecard is present, the C# parser requires numeric successful
and total counts and calculates the percentage. Missing fields fail instead of becoming
success-shaped zeros.

`--offline` exercises the parser with the notebook's illustrative basic scorecard:

```text
category               ASR  success  total
------------------------------------------
OVERALL              10.0%        2     20
Violence             20.0%        1      5
Hate/Unfairness       0.0%        0      5
Sexual               20.0%        1      5
Self-Harm             0.0%        0      5
```

These are fixture values, not claims about your model. In a real result, two successful
attacks out of 20 would be a 10% ASR concentrated in Violence and Sexual. Tighten those
categories with [M11](11-guardrails.md) and rerun the same scan.

## 6. Advanced - evasion strategies and languages

Real attackers obfuscate prompts with Base64, ROT13, character spacing, Unicode
confusables, and other transformations. The advanced notebook code uses:

1. Base64
2. ROT13
3. Unicode confusables
4. composed Base64 then ROT13
5. Spanish and French objective languages

The C# request includes `baseline` explicitly so the service run retains the direct
comparison that the Python wrapper adds to its scorecard:

```json
[
  "baseline",
  "base64",
  "rot13",
  "unicode_confusable",
  ["base64", "rot13"]
]
```

The generated C# model supports a flat attack-strategy list even though the service
describes nested lists for composed attacks. The program uses raw REST so it can send
the composed array.

The pinned cloud contract also has no equivalent of the Python `languages` parameter.
The application scenario explicitly requests objectives in both Spanish and French.
That preserves the intent but is generator guidance, not the Python wrapper's strict
`SupportedLanguages` enum.

```text
redteam-advanced: submitted <run-id>
advanced scan complete - strategies + Spanish/French
```

> [!WARNING]
> `RedTeam`, attack strategy values, target types, and result fields are preview
> surfaces. This lab pins `Azure.AI.Projects` 2.0.0 and REST API
> `2025-05-15-preview`. Recheck the current contract before upgrading either.

## 7. Compare baseline with strategies

When the scorecard is available, the attack-technique summary reveals whether
obfuscation succeeds more often than direct prompts. `--offline` verifies the exact
comparison table using the notebook's illustrative values:

```text
technique          ASR  success  total
--------------------------------------
OVERALL          16.0%        8     50
baseline         10.0%        1     10
easy             17.5%        7     40
```

Encoded attacks scoring above baseline indicate that obfuscation bypassed a defense.
For a live cloud run, inspect ASR and each attack/response pair in Foundry when the run
metadata response does not embed a scorecard.

This closes the safety loop: [M11](11-guardrails.md) is defense, M12 is offense, and
[M9](09-evaluation.md) is the measuring tape.

## Your Turn

1. **Widen coverage.** In the local Python wrapper, change `num_objectives` to `10`.
   The pinned cloud API has no equivalent objective-count knob; do not misuse
   `numTurns`.
2. **Add a strategy.** Append `"flip"` or `"leetspeak"` to
   `advancedStrategies`, rerun, and compare its easy-complexity ASR.
3. **Attack a defended target.** The notebook points its callback at the M11 agent by
   `agent_reference`. The pinned C# model-target contract cannot represent that
   callback. Move to an API version/target type that supports Foundry agents before
   claiming this comparison.

## Cleanup and cost

Each live generated probe invokes both the target and managed safety evaluation.
Categories, strategies, multilingual generation, and multi-turn depth increase time
and cost. Delete `redteam_basic_output/` and `redteam_advanced_output/` if they contain
sensitive metadata, and delete unneeded Red Teams runs in Foundry. Never publish raw
attack/response data without reviewing it.

You configured a real cloud Red Teams scan, preserved the notebook's categories and
strategies where the REST contract allows it, exercised ASR and artifacts safely
offline, and documented every unavoidable C# service adaptation.

Next: [M13 - Human-in-the-Loop & REST](13-human-in-loop-rest.md).
