using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.AI.Projects.Agents;
using FoundryWorkshop.Shared;
using OpenAI.Responses;

#pragma warning disable OPENAI001

var offline = args.Any(arg => arg.Equals("--offline", StringComparison.OrdinalIgnoreCase));

// Cell 0 [markdown]
// # M13 - Human-in-the-Loop & REST
//
// First, pause an agent before an irreversible tool call and route the decision to
// a human. Then invoke that same named agent over raw REST: single-shot,
// previous_response_id multi-turn, and Server-Sent Events (SSE) streaming.
//
// Foundry returns custom function calls without running local implementations.
// The host decides whether to execute each call and sends function_call_output back.
// Sections 1-3 demonstrate that approval boundary; sections 4-6 use raw HTTP.
// The named/versioned agent API is preview and is pinned by the repository.
//
// The notebook includes docs/assets/agent-anatomy.png as its architecture diagram.
//
// Full guide: docs/modules/13-human-in-loop-rest.md
// Check:       dotnet run --project .\labs\13-human-in-loop-rest -- --check
// Run:         dotnet run --project .\labs\13-human-in-loop-rest
// Offline:     dotnet run --project .\labs\13-human-in-loop-rest -- --offline
// Interactive: dotnet run --project .\labs\13-human-in-loop-rest -- --interactive

// Cell 1 [code]
Console.WriteLine($"Current date and time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}");

