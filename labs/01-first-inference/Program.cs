using Azure.AI.Extensions.OpenAI;
using FoundryWorkshop.Shared;
using OpenAI.Responses;

#pragma warning disable OPENAI001

return await LabHost.RunAsync(
    "M1 - First inference",
    args,
    async context =>
    {
        var config = context.Config;
        var projectClient = context.CreateProjectClient();
        var responsesClient = projectClient.ProjectOpenAIClient
            .GetProjectResponsesClientForModel(config.ChatModel);

        ResponseResult first = await responsesClient.CreateResponseAsync(
            "Reply with exactly: Foundry is ready.");
        Console.WriteLine($"Response: {first.GetOutputText()}");

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
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL",
    "EMBEDDING_MODEL");
