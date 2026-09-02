# M10 · Observability & Tracing

> **Goal:** see *inside* a running agent — wire **OpenTelemetry** tracing to
> **Application Insights**, capture spans from a real call, then run **continuous
> evaluation** on live traffic.
>
> **You'll use:** an Azure Monitor OpenTelemetry exporter, `ActivitySource`, the
> Responses API, and the evaluation-rules REST surface.

---

In [M9](09-evaluation.md) you scored a fixed test set *before* shipping. Production is
the other half of the quality loop: once an agent is live, you need to know **what it
did**, **how long it took**, and **whether quality held** — without re-running a
notebook.

Two complementary surfaces give you that:

```text
            ┌─ OpenTelemetry tracing ──▶ spans ──▶ Application Insights (you own the data)
agent call ─┤
            └─ continuous evaluation ──▶ sampled scores ──▶ Foundry portal Monitor tab
```

![The quality loop](../assets/eval-observability.png)

**Tracing** is client-side: you configure an exporter and every instrumented operation
emits spans. **Continuous evaluation** is server-side: Foundry samples live responses
and scores them automatically. This lab sets up both.

Source: [`labs/10-observability/Program.cs`](https://github.com/kanhaiyasingh/foundry-workshop/blob/main/labs/10-observability/Program.cs)

## Prerequisites and run modes

- `PROJECT_ENDPOINT` and the normal optional `CHAT_MODEL` setting.
- The full `APP_INSIGHTS_CONN_STRING` for an Application Insights resource.
- **Foundry User** on the Foundry resource to create and invoke the demo agent.
- **Foundry User** for the project's managed identity when using continuous
  evaluation.

```powershell
# Configuration-only smoke check; no Azure calls or resources
dotnet run --project .\labs\10-observability -- --check

# Cells 1-6: create the demo agent version, send two model requests, and export traces
dotnet run --project .\labs\10-observability

# Cells 1-8: also create an enabled eval rule and send three more requests
dotnet run --project .\labs\10-observability -- --online-eval
```

Continuous evaluation is opt-in in the console port because its eval object and
enabled rule persist after the process exits. This lifecycle guard is the only
execution-flow difference from running every notebook cell in sequence.

The first code cell prints the current date and time:

```text
Current date and time: 2026-08-24 11:45:20.639000
```

## 1. Configure

Use the same `.env` as every lab, plus one observability key:
`APP_INSIGHTS_CONN_STRING`, the connection string of the Application Insights resource
that receives the traces.

```csharp
var projectEndpoint = context.Config.ProjectEndpoint;
var chatModel = context.Config.ChatModel;
var connectionString = context.Config.Require("APP_INSIGHTS_CONN_STRING");

Console.WriteLine($"Project      : {projectEndpoint}");
Console.WriteLine($"Chat         : {chatModel}");
Console.WriteLine(
    $"App Insights : {connectionString[..Math.Min(40, connectionString.Length)]}...");
```

!!! note "Where does App Insights come from?"
    Provisioning the Application Insights resource and copying its connection string
    into `.env` is a one-time platform task — see the
    [C# setup guide](../csharp-setup.md). The
    reference deploys it with Bicep; this lab assumes it already exists and only reads
    `APP_INSIGHTS_CONN_STRING`.

!!! note "Expected output"
    ```text
    Project      : https://<account>.services.ai.azure.com/api/projects/<project>
    Chat         : gpt-4.1-mini
    App Insights : InstrumentationKey=abc123...;IngestionEndpoint=...
    ```

## 2. Wire OpenTelemetry to Application Insights

The .NET provider sets up the same complete OpenTelemetry pipeline as Python's
`configure_azure_monitor()`: a `TracerProvider` with an Azure Monitor exporter pointed
at the configured connection string. It must be built **before** any instrumented
client or operation.

```csharp
using var provider = Sdk.CreateTracerProviderBuilder()
    .AddSource("FoundryWorkshop.M10")
    .AddAzureMonitorTraceExporter(
        options => options.ConnectionString = connectionString)
    .Build();

Console.WriteLine("OpenTelemetry configured with Azure Monitor");
Console.WriteLine("TracerProvider ready");
```

!!! note "Expected output"
    ```text
    OpenTelemetry configured with Azure Monitor
    TracerProvider ready
    ```

    A notebook re-run can reuse an existing `TracerProvider` so exporters do not stack.
    This console application creates exactly one provider per process.

## 3. Instrument the Foundry SDK

The pipeline is ready, but operations do not emit this lab's spans until they are
instrumented. The current C# SDK has no facade equivalent to Python's
`AIProjectInstrumentor`, so the port uses `ActivitySource` around agent administration
and every Responses operation. The instrumentation is installed before clients are
built.

Prompt and response bodies are never attached to activities, which preserves
`enable_content_recording=False`. Spans, timings, response IDs, model names, agent
names, and token counts are still captured.

```csharp
using var activitySource = new ActivitySource("FoundryWorkshop.M10");

Console.WriteLine("Azure AI Projects SDK instrumented");
Console.WriteLine(
    "  content recording : disabled (spans + token counts still captured)");
```

!!! note "Expected output"
    ```text
    Azure AI Projects SDK instrumented
      content recording : disabled (spans + token counts still captured)
    ```

!!! warning "Order matters"
    Configure Azure Monitor → instrument operations → **then** build clients. Building
    or calling clients outside the `ActivitySource` wrappers produces no lab spans.
    Re-run from section 2 in order if traces are empty.

## 4. Build the client and a small agent to watch

Now — *after* instrumentation — build the client and create a tiny agent to trace.
This mirrors [M2](02-your-first-agent.md): a `DeclarativeAgentDefinition` versioned
under a stable name.

```csharp
var projectClient = context.CreateProjectClient();
var openAiClient = projectClient.ProjectOpenAIClient;

ProjectsAgentDefinition definition = new DeclarativeAgentDefinition(chatModel)
{
    Instructions = "You are a concise assistant. Answer in one or two sentences."
};
var result = await projectClient.AgentAdministrationClient.CreateAgentVersionAsync(
    "observability-demo-agent",
    new ProjectsAgentVersionCreationOptions(definition));
ProjectsAgentVersion agent = result.Value;

Console.WriteLine("openai_client : ready");
Console.WriteLine($"agent         : {agent.Name} v{agent.Version}");
```

The actual source wraps `CreateAgentVersionAsync` in an `agents.create_version`
activity and adds model, agent name, version, status, and error details.

!!! note "Expected output"
    ```text
    openai_client : ready
    agent         : observability-demo-agent v1
    ```

    A `403` here means the identity lacks the **Foundry User** role; a credential
    error means `az login`. The bootstrap is the same as every lab — only the
    instrumentation before it is new.

## 5. Make a traced call and flush

Invoke the agent through the Responses API exactly as in M2. Nothing extra appears in
the answer because spans are emitted silently to Application Insights in the
background. OpenTelemetry batches spans, so `ForceFlush()` pushes them immediately
instead of waiting for the batch timer.

```csharp
string[] queries =
[
    "What is OpenTelemetry, in one sentence?",
    "Name one benefit of distributed tracing."
];

foreach (var query in queries)
{
    var stopwatch = Stopwatch.StartNew();
    using var response = await CreateTracedResponseAsync(
        context, activitySource, agent.Name, chatModel, query);
    stopwatch.Stop();

    Console.WriteLine($"Q: {query}");
    Console.WriteLine(
        $"A: {JsonHelpers.GetOutputText(response.RootElement)}   " +
        $"({stopwatch.Elapsed.TotalMilliseconds:0} ms)");
    Console.WriteLine();
}

provider.ForceFlush();
Console.WriteLine("Spans flushed to Application Insights");
```

`CreateTracedResponseAsync` creates nested `responses` and `chat` activities, sends an
`agent_reference`, awaits the asynchronous response to a terminal state, records
response and usage attributes, and marks failures without recording content. Awaiting
each response also avoids the agent's single-flight `409 Conflict` when the next query
starts.

!!! note "Expected output"
    ```text
    Q: What is OpenTelemetry, in one sentence?
    A: OpenTelemetry is an open standard for collecting traces, metrics, and logs
       from applications.   (812 ms)

    Q: Name one benefit of distributed tracing.
    A: It lets you follow a single request across services to pinpoint where latency
       or errors occur.   (734 ms)

    Spans flushed to Application Insights
    ```

    Replies and timings vary. The value is the **telemetry behind them**.

## 6. What the spans look like

Each Responses call produces a nested **span tree** in the Application Insights
`dependencies` table. Spans follow the **GenAI OpenTelemetry semantic conventions**,
so their attribute names remain portable:

```text
responses
  └── chat
        gen_ai.request.model       = gpt-4.1-mini
        gen_ai.usage.input_tokens  = 18
        gen_ai.usage.output_tokens = 24
        gen_ai.system              = az.ai.inference
        agent.name                 = observability-demo-agent
```

Query the spans with KQL after the normal 30–60 second ingestion delay. The program
prints this query:

```kusto
dependencies
| where timestamp > ago(30m)
| where name has_any ("responses", "chat", "tool")
| project timestamp, span = name,
          model         = tostring(customDimensions["gen_ai.request.model"]),
          input_tokens  = toint(customDimensions["gen_ai.usage.input_tokens"]),
          output_tokens = toint(customDimensions["gen_ai.usage.output_tokens"]),
          duration_ms   = duration, success
| order by timestamp asc
```

Run it in the Application Insights Logs blade. The programmatic equivalent is
`Azure.Monitor.Query.LogsQueryClient` with the Application Insights resource ID and a
30-minute timespan.

!!! note "Expected output (after ingestion)"
    ```text
    timestamp  span       model         input_tokens  output_tokens  duration_ms  success
    10:42:01   responses  gpt-4.1-mini        18            24            812       True
    10:42:03   chat       gpt-4.1-mini        18            24            640       True
    ```

!!! tip "Two views, no extra code"
    The same traces also appear under the agent's **Monitor tab** in the Foundry portal:
    `ai.azure.com` → **Build** → your agent → **Monitor**. That view shows token usage,
    latency, and run success-rate charts server-side with **no client
    instrumentation**. Tracing is for *your* backend; the Monitor tab is the
    at-a-glance dashboard.

## 7. Continuous (online) evaluation

Tracing tells you *what happened*; **continuous evaluation** tells you *whether it was
any good* — automatically, on live traffic. Define an **eval object** describing what
to measure, then an **evaluation rule** attached to the agent. Foundry samples real
responses and runs the evaluator server-side; results land in the Monitor tab with no
per-request evaluation code. Run this stage with `--online-eval`; without that flag the
program stops after printing the KQL and explains how to opt in.

```csharp
using var evaluation = await context.Rest.SendProjectJsonAsync(
    HttpMethod.Post,
    "openai/v1/evals",
    new
    {
        name = "Continuous Relevance (observability demo)",
        data_source_config = new { type = "azure_ai_source", scenario = "responses" },
        testing_criteria = new object[]
        {
            new
            {
                type = "azure_ai_evaluator",
                name = "relevance_check",
                evaluator_name = "builtin.relevance",
                data_mapping = new
                {
                    query = "{{item.query}}",
                    response = "{{item.response}}"
                },
                initialization_parameters = new
                {
                    deployment_name = chatModel
                }
            }
        }
    });
var evaluationId = evaluation.RootElement.GetProperty("id").GetString();
Console.WriteLine($"eval object : {evaluationId}");

using var rule = await context.Rest.SendProjectJsonAsync(
    HttpMethod.Put,
    "evaluationrules/continuous-relevance-rule-demo?api-version=v1",
    new
    {
        displayName = "Continuous Relevance (observability demo)",
        action = new
        {
            type = "continuousEvaluation",
            evalId = evaluationId,
            maxHourlyRuns = 100
        },
        eventType = "responseCompleted",
        filter = new { agentName = agent.Name },
        enabled = true
    },
    new Dictionary<string, string>
    {
        ["Foundry-Features"] = "Evaluations=V1Preview"
    });
```

The complete source also validates the eval ID and prints the returned rule ID and
enabled state.

!!! note "Expected output"
    ```text
    eval object : eval_abc123
    rule        : continuous-relevance-rule-demo | enabled: True
    ```

    From now on, a sampled fraction of this agent's live responses is scored for
    relevance automatically. The rule keeps running until you disable it.

!!! warning "API is evolving"
    Continuous-evaluation models and the evals schema are **preview** and move between
    releases. The C# port uses authenticated REST because the stable C# SDK has no
    equivalent facade. A `403` on the rule means the project's managed identity needs
    the **Foundry User** role, which is a platform setup step.

## 8. See it in the portal

Generate a little traffic, then watch the scores appear. The rule samples these agent
responses and the **Monitor tab** fills in within a few minutes.

```csharp
foreach (var query in new[]
         {
             "Summarize what a span is.",
             "Why batch telemetry before exporting?",
             "What does force_flush do?"
         })
{
    using var response = await CreateTracedResponseAsync(
        context, activitySource, agent.Name, chatModel, query);
}
provider.ForceFlush();

Console.WriteLine("Traffic sent — the continuous-eval rule will score sampled responses.");
Console.WriteLine();
Console.WriteLine("View results:");
Console.WriteLine("  1. Open https://ai.azure.com  (New Foundry toggle on)");
Console.WriteLine("  2. Build -> select 'observability-demo-agent'");
Console.WriteLine("  3. Monitor tab -> Evaluation metrics + Monitor Settings");
```

!!! note "Expected output"
    ```text
    Traffic sent — the continuous-eval rule will score sampled responses.

    View results:
      1. Open https://ai.azure.com  (New Foundry toggle on)
      2. Build -> select 'observability-demo-agent'
      3. Monitor tab -> Evaluation metrics + Monitor Settings
    ```

    Scores can take a few minutes to surface after the first batch. Under **Monitor
    Settings**, `continuous-relevance-rule-demo` appears as **Enabled** — the same
    relevance evaluator from M9, now running on live traffic instead of a static file.

## Cost and cleanup

Application Insights telemetry ingestion and model calls can incur charges. A normal
run sends two model requests; `--online-eval` sends five total and can add ongoing
evaluator/model usage. Each run creates a persistent
`observability-demo-agent` version. The opt-in path also creates a persistent eval
object and replaces or enables `continuous-relevance-rule-demo`.

After the exercise, delete `observability-demo-agent` from **Build**, and disable or
delete `continuous-relevance-rule-demo` from **Monitor Settings**. Delete the printed
`eval_...` object as well if it is no longer needed. The console does not automate
M10 cleanup because the preview eval/rule deletion surface can vary by service version;
leaving the enabled rule in place can continue consuming evaluation capacity.

## 🧪 Your turn

1. **Add a tool, watch the span tree grow.** Attach a function tool to the agent as in
   [M3](03-tools-and-function-calling.md), then re-run section 5. A `tool` span now
   nests under `responses` in the KQL results.
2. **Trace token cost.** Extend the KQL `project` to `sum(output_tokens)` grouped by
   `model` to see spend per deployment over the last hour.
3. **Add a second rule.** Create an eval object for `builtin.coherence` and a second
   evaluation rule with a new ID, then list evaluation rules and confirm both appear.

---

✅ **You wired OpenTelemetry to Application Insights, captured spans from a live agent
call, and set up continuous evaluation that scores production traffic.** Next: put
**guardrails** around what your agent is allowed to say and do.

→ **[M11 · Guardrails](11-guardrails.md)**