return await LabHost.RunAsync(
    "M13 - Human-in-the-Loop & REST",
    args,
    async context =>
    {
        // Cells 2-4 [markdown/code]
        // ## 1. Configure & build the client
        //
        // Name the agent up front because SDK and raw REST calls both reference it
        // by name. WorkshopContext performs the notebook's .env, credential, project
        // client, and OpenAI client bootstrap.
        var projectEndpoint = offline ? "<offline fixture>" : context.Config.ProjectEndpoint;
        var chatModel = context.Config.ChatModel;
        const string agentName = "payments-approval-agent";

        Console.WriteLine($"Project    : {projectEndpoint}");
        Console.WriteLine($"Model      : {chatModel}");
        Console.WriteLine($"Agent name : {agentName}");

        // Expected live shape:
        // Project    : https://<account>.services.ai.azure.com/api/projects/<project>
        // Model      : gpt-4.1-mini
        // Agent name : payments-approval-agent

        // Cells 5-7 [markdown/code]
        // ## 2. Define tools - and create the agent
        //
        // get_account_balance is read-only and safe to auto-run. transfer_funds is
        // irreversible and must be intercepted. approvalRequiredTools is host policy,
        // not a safeguard enforced by the model or function schema. Both tool
        // implementations below are deterministic mocks and never move real money.
        var approvalRequiredTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "transfer_funds"
        };

        ResponseTool getBalanceTool = ResponseTool.CreateFunctionTool(
            functionName: "get_account_balance",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    account_id = new { type = "string" }
                },
                required = new[] { "account_id" }
            }),
            strictModeEnabled: false,
            functionDescription:
                "Get the current balance for an account. Safe to execute automatically.");

        ResponseTool transferTool = ResponseTool.CreateFunctionTool(
            functionName: "transfer_funds",
            functionParameters: BinaryData.FromObjectAsJson(new
            {
                type = "object",
                properties = new
                {
                    from_account = new { type = "string" },
                    to_account = new { type = "string" },
                    amount = new { type = "number" }
                },
                required = new[] { "from_account", "to_account", "amount" }
            }),
            strictModeEnabled: false,
            functionDescription:
                "Transfer funds between accounts. REQUIRES human approval before execution.");

        var definition = new DeclarativeAgentDefinition(chatModel)
        {
            Instructions =
                "You are a banking assistant with two tools: get_account_balance and " +
                "transfer_funds. Call the tool directly - do not describe what you will " +
                "do. The system handles human approval for transfer_funds."
        };
        definition.Tools.Add(getBalanceTool);
        definition.Tools.Add(transferTool);

        string activeAgentName;
        if (offline)
        {
            activeAgentName = agentName;
            Console.WriteLine(
                $"Agent '{agentName}' ready (offline fixture; no version was created).");
        }
        else
        {
            var projectClient = context.CreateProjectClient();
            _ = projectClient.ProjectOpenAIClient;
            var agentResult = await projectClient.AgentAdministrationClient
                .CreateAgentVersionAsync(
                    agentName,
                    new ProjectsAgentVersionCreationOptions(definition)
                    {
                        Description =
                            "HITL demo - financial transactions with human approval for transfers."
                    });
            ProjectsAgentVersion agent = agentResult.Value;
            activeAgentName = agent.Name;
            Console.WriteLine($"Agent '{agent.Name}' ready (version {agent.Version}).");
        }

        Console.WriteLine(
            $"Approval-required: " +
            $"{{{string.Join(", ", approvalRequiredTools.Select(name => $"'{name}'"))}}}");

        // Expected on a new live project:
        // Agent 'payments-approval-agent' ready (version 1).
        // Approval-required: {'transfer_funds'}
        //
        // The agent advertises both schemas. The host loop below, not the agent,
        // decides whether a proposed call runs. Reusing the stable name may create a
        // later version rather than version 1.

        var offlineResponses = offline ? new OfflineResponseService() : null;
        Func<object, Task<JsonDocument>> createResponse = offline
            ? offlineResponses!.CreateResponseAsync
            : body => context.Rest.CreateResponseAsync(body);

        // Cells 8-10 [markdown/code]
        // ## 3. The approval loop - approve & reject
        //
        // Scan each response for function_call items. Auto-execute safe tools; route
        // approval-required calls through the callback. Submit every result as a
        // function_call_output with previous_response_id and continue until a final
        // message arrives. In production the callback can be a UI, webhook, Teams
        // Adaptive Card, or asynchronous approval queue.
        var interactive = context.HasFlag("--interactive");
        Console.WriteLine(">>> APPROVE path");
        Console.WriteLine(await RunWithHitlAsync(
            createResponse,
            activeAgentName,
            "Transfer $500 from ACC-001 to ACC-002.",
            approvalRequiredTools,
            (name, toolArgs) =>
                GetApproval(name, toolArgs, defaultDecision: true, interactive)));

        Console.WriteLine("\n>>> REJECT path");
        Console.WriteLine(await RunWithHitlAsync(
            createResponse,
            activeAgentName,
            "Transfer $9000 from ACC-001 to ACC-002.",
            approvalRequiredTools,
            (name, toolArgs) =>
                GetApproval(name, toolArgs, defaultDecision: false, interactive)));

        // Expected shape:
        // >>> APPROVE path
        // [APPROVED] transfer_funds({...}) -> Transferred $500.00 ...
        // The transfer of $500.00 ... is complete.
        //
        // >>> REJECT path
        // [REJECTED] transfer_funds will not execute
        // I wasn't able to complete that transfer - it was rejected by the operator.
        //
        // Nothing executes until the host sends function_call_output. The rejection
        // string is itself a tool result, allowing the model to report the decline.

        using var httpClient = offline
            ? null
            : new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var responsesUrl = offline
            ? null
            : new Uri($"{projectEndpoint.TrimEnd('/')}/openai/v1/responses");

        Func<object, Task<(int StatusCode, JsonDocument Payload)>> sendResponse;
        if (offline)
        {
            sendResponse = async body =>
                (200, await offlineResponses!.CreateResponseAsync(body));
        }
        else
        {
            sendResponse = body => PostResponseAsync(
                context,
                httpClient!,
                responsesUrl!,
                body);
        }

        // Cells 11-13 [markdown/code]
        // ## 4. Drop to raw REST - single-shot
        //
        // Live requests POST to {endpoint}/openai/v1/responses with a bearer token
        // for https://ai.azure.com/.default. agent_reference is a top-level wire
        // property (the Python SDK supplied it through extra_body). The wire payload
        // has no top-level output_text convenience property, so GetOutputText
        // concatenates message content parts whose type is output_text.
        var singleBody = new
        {
            input = new[]
            {
                new
                {
                    role = "user",
                    content = "What is my balance for account ACC-001?"
                }
            },
            agent_reference =
                new { name = activeAgentName, type = "agent_reference" }
        };

        var (statusCode, result) = await PostResponseWithSafeToolsAsync(
            sendResponse,
            singleBody,
            activeAgentName,
            approvalRequiredTools);
        using (result)
        {
            Console.WriteLine($"HTTP   : {statusCode}");
            Console.WriteLine(
                $"Resp id: {result.RootElement.GetProperty("id").GetString()}");
            Console.WriteLine(
                $"Status : {result.RootElement.GetProperty("status").GetString()}");
            Console.WriteLine(
                $"Output : {JsonHelpers.GetOutputText(result.RootElement)}");
        }

        // Expected shape:
        // [AUTO] get_account_balance(...) -> Account ACC-001 balance: $5,000.00
        // HTTP   : 200
        // Resp id: resp_...
        // Status : completed
        // Output : Account ACC-001 has a balance of $5,000.00.
        //
        // A custom function schema does not deploy the local implementation to
        // Foundry. PostResponseWithSafeToolsAsync therefore remains raw REST while
        // the C# host executes only non-gated mock calls and submits their outputs.
        // It refuses any approval-gated call instead of executing it unattended.
        // A name-only reference selects the latest agent version; add version to pin.

        // Cells 14-16 [markdown/code]
        // ## 5. Multi-turn over REST - previous_response_id
        //
        // Do not resend history. The second body carries only the new user input and
        // previous_response_id; the service rehydrates prior state. This is the same
        // continuation primitive used for function_call_output in the HITL loop.
        var (_, turn1) = await sendResponse(new
        {
            input = new[]
            {
                new
                {
                    role = "user",
                    content = "Invent a one-line story about an astronaut named Mira."
                }
            },
            agent_reference =
                new { name = activeAgentName, type = "agent_reference" }
        });
        using (turn1)
        {
            Console.WriteLine($"Turn 1: {JsonHelpers.GetOutputText(turn1.RootElement)}");

            var (_, turn2) = await sendResponse(new
            {
                input = new[]
                {
                    new
                    {
                        role = "user",
                        content = "Now tell me what happens next, in one line."
                    }
                },
                previous_response_id =
                    turn1.RootElement.GetProperty("id").GetString(),
                agent_reference =
                    new { name = activeAgentName, type = "agent_reference" }
            });
            using (turn2)
            {
                Console.WriteLine(
                    $"Turn 2: {JsonHelpers.GetOutputText(turn2.RootElement)}");
            }
        }

        // Expected shape:
        // Turn 1: Mira drifted past Saturn's rings, humming a lullaby to the dark.
        // Turn 2: A reply hummed back - and Mira realised the dark had been listening.

        // Cells 17-19 [markdown/code]
        // ## 6. Streaming over REST - Server-Sent Events
        //
        // stream=true changes the content type to text/event-stream. Parse every
        // data: JSON record, count event types, and write response.output_text.delta
        // chunks as they arrive. Their concatenation equals non-streaming output_text.
        if (offline)
        {
            await StreamOfflineResponseAsync();
        }
        else
        {
            await StreamResponseAsync(
                context,
                httpClient!,
                responsesUrl!,
                activeAgentName);
        }

        // Tokens are short-lived. CreateRequestAsync obtains a current token for
        // every live raw request instead of retaining the section 4 token.

        // Cell 20 [markdown]
        // ## Your turn
        //
        // 1. Add close_account, add it to approvalRequiredTools, ask to close
        //    ACC-003, and reject it.
        // 2. Create a later version, add version = "1" to agent_reference, and prove
        //    the older behavior still answers.
        // 3. Stream a longer prompt. Expect more output_text.delta records but one
        //    response.completed.
        //
        // Cleanup and cost: a normal run creates a persistent agent version and uses
        // billable model tokens. Delete workshop versions in Foundry when finished.
        // --offline creates no agent, acquires no token, sends no HTTP request, and
        // exercises the approval, safe-tool, multi-turn, output aggregation, and SSE
        // parsing paths with labeled deterministic fixtures.
    },
    offline ? [] : ["PROJECT_ENDPOINT"]);

