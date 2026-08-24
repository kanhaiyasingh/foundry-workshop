# M13 - Human-in-the-Loop & REST

> **Goal:** pause an agent for **human approval** before a risky tool call - then learn
> to invoke that same agent over **raw REST** (single-shot, multi-turn, streaming).
>
> **You'll use:** C# `ResponseTool`, the Responses API approval pattern, and
> `HttpClient` against `/openai/v1/responses`.

This lab has **two themes**. First, **human-in-the-loop (HITL)**: when an agent wants
to call a tool that is irreversible - moving money, deleting data - you do not want
it firing unattended. Foundry's Responses API makes this natural: it returns the tool
call as an output item **without executing it**, so *your* code can route it to a human
first.

Then we drop below the SDK to the **raw REST** surface. Every Responses call is an
HTTPS POST with a bearer token. The lab reproduces it with `HttpClient`, chains turns
with `previous_response_id`, and streams tokens over Server-Sent Events.

![Anatomy of a Foundry agent](../assets/agent-anatomy.png)

> [!NOTE]
> Sections 1-3 are HITL; sections 4-6 are REST. The two halves share one agent: you
> build a payments agent with an approval-gated tool in sections 1-3, then invoke that
> exact agent over HTTP in sections 4-6. The versioned-agent API is preview, so keep
> the repository's `Azure.AI.Projects.Agents` version pinned if a symbol drifts.

## Run

```powershell
dotnet run --project .\labs\13-human-in-loop-rest -- --check
dotnet run --project .\labs\13-human-in-loop-rest -- --check --offline
dotnet run --project .\labs\13-human-in-loop-rest -- --offline
dotnet run --project .\labs\13-human-in-loop-rest
```

The notebook's two callback decisions make the default run non-interactive and
deterministic: the `$500` request is approved and the `$9000` request is rejected. To
put a real console prompt at the callback point, use:

```powershell
dotnet run --project .\labs\13-human-in-loop-rest -- --interactive
```

Answer `y` for the first request and `n` for the second. No implementation in this lab
moves real money.

`--offline` is the safe, meaningful smoke path. It creates no agent, acquires no
token, and sends no HTTP request. Labeled deterministic wire fixtures exercise the
approval and rejection branches, safe-tool execution, `function_call_output`,
`previous_response_id`, output-text aggregation, and SSE parsing. It is not
represented as a live Foundry response.

