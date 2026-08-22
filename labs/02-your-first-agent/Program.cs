using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects.Agents;
using FoundryWorkshop.Shared;
using OpenAI.Responses;

#pragma warning disable OPENAI001

return await LabHost.RunAsync(
    "M2 - Your first agent",
    args,
    async context =>
    {
        const string agentName = "workshop-concierge";
        var projectClient = context.CreateProjectClient();
        ProjectsAgentDefinition definition = new DeclarativeAgentDefinition(context.Config.ChatModel)
        {
            Instructions = """
                You are the Microsoft Foundry workshop concierge.
                Answer in at most three sentences and end with one practical next step.
                """
        };

        var versionResult = await projectClient.AgentAdministrationClient
            .CreateAgentVersionAsync(
                agentName,
                new ProjectsAgentVersionCreationOptions(definition));
        ProjectsAgentVersion version = versionResult.Value;
        Console.WriteLine($"Created {version.Name} version {version.Version} ({version.Id})");

        var conversation = await projectClient.ProjectOpenAIClient
            .GetProjectConversationsClient()
            .CreateProjectConversationAsync();
        var responses = projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(
            defaultAgent: agentName,
            defaultConversationId: conversation.Value.Id);

        ResponseResult first = await responses.CreateResponseAsync(
            "What is the difference between a model call and an agent?");
        Console.WriteLine(first.GetOutputText());

        ResponseResult followUp = await responses.CreateResponseAsync(
            "Summarize that in five words.");
        Console.WriteLine($"Follow-up: {followUp.GetOutputText()}");
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL");
