# M11 - Guardrails

## Objective

Layer Prompt Shields, PII patterns, standard safety categories, and a business blocklist;
then optionally apply the equivalent RAI policy and deployment through ARM REST.

## Prerequisites

Local demonstration needs only .NET. `--apply` needs `AZURE_SUBSCRIPTION_ID`,
`AZURE_RESOURCE_GROUP`, `FOUNDRY_ACCOUNT_NAME`, and Cognitive Services
Contributor/Contributor rights. `--deploy` also needs `CHAT_MODEL` and the exact
`GUARDRAIL_MODEL_VERSION`.

## Run

```powershell
dotnet run --project .\labs\11-guardrails
dotnet run --project .\labs\11-guardrails -- --apply
dotnet run --project .\labs\11-guardrails -- --apply --deploy
```

Source: [`labs/11-guardrails/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/11-guardrails/Program.cs)

## Code flow

The default path classifies benign, PII, and prompt-injection probes locally. `--apply`
creates the blocklist, regex/text entries, and `Microsoft.DefaultV2`-based RAI policy with
Jailbreak and Indirect Attack filters. `--deploy` explicitly creates a dedicated
guardrailed model deployment.

## Expected output

```text
Allowed            What are your branch hours?
PII blocklist      My SSN is ...
Prompt Shield      Ignore all previous ...
Local three-layer checks completed ...
```

ARM modes print the policy and deployment names after accepted requests.

## Your Turn

1. **Add a forbidden term.** Add `["comp-initech"] = "Initech Banking"` to `blockedTerms`,
   add an Initech probe, rerun the blocklist/policy apply path, then ask the guarded
   deployment about Initech and watch the business blocklist catch it.
2. **Tune a threshold.** Change the Violence `severityThreshold` to `"Low"`, reapply the
   policy, and probe with an edgy-but-not-violent prompt to see the stricter line.
3. **Name the trip in detail.** Extend the guarded-deployment test to print the raw
   `content_filter_result` JSON so that you can inspect severities and the exact
   `jailbreak` and `custom_blocklists` flags.

## Cleanup and cost

Delete the workshop deployment, RAI policy, blocklist items, and blocklist after use.
Deployments reserve quota and may incur cost. The local mode is free.

## Parity and preview caveats

The .NET management SDK does not expose every current guardrail operation consistently,
so the lab uses real ARM REST with `2024-10-01`. Filter and blocklist shapes can vary by
service version.