Source: [`labs/13-human-in-loop-rest/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/13-human-in-loop-rest/Program.cs)

The first notebook code cell prints the current date and time:

```csharp
Console.WriteLine($"Current date and time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}");
```

## 1. Configure & build the client

The canonical bootstrap. We also name the agent up front - we will reference it by
**name** both through the SDK here and over REST later.

```csharp
var projectEndpoint = offline
    ? "<offline fixture>"
    : context.Config.ProjectEndpoint;
var chatModel = context.Config.ChatModel;
const string agentName = "payments-approval-agent";

Console.WriteLine($"Project    : {projectEndpoint}");
Console.WriteLine($"Model      : {chatModel}");
Console.WriteLine($"Agent name : {agentName}");
```

> [!NOTE]
> Expected output:
>
> ```text
> Project    : https://<account>.services.ai.azure.com/api/projects/<project>
> Model      : gpt-4.1-mini
> Agent name : payments-approval-agent
> ```

`WorkshopContext` loads the repository `.env`, creates the configured
`DefaultAzureCredential` or `AzureCliCredential`, and constructs the project and REST
clients. This is the C# architecture change for the notebook's explicit
`load_dotenv`, `DefaultAzureCredential`, `AIProjectClient`, and OpenAI client setup.
The project client is constructed only on the live branch, immediately before agent
version creation.

## 2. Define tools - and create the agent

Two function schemas are stored on the versioned agent. `get_account_balance` is
read-only and safe to auto-run; `transfer_funds` is irreversible. The
`approvalRequiredTools` set is the convention that decides which calls get
intercepted - it is **our policy**, not something the model enforces. We also keep mock
implementations so the demo runs end-to-end.

```csharp
var approvalRequiredTools = new HashSet<string>(StringComparer.Ordinal)
{
    "transfer_funds"
};

ResponseTool getBalanceTool = ResponseTool.CreateFunctionTool(
    functionName: "get_account_balance",
    functionParameters: BinaryData.FromObjectAsJson(new
    {
        type = "object",
        properties = new
        {
            account_id = new { type = "string" }
        },
        required = new[] { "account_id" }
    }),
    strictModeEnabled: false,
    functionDescription:
        "Get the current balance for an account. Safe to execute automatically.");

ResponseTool transferTool = ResponseTool.CreateFunctionTool(
    functionName: "transfer_funds",
    functionParameters: BinaryData.FromObjectAsJson(new
    {
        type = "object",
        properties = new
        {
            from_account = new { type = "string" },
            to_account = new { type = "string" },
            amount = new { type = "number" }
        },
        required = new[] { "from_account", "to_account", "amount" }
    }),
    strictModeEnabled: false,
    functionDescription:
        "Transfer funds between accounts. REQUIRES human approval before execution.");

var definition = new DeclarativeAgentDefinition(chatModel)
{
    Instructions =
        "You are a banking assistant with two tools: get_account_balance and " +
        "transfer_funds. Call the tool directly - do not describe what you will " +
        "do. The system handles human approval for transfer_funds."
};
definition.Tools.Add(getBalanceTool);
definition.Tools.Add(transferTool);

var projectClient = context.CreateProjectClient();
_ = projectClient.ProjectOpenAIClient;
var agentResult = await projectClient.AgentAdministrationClient
    .CreateAgentVersionAsync(
        agentName,
        new ProjectsAgentVersionCreationOptions(definition)
        {
            Description =
                "HITL demo - financial transactions with human approval for transfers."
        });
ProjectsAgentVersion agent = agentResult.Value;

Console.WriteLine($"Agent '{agent.Name}' ready (version {agent.Version}).");
Console.WriteLine("Approval-required: {'transfer_funds'}");
```

The mock implementations preserve the notebook's exact data:

```csharp
static string GetAccountBalance(string accountId)
{
    var balance = accountId == "ACC-001" ? 5000m : 0m;
    return $"Account {accountId} balance: ${balance:N2}";
}

static string TransferFunds(string fromAccount, string toAccount, decimal amount) =>
    $"Transferred ${amount:N2} from {fromAccount} to {toAccount}.";
```

> [!NOTE]
> Expected output on a new project:
>
> ```text
> Agent 'payments-approval-agent' ready (version 1).
> Approval-required: {'transfer_funds'}
> ```
>
> The agent *advertises* both tools to the model. Whether a call actually executes is
> a decision **your** loop makes next - that is the whole point of HITL. If this stable
> agent name already exists, Foundry may report its existing or next version rather
> than version 1.

## 3. The approval loop - approve & reject

Here is the pattern. Create a response, then scan `output` for `function_call` items.
Auto-execute safe tools; for approval-required tools, ask a human. Submit every result
as a `function_call_output` via `previous_response_id` and loop until no tool calls
remain.

The notebook passes the human decision as an `approve` callback so the cell stays
runnable. The C# port preserves those deterministic callbacks by default and lets
`--interactive` replace them with `Console.ReadLine`.

```csharp
static async Task<string> RunWithHitlAsync(
    Func<object, Task<JsonDocument>> createResponse,
    string agentName,
    string userMessage,
    IReadOnlySet<string> approvalRequiredTools,
    Func<string, JsonElement, bool> approve)
{
    JsonDocument response = await createResponse(new
    {
        input = new[] { new { role = "user", content = userMessage } },
        agent_reference = new { name = agentName, type = "agent_reference" }
    });

    try
    {
        while (true)
        {
            var calls = JsonHelpers.GetFunctionCalls(response.RootElement).ToArray();
            if (calls.Length == 0)
            {
                return JsonHelpers.GetOutputText(response.RootElement);
            }

            var outputs = new List<object>();
            foreach (var call in calls)
            {
                var name = call.GetProperty("name").GetString()!;
                var callId = call.GetProperty("call_id").GetString()!;
                using var arguments = JsonDocument.Parse(
                    call.GetProperty("arguments").GetString() ?? "{}");
                var toolArgs = arguments.RootElement;

                string result;
                if (approvalRequiredTools.Contains(name))
                {
                    if (approve(name, toolArgs))
                    {
                        result = ExecuteTool(name, toolArgs);
                        Console.WriteLine(
                            $"[APPROVED] {name}({toolArgs.GetRawText()}) -> {result}");
                    }
                    else
                    {
                        result = $"Action '{name}' was rejected by the operator.";
                        Console.WriteLine($"[REJECTED] {name} will not execute");
                    }
                }
                else
                {
                    result = ExecuteTool(name, toolArgs);
                    Console.WriteLine(
                        $"[AUTO] {name}({toolArgs.GetRawText()}) -> {result}");
                }

                outputs.Add(new
                {
                    type = "function_call_output",
                    call_id = callId,
                    output = result
                });
            }

            var next = await createResponse(new
            {
                input = outputs,
                previous_response_id =
                    response.RootElement.GetProperty("id").GetString(),
                agent_reference = new { name = agentName, type = "agent_reference" }
            });
            response.Dispose();
            response = next;
        }
    }
    finally
    {
        response.Dispose();
    }
}

Func<object, Task<JsonDocument>> createResponse =
    body => context.Rest.CreateResponseAsync(body);

Console.WriteLine(">>> APPROVE path");
Console.WriteLine(await RunWithHitlAsync(
    createResponse,
    agent.Name,
    "Transfer $500 from ACC-001 to ACC-002.",
    approvalRequiredTools,
    (name, args) => true));

Console.WriteLine("\n>>> REJECT path");
Console.WriteLine(await RunWithHitlAsync(
    createResponse,
    agent.Name,
    "Transfer $9000 from ACC-001 to ACC-002.",
    approvalRequiredTools,
    (name, args) => false));
```

> [!NOTE]
> Expected output:
>
> ```text
> >>> APPROVE path
> [APPROVED] transfer_funds({"from_account":"ACC-001","to_account":"ACC-002","amount":500}) -> Transferred $500.00 from ACC-001 to ACC-002.
> The transfer of $500.00 from ACC-001 to ACC-002 is complete.
>
> >>> REJECT path
> [REJECTED] transfer_funds will not execute
> I wasn't able to complete that transfer - it was rejected by the operator.
> ```
>
> On approve, the tool runs and the agent confirms. On reject, the rejection string
> is fed back as the tool result, so the agent gracefully reports the decline.

> [!TIP]
> Swap the `approve` callback for whatever fits your app: the included blocking
> `Console.ReadLine` path, a Teams Adaptive Card, or an asynchronous approval queue.
> The Responses API holds the run open via `previous_response_id`; nothing executes
> until you submit the `function_call_output`.

## 4. Drop to raw REST - single-shot

Same agent, no Responses SDK. Every request is an HTTPS **POST** to
`{endpoint}/openai/v1/responses` with a **bearer token** for the
`https://ai.azure.com/.default` audience - the exact scope used by the SDK. The body
is just `input` plus the top-level `agent_reference`.

In C#, `WorkshopContext.Rest.CreateRequestAsync` obtains that bearer token and creates
the raw `HttpRequestMessage`; `HttpClient` sends it.

```csharp
var responsesUrl =
    new Uri($"{projectEndpoint.TrimEnd('/')}/openai/v1/responses");

var body = new
{
    input = new[]
    {
        new
        {
            role = "user",
            content = "What is my balance for account ACC-001?"
        }
    },
    agent_reference = new { name = agent.Name, type = "agent_reference" }
};

Func<object, Task<(int StatusCode, JsonDocument Payload)>> sendResponse =
    body => PostResponseAsync(context, httpClient, responsesUrl, body);

var (statusCode, result) = await PostResponseWithSafeToolsAsync(
    sendResponse,
    body,
    agent.Name,
    approvalRequiredTools);

Console.WriteLine($"HTTP   : {statusCode}");
Console.WriteLine($"Resp id: {result.RootElement.GetProperty("id").GetString()}");
Console.WriteLine($"Status : {result.RootElement.GetProperty("status").GetString()}");
Console.WriteLine($"Output : {JsonHelpers.GetOutputText(result.RootElement)}");
```

`PostResponseWithSafeToolsAsync` still uses raw `HttpClient` requests. It executes only
non-gated mock calls and submits their `function_call_output` over raw REST until the
single user turn has a final message. It throws rather than execute any tool listed in
`approvalRequiredTools`. This explicit host loop is necessary because a stored custom
function schema advertises a function; it does not deploy the local C# implementation
to Foundry.

`JsonHelpers.GetOutputText` aggregates visible text from the raw Responses payload.
The wire JSON has no top-level `output_text` key; that is a convenience synthesized
by typed SDK responses. The helper concatenates the `output_text` content parts:

```csharp
foreach (var item in response.GetProperty("output").EnumerateArray())
{
    foreach (var part in item.GetProperty("content").EnumerateArray())
    {
        if (part.GetProperty("type").GetString() == "output_text")
        {
            parts.Add(part.GetProperty("text").GetString() ?? string.Empty);
        }
    }
}
```

The delegate is `context.Rest.CreateResponseAsync` in live mode and a deterministic
wire responder in `--offline` mode. The loop itself is identical in both paths.

> [!NOTE]
> Expected output:
>
> ```text
> [AUTO] get_account_balance({"account_id":"ACC-001"}) -> Account ACC-001 balance: $5,000.00
> HTTP   : 200
> Resp id: resp_01J8X...
> Status : completed
> Output : Account ACC-001 has a balance of $5,000.00.
> ```
>
> `agent_reference.name` resolves to the agent's **latest** version; add
> `"version": "1"` to pin one. The host auto-runs the read-only
> `get_account_balance` mock without an approval stop, submits its output, and prints
> the final text.

## 5. Multi-turn over REST - `previous_response_id`

To continue a conversation you **do not** resend history. Capture the first response
`id` and pass it as `previous_response_id` on the next POST. The service rehydrates
the prior state server-side. It is the same field and semantics as the SDK; here it is
just another JSON key.

```csharp
var (_, turn1) = await PostResponseAsync(
    context,
    httpClient,
    responsesUrl,
    new
    {
        input = new[]
        {
            new
            {
                role = "user",
                content = "Invent a one-line story about an astronaut named Mira."
            }
        },
        agent_reference = new { name = agent.Name, type = "agent_reference" }
    });
Console.WriteLine($"Turn 1: {JsonHelpers.GetOutputText(turn1.RootElement)}");

var (_, turn2) = await PostResponseAsync(
    context,
    httpClient,
    responsesUrl,
    new
    {
        input = new[]
        {
            new
            {
                role = "user",
                content = "Now tell me what happens next, in one line."
            }
        },
        previous_response_id = turn1.RootElement.GetProperty("id").GetString(),
        agent_reference = new { name = agent.Name, type = "agent_reference" }
    });
Console.WriteLine($"Turn 2: {JsonHelpers.GetOutputText(turn2.RootElement)}");
```

> [!NOTE]
> Expected output:
>
> ```text
> Turn 1: Mira drifted past Saturn's rings, humming a lullaby to the dark.
> Turn 2: A reply hummed back - and Mira realised the dark had been listening.
> ```
>
> Turn 2 carried no copy of turn 1's text, yet the agent continued the thread. The
> server held the history keyed by `previous_response_id`. This is the same primitive
> the HITL loop in section 3 used to submit `function_call_output` back into an open
> run.

## 6. Streaming over REST - Server-Sent Events

For token-by-token UIs, add **`"stream": true`**. The response content type flips
from `application/json` to `text/event-stream`: a sequence of `data: {json}` lines.
Dispatch on each event's `type` and accumulate `response.output_text.delta` chunks as
they land.

```csharp
var streamBody = new
{
    input = new[]
    {
        new
        {
            role = "user",
            content = "Tell me a three-sentence story about a lighthouse keeper."
        }
    },
    agent_reference = new { name = agent.Name, type = "agent_reference" },
    stream = true
};

using var request = await context.Rest.CreateRequestAsync(
    HttpMethod.Post,
    responsesUrl,
    FoundryRestClient.FoundryScope);
request.Headers.Accept.Add(
    new MediaTypeWithQualityHeaderValue("text/event-stream"));
request.Content = new StringContent(
    JsonSerializer.Serialize(streamBody, JsonHelpers.Web),
    Encoding.UTF8,
    "application/json");

using var response = await httpClient.SendAsync(
    request,
    HttpCompletionOption.ResponseHeadersRead);
response.EnsureSuccessStatusCode();
Console.WriteLine(
    $"content-type: {response.Content.Headers.ContentType?.MediaType}\n");

await using var stream = await response.Content.ReadAsStreamAsync();
using var reader = new StreamReader(stream);
await ConsumeSseAsync(reader);
```

`ConsumeSseAsync` contains the notebook's loop: it ignores non-`data:` lines, stops
at `[DONE]`, counts every event `type`, appends and prints
`response.output_text.delta`, then reports the character and event totals. Extracting
the parser lets `--offline` feed the same code through a `StringReader`.

> [!NOTE]
> The story prints **incrementally** as deltas arrive, then the tallies:
>
> ```text
> content-type: text/event-stream
>
> Every night the keeper lit the lamp against the fog. One storm, a small boat
> followed it home. By dawn, the keeper had a new friend and a story worth telling.
>
> Chars   : 218
> Events  : {"response.created":1,"response.output_item.added":1,"response.output_text.delta":47,"response.output_text.done":1,"response.completed":1}
> ```
>
> The concatenated delta chunks equal the output text aggregated in section 4.
> Streaming just hands it to you a few tokens at a time.

> [!WARNING]
> Access tokens are short-lived, typically around 60-90 minutes. The C# port calls
> `CreateRequestAsync` for each raw request, which obtains a current token. A
> long-running service should likewise refresh a token per request or cache it only
> until near expiry.

## Your Turn

1. **Add a second gated tool.** Give the agent a `close_account` tool, add it to
   `approvalRequiredTools`, and confirm `RunWithHitlAsync` intercepts it too. Ask the
   agent to *"close ACC-003"* and reject it.
2. **Pin a version over REST.** Re-version the agent by editing its instructions and
   calling `CreateAgentVersionAsync` again. Then add `"version": "1"` to the REST
   `agent_reference` and prove the **older** behavior still answers.
3. **Count streaming events.** Re-run section 6 with a longer prompt and compare the
   `response.output_text.delta` count. More text means more deltas, but still **one**
   `response.completed`.

You gated a risky tool behind human approval, then invoked the same agent over raw
REST: single-shot, multi-turn, and streaming. Next, shrink a big model into a smaller,
cheaper one that mimics it in
[M14 - Fine-Tuning & Distillation](14-fine-tuning.md).

## Cleanup and cost

The `payments-approval-agent` version is a persistent project resource. Delete
workshop agent versions from the Foundry portal when they are no longer needed.
Responses calls and streamed output consume model tokens. The two tool
implementations are deterministic in-process mocks: they do not move money, contact
an external bank, or create any financial side effect. `--offline` creates no
persistent or billable resource and is the recommended smoke command.

## C# SDK differences

- `ResponseTool.CreateFunctionTool` is the C# equivalent of the notebook's
  `FunctionTool`.
- `DeclarativeAgentDefinition` and
  `AgentAdministrationClient.CreateAgentVersionAsync` are the C# equivalents of
  `PromptAgentDefinition` and `project_client.agents.create_version`.
- The HITL loop intentionally uses the shared raw Responses client so it can send and
  inspect `function_call` and `function_call_output` wire items directly.
- The REST sections use `HttpClient` and raw JSON. The only shared helper used there
  obtains the Foundry bearer token and creates the authenticated request; it does not
  call the Responses SDK.
- The offline responder emits the same wire-level item shapes and SSE event types but
  is explicitly a test fixture, not a model or service emulator.
