// # M10 · Observability & Tracing
//
// > **Goal:** see *inside* a running agent — wire **OpenTelemetry** tracing to
// > **Application Insights**, capture spans from a real call, then run **continuous
// > evaluation** on live traffic.
// > **You'll use:** an Azure Monitor OpenTelemetry exporter, ActivitySource, the
// > Responses API, and the evaluation-rules REST surface.
//
// Full guide: docs/modules/10-observability.md
// Check:       dotnet run --project .\labs\10-observability -- --check
// Run:         dotnet run --project .\labs\10-observability
// Online eval: dotnet run --project .\labs\10-observability -- --online-eval
//
// ---
//
// In M9 you scored a fixed test set *before* shipping. Production is the other half
// of the quality loop: once an agent is live, you need to know **what it did**, **how
// long it took**, and **whether quality held** — without re-running a notebook.
//
// Two complementary surfaces give you that:
//
//             ┌─ OpenTelemetry tracing ──▶ spans ──▶ Application Insights (you own the data)
// agent call ─┤
//             └─ continuous evaluation ──▶ sampled scores ──▶ Foundry portal Monitor tab
//
// See docs/assets/eval-observability.png for the quality loop.
//
// **Tracing** is client-side: you configure an exporter and every operation emits
// spans. **Continuous evaluation** is server-side: Foundry samples live responses and
// scores them automatically. You'll set up both.

using System.Diagnostics;
using System.Text.Json;
using Azure.AI.Projects.Agents;
using Azure.Monitor.OpenTelemetry.Exporter;
using FoundryWorkshop.Shared;
using OpenTelemetry;
using OpenTelemetry.Trace;

const string sourceName = "FoundryWorkshop.M10";
const string agentName = "observability-demo-agent";
using var activitySource = new ActivitySource(sourceName);

// Notebook cell: print the current date and time.
Console.WriteLine($"Current date and time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}");

