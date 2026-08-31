// M7 objective: route fixed questions to focused Agent Framework specialists.
// Full guide: docs/modules/07-multi-agent-orchestration.md
// Prerequisites: PROJECT_ENDPOINT, CHAT_MODEL, and a model that follows label-only routing.
// Check: dotnet run --project .\labs\07-multi-agent-orchestration -- --check
// Run:   dotnet run --project .\labs\07-multi-agent-orchestration
// Expect: POLICY -> policy-specialist and TECHNICAL -> technical-specialist, with variable prose.

using FoundryWorkshop.Shared;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// Step 1: Adapt Foundry Responses to IChatClient and define the label-only router.
return await LabHost.RunAsync(
    "M7 - Multi-agent orchestration",
    args,
    async context =>
    {
        using IChatClient chatClient = new FoundryChatClient(context.Rest, context.Config.ChatModel);
        AIAgent router = new ChatClientAgent(
            chatClient,
            instructions: """
                Classify the request as exactly POLICY, TECHNICAL, or GENERAL.
                Output only the label.
                """,
            name: "router",
            description: "Routes employee questions to a specialist.");
        // Expected result:
        //   Router ready with POLICY, TECHNICAL, and GENERAL classifications.

        // Step 2: Define policy, technical, and general specialists over the shared model.
        AIAgent policyAgent = new ChatClientAgent(
            chatClient,
            instructions: """
                You are an HR policy specialist. Answer conservatively, identify assumptions,
                and tell the user when manager or HR confirmation is required.
                """,
            name: "policy-specialist",
            description: "Answers workplace policy questions.");
        AIAgent technicalAgent = new ChatClientAgent(
            chatClient,
            instructions: """
                You are a Microsoft Foundry technical specialist.
                Give a concise diagnosis followed by concrete troubleshooting steps.
                """,
            name: "technical-specialist",
            description: "Answers Foundry implementation questions.");
        AIAgent generalAgent = new ChatClientAgent(
            chatClient,
            instructions: "You are a concise workplace concierge.",
            name: "general-specialist",
            description: "Handles uncategorized requests.");
        // Expected result:
        //   policy-specialist
        //   technical-specialist
        //   general-specialist

        // Step 3: Use fixed questions so routing behavior is easy to compare across runs.
        var questions = new[]
        {
            "Can I work from another country for two weeks?",
            "Why does my Foundry project endpoint return 403 after az login?"
        };
        // Expected result:
        //   Two workshop questions ready for routing.

        // Step 4: Dispatch exact known labels and expose all other output through the fallback.
        foreach (var question in questions)
        {
            var routing = await router.RunAsync(question);
            var label = routing.Text.Trim().ToUpperInvariant();
            var specialist = label switch
            {
                "POLICY" => policyAgent,
                "TECHNICAL" => technicalAgent,
                _ => generalAgent
            };
            var answer = await specialist.RunAsync(question);
            Console.WriteLine($"\nQuestion: {question}");
            Console.WriteLine($"Route: {label} -> {specialist.Name}");
            Console.WriteLine(answer.Text);
        }
        // Expected output:
        //   Question: Can I work from another country for two weeks?
        //   Route: POLICY -> policy-specialist
        //   <model-generated policy answer>
        //   Question: Why does my Foundry project endpoint return 403 after az login?
        //   Route: TECHNICAL -> technical-specialist
        //   <model-generated technical answer>

        Console.WriteLine(
            "\nThe router and specialists are Microsoft Agent Framework ChatClientAgent instances over one Foundry client.");
        // Expected output:
        //   The router and specialists are Microsoft Agent Framework ChatClientAgent instances over one Foundry client.
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL");

// Your Turn:
// 1. Add a fourth specialist. Create an it-specialist ChatClientAgent, add IT to the
//    router instructions and label switch, then ask "My laptop won't connect to VPN,
//    who do I contact?"
// 2. Probe the default branch. Ask "Tell me about Contoso" and confirm it falls through
//    to generalAgent. Then tighten the router prompt to handle it better.
// 3. Ground a specialist. If you have the M4 knowledge base, retrieve its context through
//    the existing MCP Responses shape before invoking policyAgent, include that context
//    in the specialist request, and confirm the answer cites document titles.
