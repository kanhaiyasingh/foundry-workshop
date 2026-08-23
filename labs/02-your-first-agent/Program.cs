// M2 objective: create a versioned prompt agent and continue a project conversation.
// Prerequisites: PROJECT_ENDPOINT, CHAT_MODEL, az login, and permission to create agents.
// Check: dotnet run --project .\labs\02-your-first-agent -- --check
// Run:   dotnet run --project .\labs\02-your-first-agent
// Expect: an agent version/id, a concise first answer, and a context-aware follow-up.

using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects.Agents;
using FoundryWorkshop.Shared;
using OpenAI.Responses;

#pragma warning disable OPENAI001

// Step 1: Define the agent's durable behavior under one stable name.
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

        // Step 2: Publish the definition and inspect the service-assigned version and id.
        var versionResult = await projectClient.AgentAdministrationClient
            .CreateAgentVersionAsync(
                agentName,
                new ProjectsAgentVersionCreationOptions(definition));
        ProjectsAgentVersion version = versionResult.Value;
        Console.WriteLine($"Created {version.Name} version {version.Version} ({version.Id})");

        // Step 3: Create one conversation and bind a Responses client to the named agent.
        var conversation = await projectClient.ProjectOpenAIClient
            .GetProjectConversationsClient()
            .CreateProjectConversationAsync();
        var responses = projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(
            defaultAgent: agentName,
            defaultConversationId: conversation.Value.Id);

        // Step 4: Invoke the stored instructions without resending them in the request.
        ResponseResult first = await responses.CreateResponseAsync(
            "What is the difference between a model call and an agent?");
        Console.WriteLine(first.GetOutputText());

        // Step 5: Reuse the conversation; wording varies, but the follow-up should use prior context.
        ResponseResult followUp = await responses.CreateResponseAsync(
            "Summarize that in five words.");
        Console.WriteLine($"Follow-up: {followUp.GetOutputText()}");
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL");

// Your Turn: change the agent's voice, observe version behavior with changed and unchanged
// definitions, and add another turn to the same conversation.