return await LabHost.RunAsync(
    "M10 - Observability and tracing",
    args,
    async context =>
    {
        // ## 1. Configure
        //
        // Same `.env` as every lab, plus one observability key:
        // `APP_INSIGHTS_CONN_STRING` — the connection string of an Application
        // Insights resource that will receive your traces.
        var projectEndpoint = context.Config.ProjectEndpoint;
        var chatModel = context.Config.ChatModel;
        var connectionString = context.Config.Require(
            "APP_INSIGHTS_CONN_STRING",
            "Copy the full Application Insights connection string into .env.");

        Console.WriteLine($"Project      : {projectEndpoint}");
        Console.WriteLine($"Chat         : {chatModel}");
        Console.WriteLine(
            $"App Insights : {connectionString[..Math.Min(40, connectionString.Length)]}...");

        // !!! note "Where does App Insights come from?"
        //     Provisioning the Application Insights resource and copying its connection
        //     string into `.env` is a one-time platform task — see the Platform docs.
        //     The reference deploys it with Bicep; in this lab we assume it already
        //     exists and just **read** `APP_INSIGHTS_CONN_STRING`.
        //
        // !!! note "Expected output"
        //     Project      : https://<account>.services.ai.azure.com/api/projects/<project>
        //     Chat         : gpt-4.1-mini
        //     App Insights : InstrumentationKey=abc123...;IngestionEndpoint=...

        // ## 2. Wire OpenTelemetry to Application Insights
        //
        // The .NET provider sets up the same OpenTelemetry pipeline as Python's
        // `configure_azure_monitor()`: a TracerProvider with an Azure Monitor exporter
        // pointed at the configured connection string. This must run **before** any
        // instrumented client or operation is created.
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddAzureMonitorTraceExporter(options => options.ConnectionString = connectionString)
            .Build();

        Console.WriteLine("OpenTelemetry configured with Azure Monitor");
        Console.WriteLine("TracerProvider ready");

        // !!! note "Expected output"
        //     OpenTelemetry configured with Azure Monitor
        //     TracerProvider ready
        //
        // A notebook re-run can reuse an existing TracerProvider so exporters do not
        // stack. This console process creates exactly one provider per run.

        // ## 3. Instrument the Foundry SDK
        //
        // The pipeline is ready, but operations do not emit this lab's spans until they
        // are wrapped with `ActivitySource`. The C# SDK does not expose Python's
        // `AIProjectInstrumentor`, so this port explicitly instruments agent creation
        // and each Responses operation before building the clients.
        // `enable_content_recording=False` is preserved by never attaching prompt or
        // response bodies; spans, timings, response ids, and token counts are captured.
        Console.WriteLine("Azure AI Projects SDK instrumented");
        Console.WriteLine("  content recording : disabled (spans + token counts still captured)");

        // !!! note "Expected output"
        //     Azure AI Projects SDK instrumented
        //       content recording : disabled (spans + token counts still captured)
        //
        // !!! warning "Order matters"
        //     Configure Azure Monitor → instrument operations → **then** build clients.
        //     Building or calling clients outside these ActivitySource wrappers produces
        //     no lab spans. Re-run from section 2 in order if you see empty traces.

        // ## 4. Build the client and a small agent to watch
        //
        // Now — *after* instrumentation — build the client and create a tiny agent to
        // trace. This mirrors M2: a DeclarativeAgentDefinition versioned under a stable
        // name. Every call made below runs inside the configured span source.
        var projectClient = context.CreateProjectClient();
        var openAiClient = projectClient.ProjectOpenAIClient;
        _ = openAiClient;

        ProjectsAgentVersion agent;
        using (var createSpan = activitySource.StartActivity(
                   "agents.create_version",
                   ActivityKind.Client))
        {
            createSpan?.SetTag("gen_ai.system", "microsoft_foundry");
            createSpan?.SetTag("gen_ai.request.model", chatModel);
            createSpan?.SetTag("agent.name", agentName);

            try
            {
                ProjectsAgentDefinition definition = new DeclarativeAgentDefinition(chatModel)
                {
                    Instructions =
                        "You are a concise assistant. Answer in one or two sentences."
                };
                var agentResult = await projectClient.AgentAdministrationClient
                    .CreateAgentVersionAsync(
                        agentName,
                        new ProjectsAgentVersionCreationOptions(definition));
                agent = agentResult.Value;
                createSpan?.SetTag("agent.version", agent.Version);
                createSpan?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                createSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }

        Console.WriteLine("openai_client : ready");
        Console.WriteLine($"agent         : {agent.Name} v{agent.Version}");

        // !!! note "Expected output"
        //     openai_client : ready
        //     agent         : observability-demo-agent v1
        //
        // A `403` here means your identity lacks the **Azure AI Developer** role; a
        // credential error means `az login`. Same bootstrap as every lab — only the
        // instrumentation before it is new.

        // ## 5. Make a traced call and flush
        //
        // Invoke the agent through the Responses API exactly as in M2. You won't *see*
        // anything extra in the answer — spans are emitted silently to App Insights in
        // the background. OpenTelemetry **batches** spans, so `ForceFlush()` pushes them
        // immediately rather than waiting for the batch timer.
        string[] queries =
        [
            "What is OpenTelemetry, in one sentence?",
            "Name one benefit of distributed tracing."
        ];

        foreach (var query in queries)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await CreateTracedResponseAsync(
                context,
                activitySource,
                agent.Name,
                chatModel,
                query);
            stopwatch.Stop();

            Console.WriteLine($"Q: {query}");
            Console.WriteLine(
                $"A: {JsonHelpers.GetOutputText(response.RootElement)}   " +
                $"({stopwatch.Elapsed.TotalMilliseconds:0} ms)");
            Console.WriteLine();
        }

        provider.ForceFlush();
        Console.WriteLine("Spans flushed to Application Insights");

        // !!! note "Expected output"
        //     Q: What is OpenTelemetry, in one sentence?
        //     A: OpenTelemetry is an open standard for collecting traces, metrics, and
        //        logs from applications.   (812 ms)
        //
        //     Q: Name one benefit of distributed tracing.
        //     A: It lets you follow a single request across services to pinpoint where
        //        latency or errors occur.   (734 ms)
        //
        //     Spans flushed to Application Insights
        //
        // The replies are ordinary; the value is the **telemetry behind them** — see the
        // next section.

        // ## 6. What the spans look like
        //
        // Each Responses call produces a nested **span tree** in App Insights (the
        // `dependencies` table). Spans follow the **GenAI OpenTelemetry semantic
        // conventions**, so attribute names are portable across tools:
        //
        // responses
        //   └── chat
        //         gen_ai.request.model       = gpt-4.1-mini
        //         gen_ai.usage.input_tokens  = 18
        //         gen_ai.usage.output_tokens = 24
        //         gen_ai.system              = az.ai.inference
        //         agent.name                 = observability-demo-agent
        //
        // You query these with KQL once they've ingested (~30–60s).
        const string kql = """
            dependencies
            | where timestamp > ago(30m)
            | where name has_any ("responses", "chat", "tool")
            | project timestamp, span = name,
                      model         = tostring(customDimensions["gen_ai.request.model"]),
                      input_tokens  = toint(customDimensions["gen_ai.usage.input_tokens"]),
                      output_tokens = toint(customDimensions["gen_ai.usage.output_tokens"]),
                      duration_ms   = duration, success
            | order by timestamp asc
            """;
        Console.WriteLine(kql);

        // The KQL is printed for the Application Insights Logs blade. The equivalent
        // programmatic path is Azure.Monitor.Query's LogsQueryClient with the resource
        // id and a 30-minute timespan.

        // !!! note "Expected output (after ingestion)"
        //     Running the KQL in the App Insights Logs blade returns one row per span:
        //
        //     timestamp  span       model         input_tokens  output_tokens  duration_ms  success
        //     10:42:01   responses  gpt-4.1-mini        18            24            812       True
        //     10:42:03   chat       gpt-4.1-mini        18            24            640       True
        //
        // !!! tip "Two views, no extra code"
        //     Those same traces also appear under the agent's **Monitor tab** in the
        //     Foundry portal (`ai.azure.com` → **Build** → your agent → **Monitor**) —
        //     token usage, latency, and run success-rate charts, server-side, with **no
        //     client instrumentation**. Tracing is for *your* backend; the Monitor tab
        //     is an at-a-glance dashboard.

        // ## 7. Continuous (online) evaluation
        //
        // Tracing tells you *what happened*; **continuous evaluation** tells you
        // *whether it was any good* — automatically, on live traffic. Define an **eval
        // object** (what to measure), then an **evaluation rule** attached to the agent.
        // Foundry samples real responses and runs the evaluator server-side; results land
        // in the Monitor tab. No per-request code.
        //
        // Creating the eval object and enabled rule is deliberately opt-in in the console
        // port because both persist after the process exits and the rule can continue to
        // consume evaluation capacity. The notebook cell below is otherwise unchanged.
        if (!context.HasFlag("--online-eval"))
        {
            Console.WriteLine();
            Console.WriteLine(
                "Add --online-eval to create the persistent continuous relevance eval and rule.");
            return;
        }

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
        var evaluationId = evaluation.RootElement.GetProperty("id").GetString()
            ?? throw new JsonException("Foundry create-eval response omitted id.");
        Console.WriteLine($"eval object : {evaluationId}");

        using var rule = await context.Rest.SendProjectJsonAsync(
            HttpMethod.Put,
            "evaluation_rules/continuous-relevance-rule-demo?api-version=2025-05-15-preview",
            new
            {
                display_name = "Continuous Relevance (observability demo)",
                action = new
                {
                    type = "continuous",
                    eval_id = evaluationId,
                    max_hourly_runs = 100
                },
                event_type = "response_completed",
                filter = new { agent_name = agent.Name },
                enabled = true
            });
        var ruleId = GetOptionalString(rule.RootElement, "id")
            ?? "continuous-relevance-rule-demo";
        var ruleEnabled = GetOptionalBoolean(rule.RootElement, "enabled") ?? true;
        Console.WriteLine($"rule        : {ruleId} | enabled: {ruleEnabled}");

        // !!! note "Expected output"
        //     eval object : eval_abc123
        //     rule        : continuous-relevance-rule-demo | enabled: True
        //
        // From now on, a sampled fraction of this agent's live responses are scored for
        // relevance automatically — the rule keeps running until you disable it.
        //
        // !!! warning "API is evolving"
        //     Continuous-eval models and the evals schema are **preview** and move between
        //     releases. This C# port uses authenticated REST because the stable C# SDK
        //     has no equivalent facade. A `403` on the rule means the project's managed
        //     identity needs the **Foundry User** role (a platform setup step).

        // ## 8. See it in the portal
        //
        // Generate a little traffic, then watch the scores appear. The rule samples these
        // agent responses and the **Monitor tab** fills in within a few minutes.
        foreach (var query in new[]
                 {
                     "Summarize what a span is.",
                     "Why batch telemetry before exporting?",
                     "What does force_flush do?"
                 })
        {
            using var response = await CreateTracedResponseAsync(
                context,
                activitySource,
                agent.Name,
                chatModel,
                query);
        }

        provider.ForceFlush();

        Console.WriteLine("Traffic sent — the continuous-eval rule will score sampled responses.");
        Console.WriteLine();
        Console.WriteLine("View results:");
        Console.WriteLine("  1. Open https://ai.azure.com  (New Foundry toggle on)");
        Console.WriteLine("  2. Build -> select 'observability-demo-agent'");
        Console.WriteLine("  3. Monitor tab -> Evaluation metrics + Monitor Settings");

        // !!! note "Expected output"
        //     Traffic sent — the continuous-eval rule will score sampled responses.
        //
        //     View results:
        //       1. Open https://ai.azure.com  (New Foundry toggle on)
        //       2. Build -> select 'observability-demo-agent'
        //       3. Monitor tab -> Evaluation metrics + Monitor Settings
        //
        // Scores can take a few minutes to surface after the first batch. Under **Monitor
        // Settings** you'll see `continuous-relevance-rule-demo` listed as **Enabled** —
        // the same relevance evaluator from M9, now running on live traffic instead of a
        // static file.

        // ## 🧪 Your turn
        //
        // 1. **Add a tool, watch the span tree grow.** Attach a function tool to the agent
        //    (as in M3) and re-run section 5 — a `tool` span now nests under `responses`
        //    in your KQL results.
        // 2. **Trace token cost.** Extend the KQL `project` to `sum(output_tokens)`
        //    grouped by `model` to see spend per deployment over the last hour.
        // 3. **Add a second rule.** Create an eval object for `builtin.coherence` and a
        //    second evaluation rule with a new id, then list evaluation rules and confirm
        //    both appear.
        //
        // ---
        //
        // ✅ **You wired OpenTelemetry to Application Insights, captured spans from a
        // live agent call, and set up continuous evaluation that scores production
        // traffic.** Next: put **guardrails** around what your agent is allowed to say
        // and do. → **M11 · Guardrails**
    },
    "PROJECT_ENDPOINT",
    "APP_INSIGHTS_CONN_STRING");

