// M6 objective: create a preview memory store, extract preferences, and recall them by scope.
// Full guide: docs/modules/06-agent-memory.md
// Prerequisites: PROJECT_ENDPOINT, CHAT_MODEL, EMBEDDING_MODEL, Memory API availability,
// and permission to manage project memory stores.
// Check: dotnet run --project .\labs\06-agent-memory -- --check
// Run:   dotnet run --project .\labs\06-agent-memory
// Expect: the named store, then raw recalled-memory JSON containing extracted preferences.
// Caution: each run deletes and recreates the fixed workshop store name.

using FoundryWorkshop.Shared;

// Step 1: Fix the preview API version, workshop store name, and isolated user scope.
return await LabHost.RunAsync(
    "M6 - Agent memory",
    args,
    async context =>
    {
        const string apiVersion = "2025-11-15-preview";
        const string storeName = "csharp-workshop-dev-preferences";
        const string scope = "workshop-user-dana";
        // Expected result:
        //   Store 'csharp-workshop-dev-preferences' and scope 'workshop-user-dana' ready.

        // Step 2: Reset and create the memory store with chat extraction and embedding retrieval.
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
            // Expected output:
            //   Created memory store: csharp-workshop-dev-preferences

            // Step 3: Submit a fixed conversation whose durable C# preferences should be extracted.
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
        // Expected result:
        //   Memory update accepted for scope 'workshop-user-dana'.

        // Step 4: Poll asynchronous extraction and surface terminal service failures.
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
        // Expected result:
        //   Memory extraction completes.

        // Step 5: Search the same scope; response shape and extracted wording can vary.
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
        // Expected output:
        //   Recalled memories:
        //   { <model-dependent extracted C#, concise, code-first, VS Code, and Windows preferences> }
        //   Preview note: the Memory API is REST-backed because Azure.AI.Projects 2.0 has no stable memory client.
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL",
    "EMBEDDING_MODEL");

// Your Turn:
// 1. Teach it something new. Add "I've switched to nullable reference types everywhere"
//    to conversation, submit another update, wait for extraction, then search in a new
//    call and confirm the memory is returned.
// 2. Prove isolation. Change scope to "workshop-user-sam" and run the same search; it
//    should not return Dana's preferences.
// 3. Go production-style. Resolve scope from context.Config.Require("USER_ID") instead
//    of a fixed string, so one application instance serves users with isolated memory.