static async Task<string> RunWithHitlAsync(
    Func<object, Task<JsonDocument>> createResponse,
    string agentName,
    string userMessage,
    IReadOnlySet<string> approvalRequiredTools,
    Func<string, JsonElement, bool> approve)
{
    JsonDocument response = await createResponse(new
    {
        input = new[] { new { role = "user", content = userMessage } },
        agent_reference = new { name = agentName, type = "agent_reference" }
    });

    try
    {
        while (true)
        {
            var calls = JsonHelpers.GetFunctionCalls(response.RootElement).ToArray();
            if (calls.Length == 0)
            {
                return JsonHelpers.GetOutputText(response.RootElement);
            }

            var outputs = new List<object>();
            foreach (var call in calls)
            {
                var name = ReadRequiredString(call, "name", "function call");
                var callId = ReadRequiredString(call, "call_id", $"function call '{name}'");
                using var arguments = JsonDocument.Parse(
                    call.TryGetProperty("arguments", out var argumentValue)
                        ? argumentValue.GetString() ?? "{}"
                        : "{}");
                var toolArgs = arguments.RootElement;

                string result;
                if (approvalRequiredTools.Contains(name))
                {
                    if (approve(name, toolArgs))
                    {
                        result = ExecuteTool(name, toolArgs);
                        Console.WriteLine(
                            $"[APPROVED] {name}({toolArgs.GetRawText()}) -> {result}");
                    }
                    else
                    {
                        result = $"Action '{name}' was rejected by the operator.";
                        Console.WriteLine($"[REJECTED] {name} will not execute");
                    }
                }
                else
                {
                    result = ExecuteTool(name, toolArgs);
                    Console.WriteLine(
                        $"[AUTO] {name}({toolArgs.GetRawText()}) -> {result}");
                }

                outputs.Add(new
                {
                    type = "function_call_output",
                    call_id = callId,
                    output = result
                });
            }

            var previousResponseId = ReadRequiredString(
                response.RootElement,
                "id",
                "response");
            var next = await createResponse(new
            {
                input = outputs,
                previous_response_id = previousResponseId,
                agent_reference = new { name = agentName, type = "agent_reference" }
            });
            response.Dispose();
            response = next;
        }
    }
    finally
    {
        response.Dispose();
    }
}

