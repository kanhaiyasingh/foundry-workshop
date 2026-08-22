using System.Text.Json;
using FoundryWorkshop.Shared;

return await LabHost.RunAsync(
    "M13 - Human in the loop and REST",
    args,
    async context =>
    {
        Console.WriteLine("Approval path:");
        Console.WriteLine(await RunTransferAsync(context, 500, approve: true));
        Console.WriteLine("\nRejection path:");
        Console.WriteLine(await RunTransferAsync(context, 9000, approve: false));

        using var single = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            input = "Invent a one-line story about an astronaut named Mira."
        });
        var firstId = single.RootElement.GetProperty("id").GetString();
        Console.WriteLine($"\nREST single-shot: {JsonHelpers.GetOutputText(single.RootElement)}");

        using var followUp = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            previous_response_id = firstId,
            input = "Continue the story in one line without repeating the first line."
        });
        Console.WriteLine($"REST multi-turn: {JsonHelpers.GetOutputText(followUp.RootElement)}");

        Console.Write("REST SSE stream: ");
        await foreach (var delta in context.Rest.StreamResponseTextAsync(new
        {
            model = context.Config.ChatModel,
            input = "Tell a two-sentence story about a lighthouse keeper.",
            stream = true
        }))
        {
            Console.Write(delta);
        }

        Console.WriteLine();
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL");

static async Task<string> RunTransferAsync(
    WorkshopContext context,
    decimal amount,
    bool approve)
{
    var transferTool = new
    {
        type = "function",
        name = "transfer_funds",
        description = "Transfer funds between accounts. The host must obtain human approval.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                from_account = new { type = "string" },
                to_account = new { type = "string" },
                amount = new { type = "number" }
            },
            required = new[] { "from_account", "to_account", "amount" },
            additionalProperties = false
        }
    };
    using var initial = await context.Rest.CreateResponseAsync(new
    {
        model = context.Config.ChatModel,
        instructions = "Use transfer_funds for every requested transfer. Do not claim success before the tool result.",
        input = $"Transfer ${amount} from ACC-001 to ACC-002.",
        tools = new[] { transferTool }
    });
    var calls = JsonHelpers.GetFunctionCalls(initial.RootElement).ToArray();
    if (calls.Length == 0)
    {
        throw new InvalidOperationException("The model did not request the transfer tool.");
    }

    var outputs = new List<object>();
    foreach (var call in calls)
    {
        using var parsed = JsonDocument.Parse(call.GetProperty("arguments").GetString() ?? "{}");
        var requestedAmount = parsed.RootElement.GetProperty("amount").GetDecimal();
        var result = approve
            ? $"APPROVED: transferred ${requestedAmount:F2} from ACC-001 to ACC-002."
            : "REJECTED: the human operator declined this transfer; no funds moved.";
        Console.WriteLine($"{(approve ? "[APPROVED]" : "[REJECTED]")} ${requestedAmount:F2}");
        outputs.Add(new
        {
            type = "function_call_output",
            call_id = call.GetProperty("call_id").GetString(),
            output = result
        });
    }

    using var completed = await context.Rest.CreateResponseAsync(new
    {
        model = context.Config.ChatModel,
        previous_response_id = initial.RootElement.GetProperty("id").GetString(),
        input = outputs
    });
    return JsonHelpers.GetOutputText(completed.RootElement);
}
