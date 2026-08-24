// M15 objective: combine grounded tools, deterministic evaluation, and optional tracing.
// Prerequisites: PROJECT_ENDPOINT, CHAT_MODEL, M3/M4/M9/M10 concepts, and optional
// APP_INSIGHTS_CONN_STRING.
// Check: dotnet run --project .\labs\15-capstone -- --check
// Run:   dotnet run --project .\labs\15-capstone
// Expect: a tool-grounded C-1007 answer, three PASS checks, and a 3/3 score.

using System.Diagnostics;
using System.Text.Json;
using Azure.Monitor.OpenTelemetry.Exporter;
using FoundryWorkshop.Shared;
using OpenTelemetry;
using OpenTelemetry.Trace;

const string sourceName = "FoundryWorkshop.M15";
using var activitySource = new ActivitySource(sourceName);

// Step 1: Start optional Azure Monitor tracing without making it a functional dependency.
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
        // Expected result:
        //   Tracing ready when APP_INSIGHTS_CONN_STRING is configured.

        using var activity = activitySource.StartActivity("contoso-support.run", ActivityKind.Client);
        activity?.SetTag("gen_ai.system", "microsoft_foundry");
        activity?.SetTag("gen_ai.request.model", context.Config.ChatModel);

        // Step 2: Define one order-fact tool and one approved-policy retrieval tool.
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
        // Expected result:
        //   Tools defined: get_order_status, search_support_policy

        // Step 3: Ask the fixed damaged-order question and require at least one tool call.
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
        // Expected result:
        //   The response contains one or more function calls.

        // Step 4: Execute model-proposed calls in trusted C# against deterministic workshop data.
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
        // Expected result:
        //   Order C-1007: delivered, delivered_on 2026-08-20, carrier_case not-opened.
        //   Policy: damaged goods may be returned within 30 days; open a carrier case first.

        // Step 5: Return tool outputs and require the model to compose a cited support answer.
        using var final = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            previous_response_id = first.RootElement.GetProperty("id").GetString(),
            input = outputs
        });
        var answer = JsonHelpers.GetOutputText(final.RootElement);
        Console.WriteLine(answer);
        // Expected output:
        //   <model-generated answer describing delivered status, carrier case, and [support-policy]>

        // Step 6: Apply three transparent release checks; model wording may vary, score should be 3/3.
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
        // Expected output:
        //   PASS mentions delivered state
        //   PASS cites policy
        //   PASS mentions carrier case

        // Step 7: Add tool/evaluation tags to the optional trace and flush it.
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
        // Expected output:
        //   Capstone score: 3/3. <Trace exported. | Set APP_INSIGHTS_CONN_STRING to export the trace.>
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL");

// Your Turn: replace policy lookup with M4, apply M11 guardrails, run M12 attacks,
// and add the worst cases to M9 while retaining the three capstone checks as a gate.