static bool GetApproval(
    string name,
    JsonElement arguments,
    bool defaultDecision,
    bool interactive)
{
    if (!interactive)
    {
        return defaultDecision;
    }

    Console.Write($"Approve {name}({arguments.GetRawText()})? (y/n): ");
    var answer = Console.ReadLine();
    return answer?.Trim().ToLowerInvariant() switch
    {
        "y" or "yes" => true,
        "n" or "no" => false,
        null => throw new InvalidOperationException(
            "Approval input ended before a decision was read."),
        _ => throw new InvalidOperationException(
            "Approval must be 'y', 'yes', 'n', or 'no'.")
    };
}

static string ExecuteTool(string name, JsonElement arguments) =>
    name switch
    {
        "get_account_balance" => GetAccountBalance(
            ReadRequiredString(arguments, "account_id", "get_account_balance arguments")),
        "transfer_funds" => TransferFunds(
            ReadRequiredString(arguments, "from_account", "transfer_funds arguments"),
            ReadRequiredString(arguments, "to_account", "transfer_funds arguments"),
            arguments.TryGetProperty("amount", out var amount) &&
            amount.ValueKind == JsonValueKind.Number
                ? amount.GetDecimal()
                : throw new InvalidOperationException(
                    "transfer_funds arguments omitted numeric amount.")),
        _ => throw new InvalidOperationException(
            $"No implementation is registered for tool '{name}'.")
    };