static async Task<JsonDocument> CreateTracedResponseAsync(
    WorkshopContext context,
    ActivitySource activitySource,
    string agentName,
    string model,
    string query)
{
    using var responsesSpan = activitySource.StartActivity("responses", ActivityKind.Client);
    SetCommonTags(responsesSpan, agentName, model);

    using var chatSpan = activitySource.StartActivity("chat", ActivityKind.Client);
    SetCommonTags(chatSpan, agentName, model);

    JsonDocument? response = null;
    try
    {
        response = await context.Rest.CreateResponseAsync(new
        {
            input = new[] { new { role = "user", content = query } },
            agent_reference = new { name = agentName, type = "agent_reference" }
        });

        var status = GetOptionalString(response.RootElement, "status");
        if (status is not null &&
            !status.Equals("completed", StringComparison.OrdinalIgnoreCase))
        {
            var responseId = GetOptionalString(response.RootElement, "id")
                ?? throw new JsonException("Foundry create-response payload omitted id.");
            var terminal = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "completed", "failed", "incomplete", "cancelled"
            };

            for (var attempt = 0;
                 attempt < 60 && !terminal.Contains(status ?? string.Empty);
                 attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                var retrieved = await context.Rest.SendProjectJsonAsync(
                    HttpMethod.Get,
                    $"openai/v1/responses/{responseId}");
                response.Dispose();
                response = retrieved;
                status = GetOptionalString(response.RootElement, "status");
            }

            if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Response '{responseId}' did not complete (last status: " +
                    $"{status ?? "unknown"}). Payload: {response.RootElement.GetRawText()}");
            }
        }

        AddResponseTags(chatSpan, response.RootElement);
        AddResponseTags(responsesSpan, response.RootElement);
        chatSpan?.SetStatus(ActivityStatusCode.Ok);
        responsesSpan?.SetStatus(ActivityStatusCode.Ok);
        var completed = response;
        response = null;
        return completed;
    }
    catch (Exception ex)
    {
        chatSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
        responsesSpan?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
    finally
    {
        response?.Dispose();
    }
}

