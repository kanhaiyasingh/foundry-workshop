using FoundryWorkshop.Shared;

return await LabHost.RunAsync(
    "M6 - Agent memory",
    args,
    async context =>
    {
        const string apiVersion = "2025-11-15-preview";
        const string storeName = "csharp-workshop-dev-preferences";
        const string scope = "workshop-user-dana";

        await context.Rest.SendProjectJsonAsync(
            HttpMethod.Delete,
            $"memory_stores/{storeName}?api-version={apiVersion}");

        using var store = await context.Rest.SendProjectJsonAsync(
            HttpMethod.Post,
            $"memory_stores?api-version={apiVersion}",
            new
            {
                name = storeName,
                description = "Developer preferences learned in the C# workshop.",
                definition = new
                {
                    kind = "default",
                    chat_model = context.Config.ChatModel,
                    embedding_model = context.Config.EmbeddingModel,
                    options = new
                    {
                        user_profile_enabled = true,
                        user_profile_details = "Preferred languages, tools, operating system, and answer style.",
                        chat_summary_enabled = true
                    }
                }
            });
        Console.WriteLine($"Created memory store: {store.RootElement.GetProperty("name").GetString()}");

        var conversation = new object[]
        {
            new
            {
                type = "message",
                role = "user",
                content = new[] { new { type = "input_text", text = "I use C# and prefer concise, code-first answers in VS Code on Windows." } }
            },
            new
            {
                type = "message",
                role = "assistant",
                content = new[] { new { type = "output_text", text = "I will remember those development preferences." } }
            }
        };
        using var update = await context.Rest.SendProjectJsonAsync(
            HttpMethod.Post,
            $"memory_stores/{storeName}:update_memories?api-version={apiVersion}",
            new { scope, items = conversation, update_delay = 0 });
        var updateId = update.RootElement.GetProperty("update_id").GetString()
                       ?? throw new InvalidOperationException("Memory update returned no update_id.");

        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var status = await context.Rest.SendProjectJsonAsync(
                HttpMethod.Get,
                $"memory_stores/{storeName}/updates/{updateId}?api-version={apiVersion}");
            var state = status.RootElement.GetProperty("status").GetString();
            if (state == "completed")
            {
                break;
            }

            if (state is "failed" or "cancelled")
            {
                throw new InvalidOperationException($"Memory extraction ended in state '{state}'.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        using var recalled = await context.Rest.SendProjectJsonAsync(
            HttpMethod.Post,
            $"memory_stores/{storeName}:search_memories?api-version={apiVersion}",
            new
            {
                scope,
                query = "What are this developer's coding preferences?",
                max_num_results = 5
            });
        Console.WriteLine("Recalled memories:");
        Console.WriteLine(recalled.RootElement.ToString());
        Console.WriteLine(
            "Preview note: the Memory API is REST-backed because Azure.AI.Projects 2.0 has no stable memory client.");
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL",
    "EMBEDDING_MODEL");