static string GetAccountBalance(string accountId)
{
    var balance = accountId == "ACC-001" ? 5000m : 0m;
    return $"Account {accountId} balance: ${balance:N2}";
}

static string TransferFunds(string fromAccount, string toAccount, decimal amount) =>
    $"Transferred ${amount:N2} from {fromAccount} to {toAccount}.";

static async Task<(int StatusCode, JsonDocument Payload)> PostResponseAsync(
    WorkshopContext context,
    HttpClient httpClient,
    Uri responsesUrl,
    object body)
{
    using var request = await context.Rest.CreateRequestAsync(
        HttpMethod.Post,
        responsesUrl,
        FoundryRestClient.FoundryScope);
    request.Content = new StringContent(
        JsonSerializer.Serialize(body, JsonHelpers.Web),
        Encoding.UTF8,
        "application/json");

    using var response = await httpClient.SendAsync(request);
    var payload = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw new HttpRequestException(
            $"POST {responsesUrl} returned {(int)response.StatusCode} " +
            $"({response.ReasonPhrase}). {payload}",
            null,
            response.StatusCode);
    }

    return ((int)response.StatusCode, JsonDocument.Parse(payload));
}

static async Task<(int StatusCode, JsonDocument Payload)>
    PostResponseWithSafeToolsAsync(
        Func<object, Task<(int StatusCode, JsonDocument Payload)>> sendResponse,
        object body,
        string agentName,
        IReadOnlySet<string> approvalRequiredTools)
{
    var (statusCode, response) = await sendResponse(body);
    try
    {
        while (true)
        {
            var calls = JsonHelpers.GetFunctionCalls(response.RootElement).ToArray();
            if (calls.Length == 0)
            {
                return (statusCode, response);
            }

            var outputs = new List<object>();
            foreach (var call in calls)
            {
                var name = ReadRequiredString(call, "name", "function call");
                if (approvalRequiredTools.Contains(name))
                {
                    throw new InvalidOperationException(
                        $"Raw safe-tool completion refused approval-gated tool '{name}'.");
                }

                var callId = ReadRequiredString(
                    call,
                    "call_id",
                    $"function call '{name}'");
                using var arguments = JsonDocument.Parse(
                    call.TryGetProperty("arguments", out var argumentValue)
                        ? argumentValue.GetString() ?? "{}"
                        : "{}");
                var result = ExecuteTool(name, arguments.RootElement);
                Console.WriteLine(
                    $"[AUTO] {name}({arguments.RootElement.GetRawText()}) -> {result}");
                outputs.Add(new
                {
                    type = "function_call_output",
                    call_id = callId,
                    output = result
                });
            }

            var previousResponseId = ReadRequiredString(
                response.RootElement,
                "id",
                "response");
            var next = await sendResponse(new
            {
                input = outputs,
                previous_response_id = previousResponseId,
                agent_reference = new { name = agentName, type = "agent_reference" }
            });
            response.Dispose();
            (statusCode, response) = next;
        }
    }
    catch
    {
        response.Dispose();
        throw;
    }
}

static async Task StreamResponseAsync(
    WorkshopContext context,
    HttpClient httpClient,
    Uri responsesUrl,
    string agentName)
{
    var streamBody = new
    {
        input = new[]
        {
            new
            {
                role = "user",
                content = "Tell me a three-sentence story about a lighthouse keeper."
            }
        },
        agent_reference = new { name = agentName, type = "agent_reference" },
        stream = true
    };

    using var request = await context.Rest.CreateRequestAsync(
        HttpMethod.Post,
        responsesUrl,
        FoundryRestClient.FoundryScope);
    request.Headers.Accept.Add(
        new MediaTypeWithQualityHeaderValue("text/event-stream"));
    request.Content = new StringContent(
        JsonSerializer.Serialize(streamBody, JsonHelpers.Web),
        Encoding.UTF8,
        "application/json");

    using var response = await httpClient.SendAsync(
        request,
        HttpCompletionOption.ResponseHeadersRead);
    if (!response.IsSuccessStatusCode)
    {
        var error = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"Streaming response returned {(int)response.StatusCode}. {error}",
            null,
            response.StatusCode);
    }

    Console.WriteLine(
        $"content-type: {response.Content.Headers.ContentType?.MediaType ?? "<unknown>"}\n");
    await using var stream = await response.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream);
    await ConsumeSseAsync(reader);
}

