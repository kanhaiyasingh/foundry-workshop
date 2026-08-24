// M1 - First Inference
//
// Goal: make your first model calls on Foundry - chat, embeddings, streaming, and the
// Responses API - all from one project configuration.
// You'll use: AIProjectClient, account-scoped chat completions and embeddings, and the
// project Responses API.
//
// Every lab starts the same way: authenticate with your Azure identity, build an
// AIProjectClient from the project endpoint, and use OpenAI-compatible APIs for chat,
// embeddings, and Responses. The Responses API later powers agents and tools.
//
// If you have not configured the project and .env, complete docs/setup.md first.
// Full guide: docs/modules/01-first-inference.md
// Check: dotnet run --project .\labs\01-first-inference -- --check
// Run:   dotnet run --project .\labs\01-first-inference

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.AI.Extensions.OpenAI;
using FoundryWorkshop.Shared;
using OpenAI.Responses;

#pragma warning disable OPENAI001

// Notebook cell: print the current date and time.
Console.WriteLine($"Current date and time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}");

return await LabHost.RunAsync(
    "M1 - First inference",
    args,
    async context =>
    {
        // 1. Configure
        //
        // Every lab reads the same variables from .env. Grab the project endpoint and the
        // two model deployment names used in this lab.
        var config = context.Config;
        var projectEndpoint = config.ProjectEndpoint;
        var chatModel = config.ChatModel;
        var embeddingModel = config.EmbeddingModel;

        Console.WriteLine($"Project : {projectEndpoint}");
        Console.WriteLine($"Chat    : {chatModel}");
        Console.WriteLine($"Embed   : {embeddingModel}");

        // Expected output:
        //   Project : https://<account>.services.ai.azure.com/api/projects/<project>
        //   Chat    : gpt-4.1-mini
        //   Embed   : text-embedding-3-large
        //
        // These values come from .env; no secrets are printed.

        // 2. Build the client
        //
        // AzureCliCredential is selected when AZURE_AUTH_MODE=cli; otherwise the workshop
        // uses DefaultAzureCredential. AIProjectClient uses the project endpoint and that
        // identity. This project routes classic chat and embeddings through the account
        // data plane, while Responses uses the project client.
        var projectClient = context.CreateProjectClient();
        var accountOpenAiUri = new Uri(config.AccountUri, "openai/v1/");
        Console.WriteLine("project_client : ready");
        Console.WriteLine($"openai_client  : ready -> {accountOpenAiUri}");

        // Expected output:
        //   project_client : ready
        //   openai_client  : ready
        //
        // An authentication error usually means you need az login. A 403 usually means the
        // signed-in identity lacks the Foundry/Azure role required by the project.

        // 3. Chat completions
        //
        // The classic chat surface takes a deployment name and a list of messages.
        var chatUri = new Uri(
            config.AccountUri,
            $"openai/deployments/{Uri.EscapeDataString(chatModel)}/chat/completions" +
            "?api-version=2024-10-21");
        using var chat = await context.Rest.SendJsonAsync(
            HttpMethod.Post,
            chatUri,
            new
            {
                model = chatModel,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "You are a concise technical assistant."
                    },
                    new
                    {
                        role = "user",
                        content = "What is catastrophic forgetting in neural networks?"
                    }
                }
            },
            FoundryRestClient.CognitiveServicesScope);
        var chatRoot = chat.RootElement;
        var returnedModel = chatRoot.GetProperty("model").GetString() ?? chatModel;
        var totalTokens = chatRoot.GetProperty("usage").GetProperty("total_tokens").GetInt32();
        var chatText = chatRoot
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        Console.WriteLine($"Model  : {returnedModel}");
        Console.WriteLine($"Tokens : {totalTokens}");
        Console.WriteLine();
        Console.WriteLine(chatText);

        // Expected output:
        //   Model  : gpt-4.1-mini
        //   Tokens : 142
        //
        //   Catastrophic forgetting is the tendency of a neural network to abruptly lose
        //   knowledge of previously learned tasks when it is trained on a new task...
        //
        // Token counts and wording vary; the output shape is what matters.

        // 4. Embeddings
        //
        // Turn text into vectors - the foundation for retrieval in M4. One request embeds
        // a batch of strings.
        var texts = new[]
        {
            "Microsoft Foundry centralises model governance behind one platform.",
            "Embeddings turn text into vectors for semantic search.",
            "Each project authenticates with DefaultAzureCredential."
        };
        var embeddingsUri = new Uri(
            config.AccountUri,
            $"openai/deployments/{Uri.EscapeDataString(embeddingModel)}/embeddings" +
            "?api-version=2024-10-21");
        using var embeddings = await context.Rest.SendJsonAsync(
            HttpMethod.Post,
            embeddingsUri,
            new { model = embeddingModel, input = texts },
            FoundryRestClient.CognitiveServicesScope);
        var embeddingData = embeddings.RootElement.GetProperty("data");
        var dimensions = embeddingData[0].GetProperty("embedding").GetArrayLength();

        Console.WriteLine($"Model      : {embeddingModel}");
        Console.WriteLine($"Dimensions : {dimensions}");
        var itemIndex = 0;
        foreach (var item in embeddingData.EnumerateArray())
        {
            var vector = item.GetProperty("embedding");
            var values = vector.EnumerateArray().Take(3)
                .Select(value => value.GetDouble().ToString("F4", CultureInfo.InvariantCulture));
            Console.WriteLine(
                $"[{itemIndex}] [{string.Join(", ", values)}, ...]  " +
                $"({vector.GetArrayLength()} dims)");
            itemIndex++;
        }

        // Expected output:
        //   Model      : text-embedding-3-large
        //   Dimensions : 3072
        //   [0] [-0.0123, 0.0456, -0.0789, ...]  (3072 dims)
        //   [1] [0.0234, -0.0567, 0.0891, ...]  (3072 dims)
        //   [2] [-0.0345, 0.0678, -0.0912, ...]  (3072 dims)
        //
        // Values and dimensions depend on the configured embedding deployment.

        // 5. Streaming
        //
        // For responsive UIs, stream tokens as they are generated instead of waiting for
        // the full response.
        using var streamRequest = await context.Rest.CreateRequestAsync(
            HttpMethod.Post,
            chatUri,
            FoundryRestClient.CognitiveServicesScope);
        streamRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        streamRequest.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                model = chatModel,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = "In one sentence, what is Microsoft Foundry?"
                    }
                },
                stream = true
            }),
            Encoding.UTF8,
            "application/json");

        using var streamClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var streamResponse = await streamClient.SendAsync(
            streamRequest,
            HttpCompletionOption.ResponseHeadersRead);
        if (!streamResponse.IsSuccessStatusCode)
        {
            var error = await streamResponse.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Streaming chat returned {(int)streamResponse.StatusCode}. {error}",
                null,
                streamResponse.StatusCode);
        }

        await using (var stream = await streamResponse.Content.ReadAsStreamAsync())
        using (var reader = new StreamReader(stream))
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    continue;
                }

                var data = line[6..];
                if (data == "[DONE]")
                {
                    break;
                }

                using var chunk = JsonDocument.Parse(data);
                var choices = chunk.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() == 0)
                {
                    continue;
                }

                var delta = choices[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var content))
                {
                    Console.Write(content.GetString());
                }
            }
        }
        Console.WriteLine();

        // Expected output:
        // The sentence prints incrementally, a few tokens at a time:
        //   Microsoft Foundry is Azure's unified platform-as-a-service for building,
        //   governing, and operating enterprise AI models, agents, and apps.

        // 6. The Responses API
        //
        // Responses is the modern stateful surface that powers agents and tools in later
        // labs. The minimal call takes a model and input; the reply is output text.
        var responsesClient = projectClient.ProjectOpenAIClient
            .GetProjectResponsesClientForModel(chatModel);
        ResponseResult response = await responsesClient.CreateResponseAsync(
            "Name a planet with rings, in one short sentence.");
        Console.WriteLine(response.GetOutputText());

        // Expected output:
        //   Saturn is a planet famous for its prominent ring system.
        //
        // Why this matters:
        // In M2, a model is wrapped in an agent definition and invoked through this same
        // Responses surface with an agent reference attached.

        // Your turn
        //
        // 1. Swap the model. If you deployed a reasoning model, set REASONING_MODEL in
        //    .env, read it in the Configure section, and rerun the Responses call with it.
        //    Note how the answer style changes.
        // 2. Compare token usage. Ask the chat model a long question and a short one, then
        //    print usage.total_tokens for each response.
        // 3. Embed and compare. Embed two similar sentences and two different ones, then
        //    normalize the vectors and compute cosine similarity in C#. Similar sentences
        //    should score higher.
        //
        // You made chat, embedding, streaming, and Responses API calls from one project.
        // Next: wrap a model in a versioned agent and invoke it in M2.
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL",
    "EMBEDDING_MODEL");
