# M10 - Observability and tracing

## Objective

Export OpenTelemetry spans to Application Insights for a real Foundry response and
optionally create a preview continuous relevance evaluation rule.

## Prerequisites

- `PROJECT_ENDPOINT`, `CHAT_MODEL`
- Full `APP_INSIGHTS_CONN_STRING`
- Permission to ingest telemetry
- Additional project permission for `--online-eval`

## Run

```powershell
dotnet run --project .\labs\10-observability -- --check
dotnet run --project .\labs\10-observability
dotnet run --project .\labs\10-observability -- --online-eval
```

Source: [`labs/10-observability/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/10-observability/Program.cs)

## Code flow

1. Build an OpenTelemetry provider with `AddAzureMonitorTraceExporter`.
2. Start a client span and tag system, model, lab, response id, and response length.
3. Invoke Foundry and export the span without prompt/response content.
4. With `--online-eval`, create an eval object and preview response-completed rule.

## Expected output

```text
<one-sentence span explanation>
Trace exported to Application Insights without recording prompt or response content.
Add --online-eval ...
```

The optional path prints the created rule response.

## Your Turn

1. **Add a tool, watch the span tree grow.** Attach a function tool as in Module 3, create
   a child `Activity` around host execution, and confirm that a tool span nests under
   `foundry.responses` in Application Insights.
2. **Trace token cost.** Read `usage.output_tokens` from `response.RootElement`, add it as
   a span tag, and extend the KQL query to sum output tokens by model over the last hour.
3. **Add a second rule.** Repeat the evaluation and rule REST calls for
   `builtin.coherence` with new ids, then `GET evaluation_rules` and confirm that both
   rules are returned.

## Cleanup and cost

Telemetry ingestion and online evaluators can incur cost. Delete or disable the
`csharp-workshop-relevance` rule after the exercise; it remains active server-side.

## Parity and preview caveats

OpenTelemetry and the Azure Monitor exporter are native stable .NET packages. Continuous
evaluation is preview and therefore uses authenticated REST; payload/version changes are
documented in [troubleshooting](../troubleshooting.md).
