// M2 - Your First Agent
//
// Goal: turn a raw model into a named, versioned agent - give it instructions, create
// it on Foundry, invoke it, then iterate safely.
// You'll use: DeclarativeAgentDefinition, CreateAgentVersionAsync, and the Responses
// API with an agent reference.
//
// In M1 you called a model directly. An agent wraps that model in a reusable,
// server-side definition:
//
//   agent = model + instructions + tools
//
// The definition lives in your Foundry project under a stable name. When you change
// the definition, Foundry stores a new version - so callers can keep using the same
// name while the agent evolves.
//
// See docs/assets/agent-anatomy.png for the anatomy of a Foundry agent.
//
// If your project and .env are not ready yet, complete docs/setup.md first.
// Full guide: docs/modules/02-your-first-agent.md
// Check: dotnet run --project .\labs\02-your-first-agent -- --check
// Run:   dotnet run --project .\labs\02-your-first-agent

using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects.Agents;
using FoundryWorkshop.Shared;
using OpenAI.Responses;

#pragma warning disable OPENAI001

// Notebook cell: print the current date and time.
Console.WriteLine($"Current date and time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}");

return await LabHost.RunAsync(
    "M2 - Your first agent",
    args,
    async context =>
    {
        // 1. Configure
        //
        // Use the same .env as every lab (see docs/setup.md). Read the project endpoint
        // and chat model deployment, and pick a stable agent name.
        var projectEndpoint = context.Config.ProjectEndpoint;
        var chatModel = context.Config.ChatModel;

        // A stable, human-readable name. Re-running these stages versions THIS agent.
        const string agentName = "storytelling-agent";

        Console.WriteLine($"Project : {projectEndpoint}");
        Console.WriteLine($"Chat    : {chatModel}");
        Console.WriteLine($"Agent   : {agentName}");

        // Expected output:
        //   Project : https://<account>.services.ai.azure.com/api/projects/<project>
        //   Chat    : gpt-4.1-mini
        //   Agent   : storytelling-agent
        //
        // The agent name is yours to choose - keep it stable, since versioning keys off
        // it.

        // 2. Build the client
        //
        // This is the same bootstrap as M1: DefaultAzureCredential (or the workshop's
        // configured AzureCliCredential) -> AIProjectClient -> an OpenAI-compatible
        // client. AgentAdministrationClient is the C# surface for creating and
        // versioning agents.
        var projectClient = context.CreateProjectClient();
        var openAiClient = projectClient.ProjectOpenAIClient;

        Console.WriteLine("project_client : ready");
        Console.WriteLine("openai_client  : ready");

        // Expected output:
        //   project_client : ready
        //   openai_client  : ready
        //
        // A credential error here usually means you need az login; a 403 means your
        // identity lacks the Azure AI Developer role on the project.

        // 3. Define and create the agent
        //
        // DeclarativeAgentDefinition is the C# SDK equivalent of PromptAgentDefinition:
        // it contains the model and the instructions (system prompt) that shape the
        // agent's behaviour. Tools come in M3. CreateAgentVersionAsync stores that
        // definition under the chosen name.
        ProjectsAgentDefinition definition = new DeclarativeAgentDefinition(chatModel)
        {
            Instructions =
                "You are a storytelling agent. " +
                "You craft engaging one-line stories based on user prompts and context."
        };

        var agentResult = await projectClient.AgentAdministrationClient
            .CreateAgentVersionAsync(
                agentName,
                new ProjectsAgentVersionCreationOptions(definition));
        ProjectsAgentVersion agent = agentResult.Value;

        Console.WriteLine($"Name    : {agent.Name}");
        Console.WriteLine($"Version : {agent.Version}");

        // Expected output on a project where this agent does not exist yet:
        //   Name    : storytelling-agent
        //   Version : 1
        //
        // create_version is idempotent:
        // Repeat the exact request now so the live service, rather than just this
        // comment, demonstrates the notebook's claim. Foundry compares the definition
        // with the latest version and returns that version when nothing changed.
        var unchangedAgentResult = await projectClient.AgentAdministrationClient
            .CreateAgentVersionAsync(
                agentName,
                new ProjectsAgentVersionCreationOptions(definition));
        ProjectsAgentVersion unchangedAgent = unchangedAgentResult.Value;

        if (!string.Equals(agent.Version, unchangedAgent.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "An unchanged agent definition unexpectedly created a new version.");
        }

        Console.WriteLine($"Replay  : {unchangedAgent.Version} (unchanged)");

        // Expected output on the first clean-project run:
        //   Replay  : 1 (unchanged)
        //
        // Existing projects can report a higher number. The invariant is that Version
        // and Replay match; only a changed definition should increment the version.

        // 4. Invoke the agent
        //
        // Call the agent through the same Responses surface from M1, but attach an agent
        // reference instead of passing a model. GetProjectResponsesClientForAgent is the
        // C# SDK equivalent of Python's extra_body agent_reference; it sends an
        // agent_reference with the stable agent name. Foundry resolves that name, applies
        // the stored model and instructions, and returns the reply as output text.
        var responses = openAiClient.GetProjectResponsesClientForAgent(
            defaultAgent: agent.Name);
        ResponseResult response = await responses.CreateResponseAsync(
            "Tell me a one-line story about a lighthouse keeper.");

        Console.WriteLine(response.GetOutputText());

        // Expected output:
        //   Every night the keeper lit the lamp for ships that never came - until the
        //   night one finally did, carrying the letter he'd stopped waiting for.
        //
        // Wording varies run to run; what matters is that the model now speaks in the
        // voice the instructions defined, without resending the system prompt.
        //
        // This call has no conversation and no previous-response ID, matching the
        // notebook. Reusing a Responses client alone does not carry history forward.

        // 5. Version the agent
        //
        // This is the payoff. Change the instructions and call CreateAgentVersionAsync
        // again: same name, new version. Existing callers keep working; this publishes a
        // new revision they can pick up. Here the agent becomes gloomier.
        ProjectsAgentDefinition v2Definition = new DeclarativeAgentDefinition(chatModel)
        {
            Instructions =
                "You are a storytelling agent with a melancholic, noir voice. " +
                "You craft a single haunting sentence based on the user's prompt."
        };

        var agentV2Result = await projectClient.AgentAdministrationClient
            .CreateAgentVersionAsync(
                agentName, // same name -> new version
                new ProjectsAgentVersionCreationOptions(v2Definition));
        ProjectsAgentVersion agentV2 = agentV2Result.Value;

        if (string.Equals(agent.Version, agentV2.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A changed agent definition did not create a new version.");
        }

        Console.WriteLine($"Name    : {agentV2.Name}");
        Console.WriteLine($"Version : {agentV2.Version}"); // changed instructions increment it

        var v2Responses = openAiClient.GetProjectResponsesClientForAgent(
            defaultAgent: agentV2.Name);
        response = await v2Responses.CreateResponseAsync(
            "Tell me a one-line story about a lighthouse keeper.");
        Console.WriteLine();
        Console.WriteLine(response.GetOutputText());

        // Expected output on the first clean-project run:
        //   Name    : storytelling-agent
        //   Version : 2
        //
        //   The lamp still turns, but the keeper stopped counting the years the sea kept
        //   taking from him.
        //
        // Name stays, version moves:
        // The name is the stable contract callers depend on; the version is the audit
        // trail of how the agent evolved. A name-only agent reference resolves the
        // latest version. Never rename to iterate - re-version.

        // Your turn
        //
        // 1. Reshape the voice. Rewrite the instructions in section 5 (for example, as a
        //    cheerful children's-book narrator) and rerun. Confirm the version increments
        //    and the tone flips.
        // 2. Prove idempotency. The unchanged replay in section 3 already verifies that
        //    agent.Version holds steady. Change one word and watch it bump.
        // 3. Give it context. Build CreateResponseOptions with a second InputItems
        //    message (a system-style preface or a prior turn) and see how the agent blends
        //    per-call context with its stored instructions. For a true follow-up request,
        //    set PreviousResponseId; for server-managed multi-turn history, explicitly
        //    create and bind a project conversation.
        //
        // You created a named agent, invoked it via agent_reference, and versioned it
        // safely. Next: give the agent real tools - code execution and your own functions
        // in M3.
        //
        // Cleanup and cost:
        // Agent versions persist in the Foundry project, and both Responses calls consume
        // model tokens. Delete the workshop agent in the Foundry portal when finished.
    },
    "PROJECT_ENDPOINT");
