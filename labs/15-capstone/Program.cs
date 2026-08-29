using System.Diagnostics;
using System.Text.Json;
using Azure.AI.Projects.Agents;
using Azure.Monitor.OpenTelemetry.Exporter;
using FoundryWorkshop.Shared;
using OpenAI.Responses;
using OpenTelemetry;
using OpenTelemetry.Trace;

#pragma warning disable OPENAI001

// # M15 - Capstone
//
// Goal: combine everything - a grounded, tool-using, evaluated, observable agent -
// into one coherent build, then see where to go next.
//
// You'll use: DeclarativeAgentDefinition (the C# PromptAgentDefinition equivalent)
// with tools + knowledge, the Responses API, an evaluator, and tracing.
//
// This is the victory lap. The build is a single "Contoso Support" agent that is
// ready for M4 grounding, calls an M3 custom tool, is measured before release as in
// M9, and can export M10 traces.
//
// Full guide: docs/modules/15-capstone.md
// Check: dotnet run --project .\labs\15-capstone -- --check
// Run:   dotnet run --project .\labs\15-capstone

return await LabHost.RunAsync(
    "M15 - Capstone",
    args,
    async context =>
    {
        // The notebook can configure tracing in its final cell and then re-run earlier
        // cells interactively. A one-shot console process cannot, so initialize the
        // optional provider before any traced operation and report it in Cell 17.
        using TracerProvider? tracerProvider =
            context.Config.IsConfigured("APP_INSIGHTS_CONN_STRING")
                ? Sdk.CreateTracerProviderBuilder()
                    .AddSource(CapstoneTelemetry.SourceName)
                    .AddAzureMonitorTraceExporter(options =>
                        options.ConnectionString =
                            context.Config.Require("APP_INSIGHTS_CONN_STRING"))
                    .Build()
                : null;

        // ## 1. Bootstrap (the pattern you now know by heart)
        //
        // Same four lines from M1: one project client and one OpenAI-compatible
        // client, reused for everything.

        var projectEndpoint = context.Config.ProjectEndpoint;
        var chatModel = context.Config.ChatModel;
        var projectClient = context.CreateProjectClient();
        var openAiClient = projectClient.ProjectOpenAIClient;

        Console.WriteLine($"Ready to build the capstone agent on: {chatModel}");

        // Expected output:
        //   Ready to build the capstone agent on: gpt-4.1-mini

        // ## 2. A tool the agent can call
        //
        // Give the support agent one custom function tool - looking up an order's
        // status - exactly as in M3. In production this function would call the order
        // system; the workshop implementation is deterministic.

        var orderToolParameters = BinaryData.FromObjectAsJson(
            new
            {
                type = "object",
                properties = new
                {
                    order_id = new
                    {
                        type = "string",
                        description = "e.g. A-1001"
                    }
                },
                required = new[] { "order_id" }
            },
            JsonHelpers.Web);
        var orderTool = ResponseTool.CreateFunctionTool(
            "get_order_status",
            orderToolParameters,
            null,
            "Look up the status and ETA of a customer order by its ID.");

        Console.WriteLine("Tool defined: get_order_status");

        // Expected output:
        //   Tool defined: get_order_status

        // ## 3. Define the capstone agent
        //
        // Create a versioned M2 agent whose definition carries both instructions and
        // the tool. In a full build, attach the M4 Foundry IQ knowledge base to the
        // definition as well so document answers are grounded and cited.

        const string agentName = "contoso-support-agent";
        ProjectsAgentDefinition definition = new DeclarativeAgentDefinition(chatModel)
        {
            Instructions =
                "You are Contoso's support agent. Be concise and friendly. " +
                "Use the get_order_status tool whenever a customer asks about an order. " +
                "If grounding knowledge is attached, cite it. Never invent order data."
        };
        ((DeclarativeAgentDefinition)definition).Tools.Add(orderTool);
        // knowledge=[...] - attach a Foundry IQ knowledge base in a full build (M4).

        var agentResult = await projectClient.AgentAdministrationClient
            .CreateAgentVersionAsync(
                agentName,
                new ProjectsAgentVersionCreationOptions(definition));
        ProjectsAgentVersion agent = agentResult.Value;

        Console.WriteLine($"Name    : {agent.Name}");
        Console.WriteLine($"Version : {agent.Version}");

        // Expected output:
        //   Name    : contoso-support-agent
        //   Version : 1
        //
        // Tool + knowledge APIs are evolving. DeclarativeAgentDefinition is the C#
        // PromptAgentDefinition surface in Azure.AI.Projects.Agents. If these shapes
        // move, re-check M3 and M4 and keep the centrally pinned package versions.
        // ## 4. Run it - with the tool-call loop
        //
        // Invoke through Responses with agent_reference. When the model requests the
        // tool, run it locally and return function_call_output so the open response can
        // finish - the M13 function_call -> function_call_output loop.

        Console.WriteLine(
            await RunSupportAsync(
                context,
                agent.Name,
                "Where is my order A-1001?"));

        // Expected output:
        //   Your order A-1001 has shipped and is expected to arrive on 2026-06-15.
        //   Is there anything else I can help you with?
        //
        // Wording varies. The important workflow is get_order_status("A-1001"), the
        // deterministic stub result, and a final reply composed from that result.

        // ## 5. Evaluate before you trust it
        //
        // A capstone agent is not done until it is measured. Score two responses for
        // relevance against the exact inline test set.

        var aoaiEndpoint = context.Config.AccountUri;
        var relevance = new RelevanceJudge(context, chatModel, aoaiEndpoint);
        var cases = new[]
        {
            new EvaluationCase(
                "Where is my order A-1001?",
                await RunSupportAsync(
                    context,
                    agent.Name,
                    "Where is my order A-1001?")),
            new EvaluationCase(
                "What's the ETA on A-1002?",
                await RunSupportAsync(
                    context,
                    agent.Name,
                    "What's the ETA on A-1002?"))
        };

        foreach (var evaluationCase in cases)
        {
            var score = await relevance.ScoreAsync(
                evaluationCase.Query,
                evaluationCase.Response);
            Console.WriteLine(
                $"{evaluationCase.Query[..Math.Min(28, evaluationCase.Query.Length)],-30} " +
                $"relevance = {score}/5");
        }

        // Expected output:
        //   Where is my order A-1001?      relevance = 5/5
        //   What's the ETA on A-1002?      relevance = 4/5
        //
        // Scores vary. The point is a numeric release signal, not a vibe.
        //
        // Judge endpoint: account, not project.
        // Python's RelevanceEvaluator uses the classic deployments route on the
        // account endpoint, so AOAI_ENDPOINT is derived by stripping
        // /api/projects/<project>. The C# adaptation calls that same account-level
        // chat-completions route with AAD and a strict JSON schema for the same 1-5
        // relevance contract.

        // ## 6. Make it observable
        //
        // Finally, turn on M10 tracing so subsequent capstone runs can emit spans to
        // Application Insights.

        // Cell 17 [code]
        if (tracerProvider is not null)
        {
            Console.WriteLine(
                "Tracing on \u2014 capstone runs now export spans to App Insights.");
        }
        else
        {
            Console.WriteLine(
                "Set APP_INSIGHTS_CONN_STRING in .env to enable tracing (see M10).");
        }

        tracerProvider?.ForceFlush();

        // Expected output:
        //   Tracing on - capstone runs now export spans to App Insights.
        //
        // In the portal Monitor tab (or via KQL), a traced run shows a span per
        // Responses call, including tool execution - the full picture of what the
        // agent did.

        // ## Your turn - make it yours
        //
        // 1. Ground it for real. Attach an M4 Foundry IQ knowledge base and add a
        //    question whose answer must come from a document; confirm the citation.
        // 2. Add a guardrail. Pin the M11 guardrail policy to the deployment and try a
        //    prompt-injection input; confirm it is blocked.
        // 3. Harden + measure. Run the M12 scan against the capstone, add the
        //    worst-scoring prompts to the M9 test set, and re-evaluate.

        // ## Where to go next
        //
        // - Hosted agents: deploy an ACR-backed containerized service.
        // - Multi-agent at scale: grow M7 into a router + specialist fleet.
        // - Content Understanding: add document, audio, and video processing.
        // - Hub-and-spoke infra: add Bicep/APIM, quotas, and a governed gateway.
        // - Governance with policy: deny ungoverned deployments.
        // - Publishing: surface the agent in Microsoft 365, Teams, and BizChat.
        //
        // You shipped a grounded-ready, tool-using, evaluated, observable agent on
        // Microsoft Foundry - end to end.
        _ = openAiClient;
    },
    "PROJECT_ENDPOINT");

