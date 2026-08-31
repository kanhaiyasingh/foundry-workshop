// Cell 0 [markdown]
// M3 - Tools & Function Calling
//
// Goal: give an agent tools - first Foundry's hosted Code Interpreter, then a
// custom function - and watch the model decide when to call them.
// You'll use: ResponseTool.CreateCodeInterpreterTool,
// ResponseTool.CreateFunctionTool, DeclarativeAgentDefinition.Tools, and the
// function_call -> function_call_output loop.
//
// The M2 agent could only talk. Tools let it act: run code, look things up, or
// call your APIs. Foundry supports two flavours:
//
// - Hosted tools such as Code Interpreter run inside Foundry. Attach them and
//   the service executes them.
// - Custom function tools run in your code. The model emits a structured call;
//   your host executes it and feeds the result back.
//
// See docs/assets/agent-anatomy.png for the anatomy of a Foundry agent.
//
// Tool APIs are evolving. The Python notebook uses PromptAgentDefinition,
// CodeInterpreterTool, AutoCodeInterpreterToolParam, and FunctionTool. With the
// repository's current C# packages, their equivalents are
// DeclarativeAgentDefinition, ResponseTool.CreateCodeInterpreterTool with an
// automatic CodeInterpreterToolContainer, and
// ResponseTool.CreateFunctionTool. The tool and response wire shapes are the
// same.
//
// Full guide: docs/modules/03-tools-and-function-calling.md
// Check: dotnet run --project .\labs\03-tools-and-function-calling -- --check
// Run:   dotnet run --project .\labs\03-tools-and-function-calling
// Smoke only one half: add --code-interpreter-only or --function-only.

using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects.Agents;
using FoundryWorkshop.Shared;
using OpenAI.Files;
using OpenAI.Responses;
using System.Text;
using System.Text.Json;

#pragma warning disable OPENAI001

// Notebook cell 1: print the current date and time.
Console.WriteLine($"Current date and time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}");

