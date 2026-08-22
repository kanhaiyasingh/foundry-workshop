using FoundryWorkshop.Shared;

return await LabHost.RunAsync(
    "M5 - MCP tools",
    args,
    async context =>
    {
        var serverUrl = context.Config.RequireUri(
            "MCP_SERVER_URL",
            "Deploy an MCP server and copy its SSE/HTTP endpoint into .env.");
        var label = context.Config.Get("MCP_SERVER_LABEL", "project_tracker");

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

        var output = response.RootElement.GetProperty("output");
        var toolItems = output.EnumerateArray()
            .Count(item => item.TryGetProperty("type", out var type) &&
                           type.GetString() is "mcp_call" or "mcp_list_tools");
        Console.WriteLine($"MCP events: {toolItems}");
        Console.WriteLine(JsonHelpers.GetOutputText(response.RootElement));
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL",
    "MCP_SERVER_URL");
