// M5 objective: attach a remote MCP server and verify tool discovery/invocation.
// Full guide: docs/modules/05-mcp-tools.md
// Prerequisites: PROJECT_ENDPOINT, CHAT_MODEL, a Foundry-reachable MCP_SERVER_URL,
// and optional MCP_SERVER_LABEL.
// Check: dotnet run --project .\labs\05-mcp-tools -- --check
// Run:   dotnet run --project .\labs\05-mcp-tools
// Expect: a positive MCP event count and an answer grounded in the server's tool results.

using FoundryWorkshop.Shared;

// Step 1: Read the remote endpoint and label without printing endpoint credentials.
return await LabHost.RunAsync(
    "M5 - MCP tools",
    args,
    async context =>
    {
        var serverUrl = context.Config.RequireUri(
            "MCP_SERVER_URL",
            "Deploy an MCP server and copy its SSE/HTTP endpoint into .env.");
        var label = context.Config.Get("MCP_SERVER_LABEL", "project_tracker");
        // Expected result:
        //   MCP server configuration ready with label '<configured label>'.

        // Step 2: Attach MCP directly to a Responses request and ask a tool-requiring question.
        using var response = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            input = "List the active projects and summarize the item with the highest risk. Use the MCP tools.",
            tools = new object[]
            {
                new
                {
                    type = "mcp",
                    server_label = label,
                    server_url = serverUrl,
                    require_approval = context.HasFlag("--require-approval") ? "always" : "never"
                }
            }
        });
        // Expected result:
        //   Foundry connects to the MCP server and discovers its tools.

        // Step 3: Count discovery and call items; names, count, and answer vary by server/model.
        var output = response.RootElement.GetProperty("output");
        var toolItems = output.EnumerateArray()
            .Count(item => item.TryGetProperty("type", out var type) &&
                           type.GetString() is "mcp_call" or "mcp_list_tools");
        Console.WriteLine($"MCP events: {toolItems}");
        Console.WriteLine(JsonHelpers.GetOutputText(response.RootElement));
        // Expected output:
        //   MCP events: <positive count>
        //   <model-generated answer based on the configured server's tools>
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL",
    "MCP_SERVER_URL");

// Your Turn:
// 1. Ask a multi-step question. Use "Find overdue tasks on the Aurora project and flag
//    a risk for the latest one." Inspect response.RootElement["output"] and confirm more
//    than one mcp_call item appears.
// 2. Tighten the instructions. Edit input to require the task id for every item and
//    rerun. Notice the format change.
// 3. Flip approval on. Run with --require-approval and inspect response.output for an
//    mcp_approval_request instead of an immediate tool call.
