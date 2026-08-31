// M5b objective: ground a workplace question in permission-aware Work IQ data over MCP.
// Full guide: docs/modules/05b-work-iq.md
// Prerequisites: PROJECT_ENDPOINT, CHAT_MODEL, WORKIQ_MCP_URL, Microsoft 365 licensing,
// tenant consent, a supported delegated/OBO connection, and optional WORKIQ_MCP_LABEL.
// Check: dotnet run --project .\labs\05b-work-iq -- --check
// Run:   dotnet run --project .\labs\05b-work-iq
// Input: optionally pass one quoted workplace question after --.
// Expect: a positive Work IQ MCP event count and a permission-trimmed, sourced answer.

using FoundryWorkshop.Shared;

// Step 1: Resolve the Work IQ endpoint, label, and default or participant-supplied question.
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
        // Expected result:
        //   Work IQ configuration and workplace question ready.

        // Step 2: Attach Work IQ as a read-oriented MCP tool and send the workplace question.
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
        // Expected result:
        //   Foundry connects to Work IQ and discovers its permission-aware tools.

        // Step 3: Require evidence of MCP participation rather than accepting an ungrounded answer.
        var workIqCalls = response.RootElement.GetProperty("output").EnumerateArray()
            .Count(item => item.TryGetProperty("type", out var type) &&
                           type.GetString() is "mcp_call" or "mcp_list_tools");
        if (workIqCalls == 0)
        {
            throw new InvalidOperationException(
                "The response contained no Work IQ MCP call. Confirm admin consent, user licensing, and MCP connectivity.");
        }
        // Expected result:
        //   The response contains at least one Work IQ MCP event.

        // Step 4: Review variable tenant data and verify every source respects caller permissions.
        Console.WriteLine($"Work IQ MCP events: {workIqCalls}");
        Console.WriteLine(JsonHelpers.GetOutputText(response.RootElement));
        Console.WriteLine("The returned content is permission-trimmed to the signed-in Microsoft 365 user.");
        // Expected output:
        //   Work IQ MCP events: <positive count>
        //   <permission-dependent workplace answer with source references>
        //   The returned content is permission-trimmed to the signed-in Microsoft 365 user.
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL",
    "WORKIQ_MCP_URL");

// Your Turn:
// 1. Cross-signal question. Ask "Summarize the SharePoint doc Dana shared in Teams
//    yesterday." Inspect response.RootElement["output"] and confirm more than one
//    mcp_call when the answer combines files and Teams.
// 2. Tighten governance. Keep require_approval = "never" for the read-only request; if
//    the server exposes write tools separately, send those through an MCP tool/request
//    with require_approval = "always" and confirm approvals appear only for actions.
// 3. Capstone tie-in. Sketch a work-grounded specialist for M15: decide which questions
//    the router should send to Work IQ versus the M4 Foundry IQ knowledge base.
