// M3 objective: execute a host-side function-call loop and optionally use Code Interpreter.
// Full guide: docs/modules/03-tools-and-function-calling.md
// Prerequisites: PROJECT_ENDPOINT, a tool-capable CHAT_MODEL, and optional Code Interpreter access.
// Check: dotnet run --project .\labs\03-tools-and-function-calling -- --check
// Run:   dotnet run --project .\labs\03-tools-and-function-calling
// Optional: add --code-interpreter for the separate hosted statistics request.
// Expect: a printed mock tool execution, a grounded answer, and optional statistics.

using System.Text.Json;
using FoundryWorkshop.Shared;

// Step 1: Declare the strict function schema the model uses to propose arguments.
return await LabHost.RunAsync(
    "M3 - Tools and function calling",
    args,
    async context =>
    {
        var tool = new
        {
            type = "function",
            name = "get_weather",
            description = "Return the current workshop weather for a city.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    city = new { type = "string" },
                    unit = new { type = "string", @enum = new[] { "celsius", "fahrenheit" } }
                },
                required = new[] { "city" },
                additionalProperties = false
            }
        };
        // Expected result:
        //   Declared tool: get_weather

        // Step 2: Ask a question that should require the weather function.
        using var initial = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            input = "What is the weather in Seattle? Use the weather tool.",
            tools = new[] { tool }
        });

        var calls = JsonHelpers.GetFunctionCalls(initial.RootElement).ToArray();
        if (calls.Length == 0)
        {
            throw new InvalidOperationException(
                "The model returned no function call. Confirm the deployment supports Responses tools.");
        }
        // Expected result:
        //   The response contains at least one get_weather function call.

        // Step 3: Validate model-proposed arguments and execute deterministic C# host logic.
        var outputs = new List<object>();
        foreach (var call in calls)
        {
            var name = call.GetProperty("name").GetString();
            var callId = call.GetProperty("call_id").GetString();
            using var arguments = JsonDocument.Parse(call.GetProperty("arguments").GetString() ?? "{}");
            var city = arguments.RootElement.GetProperty("city").GetString() ?? "Seattle";
            var result = JsonSerializer.Serialize(new
            {
                city,
                temperatureC = 18,
                conditions = "light rain",
                observedBy = "workshop mock service"
            });
            Console.WriteLine($"Executing {name}({city}) -> {result}");
            // Expected output:
            //   Executing get_weather(Seattle) -> {"city":"Seattle","temperatureC":18,
            //   "conditions":"light rain","observedBy":"workshop mock service"}
            outputs.Add(new { type = "function_call_output", call_id = callId, output = result });
        }

        // Step 4: Return tool output to the open response and print the model's synthesis.
        using var completed = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            previous_response_id = initial.RootElement.GetProperty("id").GetString(),
            input = outputs
        });
        Console.WriteLine(JsonHelpers.GetOutputText(completed.RootElement));
        // Expected output:
        //   <model-generated answer using the Seattle tool result>

        // Step 5: Optionally run hosted code over inline data; numeric formatting may vary.
        if (context.HasFlag("--code-interpreter"))
        {
            using var codeResult = await context.Rest.CreateResponseAsync(new
            {
                model = context.Config.ChatModel,
                input = "Use Python to calculate the mean and population standard deviation of 4, 8, 15, 16, 23, 42.",
                tools = new object[]
                {
                    new { type = "code_interpreter", container = new { type = "auto" } }
                }
            });
            Console.WriteLine($"Code Interpreter: {JsonHelpers.GetOutputText(codeResult.RootElement)}");
            // Expected output with --code-interpreter:
            //   Code Interpreter: <model-generated mean and population standard deviation>
        }
        else
        {
            Console.WriteLine("Add --code-interpreter to run the hosted Code Interpreter example.");
            // Expected output without --code-interpreter:
            //   Add --code-interpreter to run the hosted Code Interpreter example.
        }
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL");

// Your Turn:
// 1. Add a second function tool. Declare convert_currency(amount, from, to), attach it
//    alongside get_weather, and ask a question that forces both calls in one turn. The
//    existing calls loop already handles multiple function_call items.
// 2. Make Code Interpreter draw. Run with --code-interpreter after changing its prompt
//    to request a bar-chart PNG. Inspect codeResult.RootElement for the
//    container_file_citation, then retrieve the cited container file through the
//    authenticated Responses/container REST endpoint.
// 3. Starve the model. Remove get_weather from tools but keep the weather question.
//    Watch the model refuse or hedge, proving the tool supplied the facts.