static async Task<string> RunSupportAsync(
    WorkshopContext context,
    string agentName,
    string userMessage)
{
    using var runActivity = CapstoneTelemetry.Source.StartActivity(
        "support.run",
        ActivityKind.Internal);
    runActivity?.SetTag("gen_ai.system", "microsoft_foundry");
    runActivity?.SetTag("gen_ai.request.model", context.Config.ChatModel);
    runActivity?.SetTag("gen_ai.agent.name", agentName);

    using var response = await CreateTracedResponseAsync(
        context,
        agentName,
        new
        {
            input = new[] { new { role = "user", content = userMessage } },
            agent_reference = new { name = agentName, type = "agent_reference" }
        });

    var toolCalls = JsonHelpers.GetFunctionCalls(response.RootElement).ToArray();
    runActivity?.SetTag("gen_ai.tool.call.count", toolCalls.Length);
    if (toolCalls.Length == 0)
    {
        return JsonHelpers.GetOutputText(response.RootElement);
    }

    var outputs = new List<object>();
    foreach (var call in toolCalls)
    {
        using var toolActivity = CapstoneTelemetry.Source.StartActivity(
            "execute_tool",
            ActivityKind.Internal);
        var toolName = call.GetProperty("name").GetString();
        toolActivity?.SetTag("gen_ai.tool.name", toolName);
        if (!string.Equals(
                toolName,
                "get_order_status",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The agent requested unsupported tool '{toolName}'.");
        }

        using var arguments = JsonDocument.Parse(
            call.GetProperty("arguments").GetString() ?? "{}");
        if (!arguments.RootElement.TryGetProperty("order_id", out var orderIdElement) ||
            orderIdElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException(
                "get_order_status requires a string order_id.");
        }

        var result = GetOrderStatus(orderIdElement.GetString()!);
        outputs.Add(new
        {
            type = "function_call_output",
            call_id = call.GetProperty("call_id").GetString(),
            output = JsonSerializer.Serialize(result, JsonHelpers.Web)
        });
    }

    using var final = await CreateTracedResponseAsync(
        context,
        agentName,
        new
        {
            input = outputs,
            previous_response_id = response.RootElement.GetProperty("id").GetString(),
            agent_reference = new { name = agentName, type = "agent_reference" }
        });
    return JsonHelpers.GetOutputText(final.RootElement);
}

static async Task<JsonDocument> CreateTracedResponseAsync(
    WorkshopContext context,
    string agentName,
    object request)
{
    using var activity = CapstoneTelemetry.Source.StartActivity(
        "responses.create",
        ActivityKind.Client);
    activity?.SetTag("gen_ai.system", "microsoft_foundry");
    activity?.SetTag("gen_ai.request.model", context.Config.ChatModel);
    activity?.SetTag("gen_ai.agent.name", agentName);

    var response = await context.Rest.CreateResponseAsync(request);
    if (response.RootElement.TryGetProperty("id", out var responseId))
    {
        activity?.SetTag("gen_ai.response.id", responseId.GetString());
    }

    return response;
}

static object GetOrderStatus(string orderId)
{
    var orders = new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["A-1001"] = new { status = "shipped", eta = "2026-06-15" },
        ["A-1002"] = new { status = "processing", eta = "2026-06-20" }
    };
    return orders.TryGetValue(orderId, out var order)
        ? order
        : new { status = "not_found" };
}