return await LabHost.RunAsync(
    "M3 - Tools and function calling",
    args,
    async context =>
    {
        var codeInterpreterOnly = context.HasFlag("--code-interpreter-only");
        var functionOnly = context.HasFlag("--function-only");
        if (codeInterpreterOnly && functionOnly)
        {
            throw new ArgumentException(
                "--code-interpreter-only and --function-only cannot be combined.");
        }

        // Cell 2 [markdown]
        // ## 1. Configure & build the client
        //
        // This is the familiar M1 bootstrap. Keep a handle on the OpenAI file
        // client to hand data to Code Interpreter and on AgentAdministrationClient
        // to define tool-equipped agents.

        // Cell 3 [code]
        // WorkshopConfig loads the repository .env without overwriting existing
        // environment variables. CreateProjectClient uses the configured
        // DefaultAzureCredential (or AzureCliCredential when AZURE_AUTH_MODE=cli).
        var chatModel = context.Config.ChatModel;
        var projectClient = context.CreateProjectClient();
        var openAiClient = projectClient.ProjectOpenAIClient;
        var fileClient = openAiClient.GetOpenAIFileClient();

        Console.WriteLine($"Chat model : {chatModel}");
        Console.WriteLine("clients    : ready");

        // Cell 4 [markdown]
        // Expected output:
        //   Chat model : gpt-4.1-mini
        //   clients    : ready

        if (!functionOnly)
        {
            // Cell 5 [markdown]
            // ## 2. Upload data for Code Interpreter
            //
            // Code Interpreter runs Python in a sandboxed container. To analyse a
            // file, upload it with purpose="assistants"; the returned file id is
            // attached to the agent. Here the same tiny CSV is synthesized in
            // memory and uploaded without creating a local file.

            // Cell 6 [code]
            const string csv =
                "sector,quarter,operating_profit\n" +
                "TRANSPORTATION,Q1,120\n" +
                "TRANSPORTATION,Q2,135\n" +
                "TRANSPORTATION,Q3,128\n" +
                "TRANSPORTATION,Q4,150\n";

            using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var uploadResult = await fileClient.UploadFileAsync(
                csvStream,
                "quarterly_results.csv",
                FileUploadPurpose.Assistants);
            OpenAIFile uploadedFile = uploadResult.Value;
            Console.WriteLine($"Uploaded file id: {uploadedFile.Id}");

            // Cell 7 [markdown]
            // Expected output:
            //   Uploaded file id: assistant-7xKQ...e2
            //
            // Current services commonly return a file-... id. Regardless of its
            // prefix, the file now lives in the project's Files store, ready for
            // agents that are granted access to it.

            // Cell 8 [markdown]
            // ## 3. Create an agent with the Code Interpreter tool
            //
            // Attach the hosted tool through the agent definition's Tools
            // collection. The automatic container provisions a managed sandbox
            // and preloads the uploaded file id, so the agent can read the CSV as
            // soon as it runs.

            // Cell 9 [code]
            DeclarativeAgentDefinition analystDefinition =
                new DeclarativeAgentDefinition(chatModel)
                {
                    Instructions =
                        "You are a helpful data analyst. Use Python to answer questions about uploaded files."
                };
            analystDefinition.Tools.Add(
                ResponseTool.CreateCodeInterpreterTool(
                    new CodeInterpreterToolContainer(
                        CodeInterpreterToolContainerConfiguration
                            .CreateAutomaticContainerConfiguration(
                                [uploadedFile.Id]))));

            var analystResult = await projectClient.AgentAdministrationClient
                .CreateAgentVersionAsync(
                    "data-analyst-agent",
                    new ProjectsAgentVersionCreationOptions(analystDefinition)
                    {
                        Description =
                            "Analyses uploaded CSVs with sandboxed Python."
                    });
            ProjectsAgentVersion analyst = analystResult.Value;

            Console.WriteLine($"Agent   : {analyst.Name}");
            Console.WriteLine($"Version : {analyst.Version}");

            // Cell 10 [markdown]
            // Expected output:
            //   Agent   : data-analyst-agent
            //   Version : 1
            //
            // This is the same create-version pattern as M2. Tools are another
            // field on the definition, so they are versioned with it. Reruns may
            // return the same version when the definition is unchanged.

            // Cell 11 [markdown]
            // ## 4. Let the agent run code
            //
            // Ask a question that requires computation. The agent writes Python
            // against the CSV, runs it in the managed container, and returns the
            // answer. Invocation uses the same agent_reference shape as M2.

            // Cell 12 [code]
            using var codeResponse = await CreateAndAwaitAgentResponseAsync(
                context,
                new
                {
                    input =
                        "From the uploaded CSV, which quarter had the highest operating profit " +
                        "for the TRANSPORTATION sector, and what was the full-year total?",
                    agent_reference =
                        new
                        {
                            name = analyst.Name,
                            type = "agent_reference"
                        }
                });
            Console.WriteLine(JsonHelpers.GetOutputText(codeResponse.RootElement));

            // Cell 13 [markdown]
            // Expected output:
            //   Q4 had the highest operating profit for TRANSPORTATION at 150. The
            //   full-year total across Q1-Q4 was 533.
            //
            // Hosted means you do not run it: Code Interpreter executes entirely
            // inside Foundry. Each conversation gets a sandbox session with an
            // idle timeout of about 30 minutes. Charts and files come back as
            // container_file_citation annotations that can be downloaded - a
            // useful next experiment.
        }

        if (!codeInterpreterOnly)
        {
            // Cell 14 [markdown]
            // ## 5. Define a custom function tool
            //
            // For your own logic, declare a name, description, and JSON Schema.
            // This is only a declaration: the model decides when and with which
            // arguments to call it. The implementation remains in this C# host.

            // Cell 15 [code]
            var weatherParameters = BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    city = new
                    {
                        type = "string",
                        description = "City name, e.g. 'Zurich'"
                    },
                    unit = new
                    {
                        type = "string",
                        @enum = new[] { "celsius", "fahrenheit" },
                        description = "Temperature unit"
                    }
                },
                required = new[] { "city" }
            });
            ResponseTool getWeatherTool = ResponseTool.CreateFunctionTool(
                functionName: "get_weather",
                functionParameters: weatherParameters,
                strictModeEnabled: false,
                functionDescription:
                    "Get the current weather for a city. Call this whenever a user asks about weather.");

            Console.WriteLine("Declared tool: get_weather");

            // The real implementation is GetWeather below. It uses the notebook's
            // Zurich/Cairo/Oslo mock values, defaults unknown cities to 21 C, and
            // performs the same Celsius-to-Fahrenheit conversion.

            // Cell 16 [markdown]
            // Expected output:
            //   Declared tool: get_weather
            //
            // The schema is the contract the model reads. A crisp description for
            // each tool and parameter is the biggest lever on whether the model
            // calls it correctly.

            // Cell 17 [markdown]
            // ## 6. Wire the function-calling loop
            //
            // Function tools need a round-trip: the model returns function_call
            // instead of text; the host executes it; then the host sends
            // function_call_output keyed by call_id. previous_response_id lets the
            // agent continue the same exchange. Loop until no calls remain.

            // Cell 18 [code]
            DeclarativeAgentDefinition weatherDefinition =
                new DeclarativeAgentDefinition(chatModel)
                {
                    Instructions =
                        "You are a travel assistant. Use the get_weather tool to answer weather questions; don't guess."
                };
            weatherDefinition.Tools.Add(getWeatherTool);

            var weatherAgentResult = await projectClient.AgentAdministrationClient
                .CreateAgentVersionAsync(
                    "weather-agent",
                    new ProjectsAgentVersionCreationOptions(weatherDefinition));
            ProjectsAgentVersion weatherAgent = weatherAgentResult.Value;

            JsonDocument response = await CreateAndAwaitAgentResponseAsync(
                context,
                new
                {
                    input = new[]
                    {
                        new
                        {
                            role = "user",
                            content =
                                "Should I pack a coat for Oslo? What's it like there now?"
                        }
                    },
                    agent_reference =
                        new
                        {
                            name = weatherAgent.Name,
                            type = "agent_reference"
                        }
                });

            try
            {
                while (true)
                {
                    var calls = JsonHelpers.GetFunctionCalls(response.RootElement)
                        .ToArray();
                    if (calls.Length == 0)
                    {
                        break;
                    }

                    var toolOutputs = new List<object>();
                    foreach (var call in calls)
                    {
                        var callId = RequireString(call, "call_id");
                        var functionName = RequireString(call, "name");
                        if (!string.Equals(
                                functionName,
                                "get_weather",
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"The agent requested unsupported tool '{functionName}'.");
                        }

                        using var arguments = JsonDocument.Parse(
                            RequireString(call, "arguments"));
                        var weatherArguments =
                            ParseAndValidateWeatherArguments(arguments.RootElement);
                        var result = GetWeather(
                            weatherArguments.City,
                            weatherArguments.Unit);
                        Console.WriteLine(
                            $"[tool] {functionName}(" +
                            $"{FormatPythonArguments(weatherArguments)}) -> {result}");
                        toolOutputs.Add(new
                        {
                            type = "function_call_output",
                            call_id = callId,
                            output = result
                        });
                    }

                    var previousResponseId =
                        RequireString(response.RootElement, "id");
                    response.Dispose();
                    response = await CreateAndAwaitAgentResponseAsync(
                        context,
                        new
                        {
                            input = toolOutputs,
                            previous_response_id = previousResponseId,
                            agent_reference =
                                new
                                {
                                    name = weatherAgent.Name,
                                    type = "agent_reference"
                                }
                        });
                }

                Console.WriteLine();
                Console.WriteLine(
                    JsonHelpers.GetOutputText(response.RootElement));
            }
            finally
            {
                response.Dispose();
            }

            // Cell 19 [markdown]
            // Expected output:
            //   [tool] get_weather({'city': 'Oslo'}) -> Oslo: 7 C, partly cloudy.
            //
            //   Yes - pack a coat. It's about 7 C and partly cloudy in Oslo right
            //   now, so a warm layer will be welcome.
            //
            // The real output uses the degree symbol shown by the notebook. The
            // model chose get_weather, this host executed it locally, and the agent
            // wove the deterministic result into a natural answer.

            // Cell 20 [markdown]
            // Validate tool arguments. The model proposes them, so treat them as
            // untrusted input. Validate and authorize before doing anything
            // irreversible. The same loop extends to a human-in-the-loop gate:
            // intercept sensitive calls, get approval, then run them.
        }

        // Cell 21 [markdown]
        // ## Your turn
        //
        // 1. Add convert_currency(amount, from, to), attach it beside get_weather,
        //    and ask a question that forces both calls in one turn. The loop already
        //    handles multiple function_call items per response.
        // 2. Ask the Code Interpreter agent for a bar-chart PNG. Read the
        //    container_file_citation annotation and download it with the C#
        //    ContainerClient returned by openAiClient.GetContainerClient().
        // 3. Remove get_weather from Tools but keep the weather question. Watch the
        //    model refuse or hedge, proving the tool supplied the facts.
        //
        // Your agent can now run hosted code and call your own functions. Next:
        // ground it in your knowledge so answers are backed by real sources in M4.
        //
        // Lifecycle note: like the notebook, this run leaves the uploaded
        // quarterly_results.csv file and the data-analyst-agent/weather-agent
        // versions in the Foundry project for inspection and reruns. The Code
        // Interpreter container is service-managed and ephemeral. See the guide
        // for cleanup and cost details.
    },
    "PROJECT_ENDPOINT");

