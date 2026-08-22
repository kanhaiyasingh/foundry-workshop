using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.Exporter;
using FoundryWorkshop.Shared;
using OpenTelemetry;
using OpenTelemetry.Trace;

const string sourceName = "FoundryWorkshop.M10";
using var activitySource = new ActivitySource(sourceName);

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

        using var activity = activitySource.StartActivity(
            "foundry.responses",
            ActivityKind.Client);
        activity?.SetTag("gen_ai.system", "microsoft_foundry");
        activity?.SetTag("gen_ai.request.model", context.Config.ChatModel);
        activity?.SetTag("workshop.lab", "M10");

        using var response = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            input = "Explain an OpenTelemetry span in one sentence."
        });
        var text = JsonHelpers.GetOutputText(response.RootElement);
        activity?.SetTag("gen_ai.response.id", response.RootElement.GetProperty("id").GetString());
        activity?.SetTag("gen_ai.response.length", text.Length);
        Console.WriteLine(text);

        activity?.Stop();
        tracerProvider.ForceFlush();
        Console.WriteLine("Trace exported to Application Insights without recording prompt or response content.");

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
        }
        else
        {
            Console.WriteLine(
                "Add --online-eval to create the preview continuous relevance rule (it remains active until deleted).");
        }
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL",
    "APP_INSIGHTS_CONN_STRING");
