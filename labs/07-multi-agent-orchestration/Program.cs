using FoundryWorkshop.Shared;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

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

        var questions = new[]
        {
            "Can I work from another country for two weeks?",
            "Why does my Foundry project endpoint return 403 after az login?"
        };

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

        Console.WriteLine(
            "\nThe router and specialists are Microsoft Agent Framework ChatClientAgent instances over one Foundry client.");
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL");