static async Task StreamOfflineResponseAsync()
{
    var deltas = new[]
    {
        "Every night the keeper lit the lamp against the fog. ",
        "One storm, a small boat followed it home. ",
        "By dawn, the keeper had a new friend and a story worth telling."
    };
    var sse = new StringBuilder();
    AppendSse(sse, new { type = "response.created" });
    AppendSse(sse, new { type = "response.output_item.added" });
    foreach (var delta in deltas)
    {
        AppendSse(sse, new { type = "response.output_text.delta", delta });
    }

    AppendSse(sse, new { type = "response.output_text.done" });
    AppendSse(sse, new { type = "response.completed" });
    sse.AppendLine("data: [DONE]");

    Console.WriteLine("content-type: text/event-stream\n");
    using var reader = new StringReader(sse.ToString());
    await ConsumeSseAsync(reader);
}

static async Task ConsumeSseAsync(TextReader reader)
{
    var chunks = new List<string>();
    var eventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    while (await reader.ReadLineAsync() is { } line)
    {
        if (!line.StartsWith("data: ", StringComparison.Ordinal))
        {
            continue;
        }

        var payload = line[6..];
        if (payload == "[DONE]")
        {
            break;
        }

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        var eventType = root.TryGetProperty("type", out var type)
            ? type.GetString() ?? "<none>"
            : "<none>";
        eventCounts[eventType] = eventCounts.GetValueOrDefault(eventType) + 1;

        if (eventType == "response.output_text.delta" &&
            root.TryGetProperty("delta", out var delta))
        {
            var text = delta.GetString() ?? string.Empty;
            chunks.Add(text);
            Console.Write(text);
        }
    }

    Console.WriteLine("\n");
    Console.WriteLine($"Chars   : {chunks.Sum(chunk => chunk.Length)}");
    Console.WriteLine($"Events  : {JsonSerializer.Serialize(eventCounts)}");
}

static void AppendSse(StringBuilder builder, object value)
{
    builder.Append("data: ");
    builder.AppendLine(JsonSerializer.Serialize(value));
    builder.AppendLine();
}

static string ReadRequiredString(
    JsonElement element,
    string propertyName,
    string owner)
{
    if (!element.TryGetProperty(propertyName, out var value) ||
        value.ValueKind != JsonValueKind.String ||
        string.IsNullOrWhiteSpace(value.GetString()))
    {
        throw new InvalidOperationException(
            $"{owner} omitted string property '{propertyName}'.");
    }

    return value.GetString()!;
}

internal sealed class OfflineResponseService
{
    private static readonly JsonSerializerOptions Compact =
        new(JsonSerializerDefaults.Web);

