// M3 objective: execute a host-side function-call loop and optionally use Code Interpreter.
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
        }
        else
        {
            Console.WriteLine("Add --code-interpreter to run the hosted Code Interpreter example.");
        }
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL");

// Your Turn: add a second function that can be called in the same turn, then remove
// get_weather and confirm the model no longer presents the mock weather as grounded fact.
