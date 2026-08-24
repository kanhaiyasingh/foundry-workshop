// M13 objective: gate a sensitive tool call, then demonstrate raw REST, continuation, and SSE.
// Prerequisites: PROJECT_ENDPOINT, a tool-capable CHAT_MODEL, az login, and Foundry access.
// Check: dotnet run --project .\labs\13-human-in-loop-rest -- --check
// Run:   dotnet run --project .\labs\13-human-in-loop-rest
// Expect: fixed approved/rejected amounts, variable model prose, two REST turns, and a stream.
// Note: the starter demonstrates both decisions automatically; it does not prompt for input.

using System.Text.Json;
using FoundryWorkshop.Shared;

// Step 1: Run the same gated transfer loop once approved and once rejected.
return await LabHost.RunAsync(
    "M13 - Human in the loop and REST",
    args,
    async context =>
    {
        Console.WriteLine("Approval path:");
        Console.WriteLine(await RunTransferAsync(context, 500, approve: true));
        Console.WriteLine("\nRejection path:");
        Console.WriteLine(await RunTransferAsync(context, 9000, approve: false));
        // Expected output:
        //   Approval path:
        //   [APPROVED] $500.00
        //   <model-generated confirmation that the transfer completed>
        //   Rejection path:
        //   [REJECTED] $9000.00
        //   <model-generated explanation that the transfer was rejected>

        // Step 2: Send a raw single-shot Responses request and print its reconstructed text.
        using var single = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            input = "Invent a one-line story about an astronaut named Mira."
        });
        var firstId = single.RootElement.GetProperty("id").GetString();
        Console.WriteLine($"\nREST single-shot: {JsonHelpers.GetOutputText(single.RootElement)}");
        // Expected output:
        //   REST single-shot: <model-generated one-line story about astronaut Mira>

        // Step 3: Continue with previous_response_id instead of resending the first story.
        using var followUp = await context.Rest.CreateResponseAsync(new
        {
            model = context.Config.ChatModel,
            previous_response_id = firstId,
            input = "Continue the story in one line without repeating the first line."
        });
        Console.WriteLine($"REST multi-turn: {JsonHelpers.GetOutputText(followUp.RootElement)}");
        // Expected output:
        //   REST multi-turn: <model-generated one-line continuation>

        // Step 4: Render SSE output-text deltas immediately; story wording is variable.
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
        // Expected output:
        //   REST SSE stream: <model-generated two-sentence lighthouse-keeper story>
        //   The story prints incrementally as deltas arrive.
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL");

// Step 5: Advertise the gated function, inspect its proposed arguments, and submit a decision.
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
        // Expected output:
        //   [APPROVED] $500.00
        //   or
        //   [REJECTED] $9000.00
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
    // Expected result:
    //   The approval decision is submitted as function_call_output.
    return JsonHelpers.GetOutputText(completed.RootElement);
}

// Your Turn: replace the Boolean with an explicit approval prompt, add a second gated
// operation, record a credential-free audit event, and count deltas for a longer stream.