static void SetCommonTags(Activity? activity, string agentName, string model)
{
    activity?.SetTag("gen_ai.system", "az.ai.inference");
    activity?.SetTag("gen_ai.request.model", model);
    activity?.SetTag("agent.name", agentName);
}

static void AddResponseTags(Activity? activity, JsonElement response)
{
    if (response.TryGetProperty("id", out var responseId))
    {
        activity?.SetTag("gen_ai.response.id", responseId.GetString());
    }

    if (!response.TryGetProperty("usage", out var usage))
    {
        return;
    }

    if (usage.TryGetProperty("input_tokens", out var inputTokens))
    {
        activity?.SetTag("gen_ai.usage.input_tokens", inputTokens.GetInt32());
    }

    if (usage.TryGetProperty("output_tokens", out var outputTokens))
    {
        activity?.SetTag("gen_ai.usage.output_tokens", outputTokens.GetInt32());
    }
}

static string? GetOptionalString(JsonElement element, string propertyName) =>
    element.TryGetProperty(propertyName, out var property) &&
    property.ValueKind == JsonValueKind.String
        ? property.GetString()
        : null;

static bool? GetOptionalBoolean(JsonElement element, string propertyName) =>
    element.TryGetProperty(propertyName, out var property) &&
    property.ValueKind is JsonValueKind.True or JsonValueKind.False
        ? property.GetBoolean()
        : null;