internal sealed class RelevanceJudge(
    WorkshopContext context,
    string model,
    Uri accountEndpoint)
{
    public async Task<int> ScoreAsync(string query, string response)
    {
        var prompt = $"""
            You are an impartial relevance evaluator.
            Score how directly and completely RESPONSE addresses QUERY.
            Use only an integer from 1 (not relevant) through 5 (fully relevant).

            QUERY:
            {query}

            RESPONSE:
            {response}

            Return only JSON matching the supplied schema.
            """;
        var uri = new Uri(
            accountEndpoint,
            $"openai/deployments/{Uri.EscapeDataString(model)}/chat/completions" +
            "?api-version=2024-10-21");
        using var judgedResponse = await context.Rest.SendJsonAsync(
            HttpMethod.Post,
            uri,
            new
            {
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0,
                max_completion_tokens = 80,
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "relevance_evaluation",
                        strict = true,
                        schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                relevance = new
                                {
                                    type = "integer",
                                    minimum = 1,
                                    maximum = 5
                                }
                            },
                            required = new[] { "relevance" },
                            additionalProperties = false
                        }
                    }
                }
            },
            FoundryRestClient.CognitiveServicesScope);

        using var result = JsonDocument.Parse(
            judgedResponse.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
            ?? throw new JsonException(
                "Relevance evaluator returned no message content."));
        var score = result.RootElement.GetProperty("relevance").GetInt32();
        if (score is < 1 or > 5)
        {
            throw new JsonException(
                $"Relevance evaluator returned out-of-range score {score}.");
        }

        return score;
    }
}

internal sealed record EvaluationCase(string Query, string Response);

internal static class CapstoneTelemetry
{
    public const string SourceName = "FoundryWorkshop.M15";
    public static readonly ActivitySource Source = new(SourceName);
}