static async Task<JsonDocument> CreateAndAwaitAgentResponseAsync(
    WorkshopContext context,
    object body)
{
    JsonDocument response = await context.Rest.CreateResponseAsync(body);
    var deadline = DateTimeOffset.UtcNow.AddMinutes(5);

    while (true)
    {
        var root = response.RootElement;
        if (!root.TryGetProperty("status", out var statusElement) ||
            statusElement.ValueKind != JsonValueKind.String)
        {
            return response;
        }

        var status = statusElement.GetString();
        if (string.Equals(status, "completed", StringComparison.Ordinal))
        {
            return response;
        }

        if (status is "failed" or "cancelled" or "incomplete")
        {
            var error = root.TryGetProperty("error", out var errorElement)
                ? errorElement.GetRawText()
                : "No error payload was returned.";
            response.Dispose();
            throw new InvalidOperationException(
                $"Agent response ended with status '{status}'. {error}");
        }

        if (DateTimeOffset.UtcNow >= deadline)
        {
            var responseId = root.TryGetProperty("id", out var timedOutId)
                ? timedOutId.GetString()
                : null;
            response.Dispose();
            throw new TimeoutException(
                $"Agent response '{responseId}' did not complete within five minutes.");
        }

        var id = RequireString(root, "id");
        response.Dispose();
        await Task.Delay(TimeSpan.FromSeconds(1));
        response = await context.Rest.SendProjectJsonAsync(
            HttpMethod.Get,
            $"openai/v1/responses/{Uri.EscapeDataString(id)}");
    }
}

