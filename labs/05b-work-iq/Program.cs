using FoundryWorkshop.Shared;

return await LabHost.RunAsync(
    "M5b - Work IQ",
    args,
    async context =>
    {
        var workIqUrl = context.Config.RequireUri(
            "WORKIQ_MCP_URL",
            "Configure Work IQ, grant Microsoft 365 admin consent, and expose its remote MCP endpoint.");
        var label = context.Config.Get("WORKIQ_MCP_LABEL", "work_iq");
        var prompt = context.Args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))
                     ?? "Brief me on today's meetings, unread launch messages, and my action items. Cite each source.";

        using var response = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            input = prompt,
            tools = new object[]
            {
                new
                {
                    type = "mcp",
                    server_label = label,
                    server_url = workIqUrl,
                    require_approval = "never"
                }
            }
        });

        var workIqCalls = response.RootElement.GetProperty("output").EnumerateArray()
            .Count(item => item.TryGetProperty("type", out var type) &&
                           type.GetString() is "mcp_call" or "mcp_list_tools");
        if (workIqCalls == 0)
        {
            throw new InvalidOperationException(
                "The response contained no Work IQ MCP call. Confirm admin consent, user licensing, and MCP connectivity.");
        }

        Console.WriteLine($"Work IQ MCP events: {workIqCalls}");
        Console.WriteLine(JsonHelpers.GetOutputText(response.RootElement));
        Console.WriteLine("The returned content is permission-trimmed to the signed-in Microsoft 365 user.");
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL",
    "WORKIQ_MCP_URL");
