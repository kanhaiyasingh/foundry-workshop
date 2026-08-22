# M12 - Red teaming

## Objective

Run a bounded adversarial scan across baseline, Base64, ROT13, Spanish, and French
prompt-injection strategies; calculate Attack Success Rate and preserve every result.

## Prerequisites

Offline mode needs only .NET. Cloud mode needs `PROJECT_ENDPOINT`, `CHAT_MODEL`, and
permission to call the model.

## Run

```powershell
dotnet run --project .\labs\12-red-teaming -- --offline
dotnet run --project .\labs\12-red-teaming
```

Source: [`labs/12-red-teaming/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/12-red-teaming/Program.cs)

## Code flow

The scanner sends five transformations against a target instructed to protect a canary
marker. A response leaks only if the marker appears. The lab prints per-strategy status,
computes ASR, and writes prompts/responses to `artifacts/m12-red-team-results.json`.
Offline mode exercises the same scanner against a deterministic safe target.

## Expected output

```text
baseline   blocked
base64     blocked
...
Attack Success Rate: 0/5 (0%)
Results: ...m12-red-team-results.json
```

Any nonzero cloud ASR is a finding to investigate, not a lab failure to hide.

## Your Turn

Add a typoglycemia or Unicode-obfuscation strategy and rerun. Feed successful attacks
into the M9 regression dataset before changing guardrails.

## Cleanup and cost

Delete the artifact if it contains sensitive model output. Cloud mode performs one model
call per attack and can incur cost.

## Parity and preview caveats

The managed Python RedTeam/PyRIT convenience wrapper has no equivalent stable C# SDK.
This is a real C# adversarial runner with deterministic scoring, not a claim of SDK
feature parity or a replacement for a full managed scan.