    public Task<JsonDocument> CreateResponseAsync(object body)
    {
        using var bodyJson = JsonDocument.Parse(
            JsonSerializer.Serialize(body, JsonHelpers.Web));
        var root = bodyJson.RootElement;

        if (root.TryGetProperty("previous_response_id", out var previousIdValue))
        {
            var previousId = previousIdValue.GetString() ??
                throw new InvalidOperationException(
                    "Offline response fixture received a null previous_response_id.");
            return Task.FromResult(Continue(previousId, root));
        }

        var userText = ReadUserText(root);
        if (userText.Contains("Transfer $500", StringComparison.Ordinal))
        {
            return Task.FromResult(FunctionCall(
                "offline-transfer-approved",
                "call-transfer-approved",
                "transfer_funds",
                new
                {
                    from_account = "ACC-001",
                    to_account = "ACC-002",
                    amount = 500
                }));
        }

        if (userText.Contains("Transfer $9000", StringComparison.Ordinal))
        {
            return Task.FromResult(FunctionCall(
                "offline-transfer-rejected",
                "call-transfer-rejected",
                "transfer_funds",
                new
                {
                    from_account = "ACC-001",
                    to_account = "ACC-002",
                    amount = 9000
                }));
        }

        if (userText.Contains("balance for account ACC-001", StringComparison.Ordinal))
        {
            return Task.FromResult(FunctionCall(
                "offline-balance",
                "call-balance",
                "get_account_balance",
                new { account_id = "ACC-001" }));
        }

        if (userText.Contains("astronaut named Mira", StringComparison.Ordinal))
        {
            return Task.FromResult(Message(
                "offline-story-1",
                "Mira drifted past Saturn's rings, humming a lullaby to the dark."));
        }

        throw new InvalidOperationException(
            $"Offline response fixture has no case for input: {userText}");
    }

    private static JsonDocument Continue(string previousId, JsonElement body)
    {
        if (previousId == "offline-story-1")
        {
            return Message(
                "offline-story-2",
                "A reply hummed back - and Mira realised the dark had been listening.");
        }

        var output = ReadFunctionOutput(body);
        if (previousId == "offline-balance")
        {
            if (!output.Contains(
                    "Account ACC-001 balance: $5,000.00",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Offline balance continuation received an unexpected tool result.");
            }

            return Message(
                "offline-balance-final",
                "Account ACC-001 has a balance of $5,000.00.");
        }

        if (previousId == "offline-transfer-approved")
        {
            if (!output.Contains("Transferred $500.00", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Offline approval continuation received an unexpected tool result.");
            }

            return Message(
                "offline-transfer-approved-final",
                "The transfer of $500.00 from ACC-001 to ACC-002 is complete.");
        }

        if (previousId == "offline-transfer-rejected")
        {
            if (!output.Contains("rejected by the operator", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Offline rejection continuation received an unexpected tool result.");
            }

            return Message(
                "offline-transfer-rejected-final",
                "I wasn't able to complete that transfer - it was rejected by the operator.");
        }

        throw new InvalidOperationException(
            $"Offline response fixture cannot continue response '{previousId}'.");
    }

    private static string ReadUserText(JsonElement body)
    {
        if (!body.TryGetProperty("input", out var input) ||
            input.ValueKind != JsonValueKind.Array ||
            input.GetArrayLength() == 0 ||
            !input[0].TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Offline response fixture expected input[0].content.");
        }

        return content.GetString() ?? string.Empty;
    }

    private static string ReadFunctionOutput(JsonElement body)
    {
        if (!body.TryGetProperty("input", out var input) ||
            input.ValueKind != JsonValueKind.Array ||
            input.GetArrayLength() == 0 ||
            !input[0].TryGetProperty("type", out var type) ||
            type.GetString() != "function_call_output" ||
            !input[0].TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Offline continuation expected a function_call_output item.");
        }

        return output.GetString() ?? string.Empty;
    }

    private static JsonDocument FunctionCall(
        string responseId,
        string callId,
        string name,
        object arguments) =>
        JsonDocument.Parse(JsonSerializer.Serialize(
            new
            {
                id = responseId,
                status = "completed",
                output = new[]
                {
                    new
                    {
                        type = "function_call",
                        call_id = callId,
                        name,
                        arguments = JsonSerializer.Serialize(arguments, Compact)
                    }
                }
            },
            Compact));

    private static JsonDocument Message(string responseId, string text) =>
        JsonDocument.Parse(JsonSerializer.Serialize(
            new
            {
                id = responseId,
                status = "completed",
                output = new[]
                {
                    new
                    {
                        type = "message",
                        content = new[]
                        {
                            new { type = "output_text", text }
                        }
                    }
                }
            },
            Compact));
}
