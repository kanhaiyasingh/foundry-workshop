// M10 objective: export a Foundry response span and optionally enable continuous evaluation.
// Prerequisites: PROJECT_ENDPOINT, CHAT_MODEL, APP_INSIGHTS_CONN_STRING, and ingestion access.
// Check: dotnet run --project .\labs\10-observability -- --check
// Run:   dotnet run --project .\labs\10-observability
// Optional: add --online-eval to create an enabled preview rule that persists server-side.
// Expect: one model answer, a trace-flush confirmation, and optionally a rule response.

using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.Exporter;
using FoundryWorkshop.Shared;
using OpenTelemetry;
using OpenTelemetry.Trace;

const string sourceName = "FoundryWorkshop.M10";
using var activitySource = new ActivitySource(sourceName);

// Step 1: Configure Azure Monitor before starting the activity or creating the request.
return await LabHost.RunAsync(
    "M10 - Observability and tracing",
    args,
    async context =>
    {
        var connectionString = context.Config.Require(
            "APP_INSIGHTS_CONN_STRING",
            "Copy the full Application Insights connection string into .env.");
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(sourceName)
            .AddAzureMonitorTraceExporter(options => options.ConnectionString = connectionString)
            .Build();
        // Expected result:
        //   OpenTelemetry configured with Azure Monitor.
        //   TracerProvider ready.

        // Step 2: Start a client span and record operational tags, not prompt/response content.
        using var activity = activitySource.StartActivity(
            "foundry.responses",
            ActivityKind.Client);
        activity?.SetTag("gen_ai.system", "microsoft_foundry");
        activity?.SetTag("gen_ai.request.model", context.Config.ChatModel);
        activity?.SetTag("workshop.lab", "M10");
        // Expected result:
        //   foundry.responses span started with content recording disabled.

        // Step 3: Make the traced call; answer wording and response length are variable.
        using var response = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            input = "Explain an OpenTelemetry span in one sentence."
        });
        var text = JsonHelpers.GetOutputText(response.RootElement);
        activity?.SetTag("gen_ai.response.id", response.RootElement.GetProperty("id").GetString());
        activity?.SetTag("gen_ai.response.length", text.Length);
        Console.WriteLine(text);
        // Expected output:
        //   <model-generated one-sentence explanation of an OpenTelemetry span>

        // Step 4: Flush locally, then allow normal ingestion delay before querying App Insights.
        activity?.Stop();
        tracerProvider.ForceFlush();
        Console.WriteLine("Trace exported to Application Insights without recording prompt or response content.");
        // Expected output:
        //   Trace exported to Application Insights without recording prompt or response content.

        // Step 5: Optionally create an eval and persistent response-completed rule.
        if (context.HasFlag("--online-eval"))
        {
            using var evaluation = await context.Rest.SendProjectJsonAsync(
                HttpMethod.Post,
                "openai/v1/evals",
                new
                {
                    name = "C# workshop continuous relevance",
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
                                deployment_name = context.Config.ChatModel
                            }
                        }
                    }
                });
            var evaluationId = evaluation.RootElement.GetProperty("id").GetString();
            using var rule = await context.Rest.SendProjectJsonAsync(
                HttpMethod.Put,
                "evaluation_rules/csharp-workshop-relevance?api-version=2025-05-15-preview",
                new
                {
                    display_name = "C# workshop continuous relevance",
                    action = new { type = "continuous", eval_id = evaluationId, max_hourly_runs = 20 },
                    event_type = "response_completed",
                    enabled = true
                });
            Console.WriteLine($"Continuous evaluation rule: {rule.RootElement}");
            // Expected output with --online-eval:
            //   Continuous evaluation rule: <resource-specific JSON>
        }
        else
        {
            Console.WriteLine(
                "Add --online-eval to create the preview continuous relevance rule (it remains active until deleted).");
            // Expected output without --online-eval:
            //   Add --online-eval to create the preview continuous relevance rule (it remains active until deleted).
        }
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL",
    "APP_INSIGHTS_CONN_STRING");

// Your Turn: add a function tool with a child activity, query model/length tags, and
// create a second evaluation rule; disable workshop rules when the exercise is complete.
