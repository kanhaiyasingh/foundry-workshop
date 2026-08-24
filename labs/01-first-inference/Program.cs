// M1 objective: make a model response, create an embedding, and stream response deltas.
// Full guide: docs/modules/01-first-inference.md
// Prerequisites: PROJECT_ENDPOINT, CHAT_MODEL, EMBEDDING_MODEL, az login, and Foundry access.
// Check: dotnet run --project .\labs\01-first-inference -- --check
// Run:   dotnet run --project .\labs\01-first-inference
// Expect: an exact readiness response, a model-dependent vector size, then streamed text.

using Azure.AI.Extensions.OpenAI;
using FoundryWorkshop.Shared;
using OpenAI.Responses;

#pragma warning disable OPENAI001

// Step 1: Build the project Responses client for the configured chat deployment.
return await LabHost.RunAsync(
    "M1 - First inference",
    args,
    async context =>
    {
        var config = context.Config;
        var projectClient = context.CreateProjectClient();
        var responsesClient = projectClient.ProjectOpenAIClient
            .GetProjectResponsesClientForModel(config.ChatModel);
        // Expected result:
        //   project_client : ready
        //   responses_client : ready

        // Step 2: Send a deterministic first prompt; the expected text is "Foundry is ready."
        ResponseResult first = await responsesClient.CreateResponseAsync(
            "Reply with exactly: Foundry is ready.");
        Console.WriteLine($"Response: {first.GetOutputText()}");
        // Expected output:
        //   Response: Foundry is ready.

        // Step 3: Call the account-scoped embeddings route and inspect the returned vector size.
        var embeddingsUri = new Uri(
            config.AccountUri,
            $"openai/deployments/{Uri.EscapeDataString(config.EmbeddingModel)}/embeddings" +
            "?api-version=2024-10-21");
        using var embeddings = await context.Rest.SendJsonAsync(
            HttpMethod.Post,
            embeddingsUri,
            new { input = new[] { "Microsoft Foundry agents use tools." } },
            FoundryRestClient.CognitiveServicesScope);
        var vector = embeddings.RootElement
            .GetProperty("data")[0]
            .GetProperty("embedding");
        Console.WriteLine($"Embedding dimensions: {vector.GetArrayLength()}");
        // Expected output:
        //   Embedding dimensions: <deployment-dependent value>

        // Step 4: Render SSE deltas immediately; wording varies, but text should appear incrementally.
        Console.Write("Streaming: ");
        await foreach (var delta in context.Rest.StreamResponseTextAsync(new
        {
            model = config.ChatModel,
            input = "Explain the Responses API in one short sentence.",
            stream = true
        }))
        {
            Console.Write(delta);
        }

        Console.WriteLine();
        // Expected output:
        //   Streaming: <model-generated short explanation of the Responses API>
        //   The sentence prints incrementally, a few tokens at a time.
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL",
    "EMBEDDING_MODEL");

// Your Turn:
// 1. Swap the model. If you deployed a reasoning model, set REASONING_MODEL in .env,
//    read it with context.Config.Require("REASONING_MODEL"), and use it for a Responses
//    call. Note how the answer style changes.
// 2. Compare token usage. Ask a long question and a short one, then print
//    response.Usage.TotalTokenCount for each.
// 3. Embed and compare. Embed two similar sentences and two different ones, normalize
//    the vectors, and compute cosine similarity in C#; similar sentences should score higher.
