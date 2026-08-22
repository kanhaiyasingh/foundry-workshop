using System.Diagnostics;
using System.Text.Json;
using Azure.Monitor.OpenTelemetry.Exporter;
using FoundryWorkshop.Shared;
using OpenTelemetry;
using OpenTelemetry.Trace;

const string sourceName = "FoundryWorkshop.M15";
using var activitySource = new ActivitySource(sourceName);

return await LabHost.RunAsync(
    "M15 - Capstone",
    args,
    async context =>
    {
        TracerProvider? tracerProvider = null;
        if (context.Config.IsConfigured("APP_INSIGHTS_CONN_STRING"))
        {
            tracerProvider = Sdk.CreateTracerProviderBuilder()
                .AddSource(sourceName)
                .AddAzureMonitorTraceExporter(options =>
                    options.ConnectionString = context.Config.Require("APP_INSIGHTS_CONN_STRING"))
                .Build();
        }

        using var activity = activitySource.StartActivity("contoso-support.run", ActivityKind.Client);
        activity?.SetTag("gen_ai.system", "microsoft_foundry");
        activity?.SetTag("gen_ai.request.model", context.Config.ChatModel);

        var tools = new object[]
        {
            new
            {
                type = "function",
                name = "get_order_status",
                description = "Get a Contoso order's current status.",
                parameters = new
                {
                    type = "object",
                    properties = new { order_id = new { type = "string" } },
                    required = new[] { "order_id" },
                    additionalProperties = false
                }
            },
            new
            {
                type = "function",
                name = "search_support_policy",
                description = "Search the approved support policy before making a policy claim.",
                parameters = new
                {
                    type = "object",
                    properties = new { query = new { type = "string" } },
                    required = new[] { "query" },
                    additionalProperties = false
                }
            }
        };

        using var first = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            instructions = """
                You are Contoso Support. Use tools for order facts and policy.
                Cite policy claims as [support-policy]. Be concise and never invent an order state.
                """,
            input = "Order C-1007 arrived damaged. What is its status and what can I do?",
            tools
        });
        var calls = JsonHelpers.GetFunctionCalls(first.RootElement).ToArray();
        if (calls.Length == 0)
        {
            throw new InvalidOperationException("Capstone expected tool calls but the model returned none.");
        }

        var outputs = new List<object>();
        foreach (var call in calls)
        {
            var name = call.GetProperty("name").GetString();
            using var arguments = JsonDocument.Parse(call.GetProperty("arguments").GetString() ?? "{}");
            object result = name switch
            {
                "get_order_status" => new
                {
                    order_id = arguments.RootElement.GetProperty("order_id").GetString(),
                    status = "delivered",
                    delivered_on = "2026-08-20",
                    carrier_case = "not-opened"
                },
                "search_support_policy" => new
                {
                    source = "support-policy",
                    text = "Damaged goods may be returned within 30 days. Open a carrier case before replacement."
                },
                _ => new { error = $"Unknown tool {name}." }
            };
            outputs.Add(new
            {
                type = "function_call_output",
                call_id = call.GetProperty("call_id").GetString(),
                output = JsonSerializer.Serialize(result)
            });
        }

        using var final = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            previous_response_id = first.RootElement.GetProperty("id").GetString(),
            input = outputs
        });
        var answer = JsonHelpers.GetOutputText(final.RootElement);
        Console.WriteLine(answer);

        var checks = new Dictionary<string, bool>
        {
            ["mentions delivered state"] = answer.Contains("delivered", StringComparison.OrdinalIgnoreCase),
            ["cites policy"] = answer.Contains("[support-policy]", StringComparison.OrdinalIgnoreCase),
            ["mentions carrier case"] = answer.Contains("carrier", StringComparison.OrdinalIgnoreCase)
        };
        foreach (var check in checks)
        {
            Console.WriteLine($"{(check.Value ? "PASS" : "FAIL")} {check.Key}");
        }

        activity?.SetTag("capstone.tool_call_count", calls.Length);
        activity?.SetTag("capstone.evaluation_pass_rate", checks.Values.Count(value => value) / (double)checks.Count);
        activity?.Stop();
        tracerProvider?.ForceFlush();
        tracerProvider?.Dispose();
        Console.WriteLine(
            $"Capstone score: {checks.Values.Count(value => value)}/{checks.Count}. " +
            (context.Config.IsConfigured("APP_INSIGHTS_CONN_STRING")
                ? "Trace exported."
                : "Set APP_INSIGHTS_CONN_STRING to export the trace."));
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL");