static WeatherArguments ParseAndValidateWeatherArguments(JsonElement arguments)
{
    if (arguments.ValueKind != JsonValueKind.Object)
    {
        throw new InvalidOperationException(
            "get_weather arguments must be a JSON object.");
    }

    foreach (var property in arguments.EnumerateObject())
    {
        if (property.Name is not ("city" or "unit"))
        {
            throw new InvalidOperationException(
                $"get_weather received unsupported argument '{property.Name}'.");
        }
    }

    if (!arguments.TryGetProperty("city", out var cityElement) ||
        cityElement.ValueKind != JsonValueKind.String ||
        string.IsNullOrWhiteSpace(cityElement.GetString()))
    {
        throw new InvalidOperationException(
            "get_weather requires a non-empty string 'city'.");
    }

    var unit = "celsius";
    var unitWasProvided = arguments.TryGetProperty(
        "unit",
        out var unitElement);
    if (unitWasProvided)
    {
        if (unitElement.ValueKind != JsonValueKind.String ||
            unitElement.GetString() is not ("celsius" or "fahrenheit"))
        {
            throw new InvalidOperationException(
                "get_weather 'unit' must be 'celsius' or 'fahrenheit'.");
        }

        unit = unitElement.GetString()!;
    }

    return new WeatherArguments(
        cityElement.GetString()!,
        unit,
        unitWasProvided);
}

static string GetWeather(string city, string unit = "celsius")
{
    var temperatures = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Zurich"] = 18,
        ["Cairo"] = 34,
        ["Oslo"] = 7
    };
    var temperature = temperatures.GetValueOrDefault(city, 21);
    if (string.Equals(unit, "fahrenheit", StringComparison.Ordinal))
    {
        temperature = (int)Math.Round(
            temperature * 9d / 5d + 32d,
            MidpointRounding.ToEven);
    }

    var suffix = unit == "fahrenheit" ? "F" : "C";
    return $"{city}: {temperature}\u00b0{suffix}, partly cloudy.";
}

static string FormatPythonArguments(WeatherArguments arguments)
{
    var city = EscapePythonString(arguments.City);
    if (!arguments.UnitWasProvided)
    {
        return $"{{'city': '{city}'}}";
    }

    return $"{{'city': '{city}', 'unit': '{arguments.Unit}'}}";
}

static string EscapePythonString(string value) =>
    value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "\\'", StringComparison.Ordinal);

static string RequireString(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property) ||
        property.ValueKind != JsonValueKind.String ||
        string.IsNullOrEmpty(property.GetString()))
    {
        throw new InvalidOperationException(
            $"Response property '{propertyName}' must be a non-empty string.");
    }

    return property.GetString()!;
}

internal sealed record WeatherArguments(
    string City,
    string Unit,
    bool UnitWasProvided);
