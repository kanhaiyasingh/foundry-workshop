# M15 · Capstone

> **Goal:** combine everything — a grounded, tool-using, evaluated, observable agent — into one coherent build, then see where to go next.
> **You'll use:** `DeclarativeAgentDefinition` (the C# equivalent of `PromptAgentDefinition`) with tools + knowledge, the Responses API, an evaluator, and tracing.

---

This is the victory lap. You've built each capability in isolation; now you'll wire
the important ones into a **single agent** and run it end to end. Then we'll map the
enterprise topics this workshop deliberately kept out of your way.

![Microsoft Foundry — one unified platform](../assets/platform-overview.png)

> **What we're assembling**
>
> A **"Contoso Support"** agent that:
>
> - is ready to be **grounded** on a knowledge base ([M4](04-grounding-rag.md)),
> - can call a **custom tool** ([M3](03-tools-and-function-calling.md)),
> - is **evaluated** for quality before we trust it ([M9](09-evaluation.md)),
> - and is **traced** so we can watch it in production ([M10](10-observability.md)).

Source: [`labs/15-capstone/Program.cs`](https://github.com/kanhaiyasingh/foundry-workshop/blob/main/labs/15-capstone/Program.cs)

## 1. Bootstrap (the pattern you now know by heart)

Same four lines from [M1](01-first-inference.md) — one client, reused for everything.
`WorkshopContext` loads `.env` and supplies the same `DefaultAzureCredential`,
`AIProjectClient`, and project-scoped OpenAI client.

```csharp
var projectEndpoint = context.Config.ProjectEndpoint;
var chatModel = context.Config.ChatModel;
var projectClient = context.CreateProjectClient();
var openAiClient = projectClient.ProjectOpenAIClient;

Console.WriteLine($"Ready to build the capstone agent on: {chatModel}");
```

> **Expected output**
>
> ```text
> Ready to build the capstone agent on: gpt-4.1-mini
> ```

## 2. A tool the agent can call

We give the support agent one **custom function tool** — looking up an order's status —
exactly as you did in [M3](03-tools-and-function-calling.md). In a real build this
would hit your order system; here it is a stub.

```csharp
static object GetOrderStatus(string orderId)
{
    var orders = new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["A-1001"] = new { status = "shipped", eta = "2026-06-15" },
        ["A-1002"] = new { status = "processing", eta = "2026-06-20" }
    };
    return orders.TryGetValue(orderId, out var order)
        ? order
        : new { status = "not_found" };
}

var orderToolParameters = BinaryData.FromObjectAsJson(
    new
    {
        type = "object",
        properties = new
        {
            order_id = new
            {
                type = "string",
                description = "e.g. A-1001"
            }
        },
        required = new[] { "order_id" }
    },
    JsonHelpers.Web);

var orderTool = ResponseTool.CreateFunctionTool(
    "get_order_status",
    orderToolParameters,
    null,
    "Look up the status and ETA of a customer order by its ID.");

Console.WriteLine("Tool defined: get_order_status");
```

> **Expected output**
>
> ```text
> Tool defined: get_order_status
> ```

## 3. Define the capstone agent

We create a **versioned agent** ([M2](02-your-first-agent.md)) whose definition carries
both **instructions** and the **tool**. In a full build you would also attach a Foundry
IQ **knowledge base** ([M4](04-grounding-rag.md)) here so answers are
grounded with citations.

```csharp
const string agentName = "contoso-support-agent";

ProjectsAgentDefinition definition = new DeclarativeAgentDefinition(chatModel)
{
    Instructions =
        "You are Contoso's support agent. Be concise and friendly. " +
        "Use the get_order_status tool whenever a customer asks about an order. " +
        "If grounding knowledge is attached, cite it. Never invent order data."
};
((DeclarativeAgentDefinition)definition).Tools.Add(orderTool);
// knowledge=[...] - attach a Foundry IQ knowledge base in a full build (M4).

var agentResult = await projectClient.AgentAdministrationClient
    .CreateAgentVersionAsync(
        agentName,
        new ProjectsAgentVersionCreationOptions(definition));
ProjectsAgentVersion agent = agentResult.Value;

Console.WriteLine($"Name    : {agent.Name}");
Console.WriteLine($"Version : {agent.Version}");
```

> **Expected output**
>
> ```text
> Name    : contoso-support-agent
> Version : 1
> ```

> **Tool + knowledge APIs are evolving**
>
> `DeclarativeAgentDefinition` is the C# `PromptAgentDefinition` surface in
> `Azure.AI.Projects.Agents`. The exact tools/knowledge shapes are pre-release. If a
> field name moves, re-check [M3](03-tools-and-function-calling.md) and
> [M4](04-grounding-rag.md) and keep the versions pinned centrally in
> `Directory.Packages.props`.

## 4. Run it — with the tool-call loop

Invoke through the Responses API with an `agent_reference`. If the model decides to
call the tool, run the function locally and feed the result back so it can finish its
answer — the `function_call → function_call_output` loop from
[M13](13-human-in-loop-rest.md).

```csharp
static async Task<string> RunSupportAsync(
    WorkshopContext context,
    string agentName,
    string userMessage)
{
    using var response = await context.Rest.CreateResponseAsync(new
    {
        input = new[] { new { role = "user", content = userMessage } },
        agent_reference = new { name = agentName, type = "agent_reference" }
    });

    var toolCalls = JsonHelpers.GetFunctionCalls(response.RootElement).ToArray();
    if (toolCalls.Length == 0)
    {
        return JsonHelpers.GetOutputText(response.RootElement);
    }

    var outputs = new List<object>();
    foreach (var call in toolCalls)
    {
        using var arguments = JsonDocument.Parse(
            call.GetProperty("arguments").GetString() ?? "{}");
        var orderId = arguments.RootElement.GetProperty("order_id").GetString()!;
        outputs.Add(new
        {
            type = "function_call_output",
            call_id = call.GetProperty("call_id").GetString(),
            output = JsonSerializer.Serialize(
                GetOrderStatus(orderId),
                JsonHelpers.Web)
        });
    }

    using var final = await context.Rest.CreateResponseAsync(new
    {
        input = outputs,
        previous_response_id = response.RootElement.GetProperty("id").GetString(),
        agent_reference = new { name = agentName, type = "agent_reference" }
    });
    return JsonHelpers.GetOutputText(final.RootElement);
}

Console.WriteLine(
    await RunSupportAsync(
        context,
        agent.Name,
        "Where is my order A-1001?"));
```

> **Expected output**
>
> ```text
> Your order A-1001 has shipped and is expected to arrive on 2026-06-15.
> Is there anything else I can help you with?
> ```
>
> The model called `get_order_status("A-1001")`, the host returned the stub data, and
> the model composed the final reply from that tool result.

The implementation validates the tool name and `order_id` before executing host code.
An unknown tool or malformed call fails explicitly rather than becoming a
success-shaped response.

## 5. Evaluate before you trust it

A capstone agent is not done until it is **measured** ([M9](09-evaluation.md)). Score
a couple of responses for **relevance** against the exact inline test set.

```csharp
var aoaiEndpoint = context.Config.AccountUri;
var relevance = new RelevanceJudge(context, chatModel, aoaiEndpoint);
var cases = new[]
{
    new EvaluationCase(
        "Where is my order A-1001?",
        await RunSupportAsync(
            context,
            agent.Name,
            "Where is my order A-1001?")),
    new EvaluationCase(
        "What's the ETA on A-1002?",
        await RunSupportAsync(
            context,
            agent.Name,
            "What's the ETA on A-1002?"))
};

foreach (var evaluationCase in cases)
{
    var score = await relevance.ScoreAsync(
        evaluationCase.Query,
        evaluationCase.Response);
    Console.WriteLine(
        $"{evaluationCase.Query[..Math.Min(28, evaluationCase.Query.Length)],-30} " +
        $"relevance = {score}/5");
}
```

The C# evaluator uses the same release-gate contract as `RelevanceEvaluator`: a strict
integer score from 1 through 5 for each query/response pair. Its exact judge prompt is:

```text
You are an impartial relevance evaluator.
Score how directly and completely RESPONSE addresses QUERY.
Use only an integer from 1 (not relevant) through 5 (fully relevant).

QUERY:
{query}

RESPONSE:
{response}

Return only JSON matching the supplied schema.
```

> **Expected output**
>
> ```text
> Where is my order A-1001?      relevance = 5/5
> What's the ETA on A-1002?      relevance = 4/5
> ```
>
> Scores will vary. The point is that you have a **number** to gate releases on, not a
> vibe.

> **Judge endpoint: account, not project**
>
> Python's AI-assisted evaluators authenticate the judge through the classic Azure
> OpenAI `?api-version=` route, which lives on the **account** endpoint rather than the
> `/api/projects/<project>` endpoint. The C# port derives the same `AOAI_ENDPOINT` by
> stripping that suffix. Because the stable C# packages do not mirror
> `AzureOpenAIModelConfiguration`/`RelevanceEvaluator`, the implementation sends the
> same 1–5 relevance rubric to the account-level chat-completions route with AAD and a
> strict JSON schema. No evaluation behavior is replaced with a heuristic.

For a release pipeline, treat scores below the M9 threshold of `3` as a failed gate.
Keep the two exact cases fixed as a regression set; do not silently drop a failed row.

## 6. Make it observable

Finally, turn on tracing ([M10](10-observability.md)) so capstone runs emit
spans to **Application Insights**. The optional setup is not a functional dependency.

```csharp
using TracerProvider? tracerProvider =
    context.Config.IsConfigured("APP_INSIGHTS_CONN_STRING")
        ? Sdk.CreateTracerProviderBuilder()
            .AddSource(CapstoneTelemetry.SourceName)
            .AddAzureMonitorTraceExporter(options =>
                options.ConnectionString =
                    context.Config.Require("APP_INSIGHTS_CONN_STRING"))
            .Build()
        : null;

if (tracerProvider is not null)
{
    Console.WriteLine(
        "Tracing on — capstone runs now export spans to App Insights.");
}
else
{
    Console.WriteLine(
        "Set APP_INSIGHTS_CONN_STRING in .env to enable tracing (see M10).");
}
```

> **Expected output**
>
> ```text
> Tracing on — capstone runs now export spans to App Insights.
> ```
>
> In the portal's **Monitor** tab (or via KQL), a traced run shows spans for each
> Responses call and local tool execution — the full picture of what the agent did.

The notebook can configure tracing in its final cell and then re-run earlier cells.
The C# lab is a one-shot process, so it initializes this optional provider before
creating the agent while keeping the status output here in notebook order. It emits a
parent support span, one child span per Responses call, and one child span per local
tool execution. Only operational tags (`gen_ai.system`, model, agent name, response
id, tool name, and tool-call count) are recorded, not prompt or response content.

## 🧪 Your turn — make it yours

1. **Ground it for real.** Attach a Foundry IQ knowledge base from
   [M4](04-grounding-rag.md) and add a question whose answer must come from
   a document — confirm the agent cites it.
2. **Add a guardrail.** Pin a guardrail policy from [M11](11-guardrails.md) to the
   deployment and try a prompt-injection input; confirm it is blocked.
3. **Harden + measure.** Run the [M12](12-red-teaming.md) scan against your capstone
   agent, then add the worst-scoring prompts to your [M9](09-evaluation.md) test set
   and re-evaluate.

## 🚀 Where to go next

You built the *application* layer end to end. The reference series this workshop draws
from goes deeper on the **enterprise platform** — pick your next thread:

| Topic | What it adds | Start with |
|:--|:--|:--|
| **Hosted agents** | Deploy your agent as a containerized (ACR-backed) service for portability and scale. | Reference lab `08-03-hosted-agents` |
| **Multi-agent at scale** | Grow [M7](07-multi-agent-orchestration.md) into a production router + specialist fleet. | Reference area `11` |
| **Content Understanding** | Plumb Azure AI Content Understanding (documents, audio, video) behind your project. | Reference area `09` |
| **Hub-and-spoke infra** | The Bicep/APIM topology, per-team quotas, and a governed gateway from [Concepts](../concepts.md). | Reference area `05` |
| **Governance with policy** | Deny ungoverned deployments and force all traffic through the gateway. | Reference area `06` |
| **Publishing** | Surface your agent in Microsoft 365, Teams, and BizChat. | Control plane docs |

Read the [Concepts](../concepts.md) page once more — now every box in that diagram is
something you've actually built.

---

✅ **You shipped a grounded-ready, tool-using, evaluated, observable agent on Microsoft
Foundry — end to end.** That's the whole workshop. Nicely done.

← Back to [the workshop home](../index.md) · revisit any lab from there.

## Configuration, success checks, cleanup, and cost

Run from the repository root:

```powershell
dotnet run --project .\labs\15-capstone -- --check
dotnet run --project .\labs\15-capstone
```

`--check` makes no Azure calls. The configured run succeeds when it:

1. prints the configured model and `get_order_status` tool,
2. creates a new version of `contoso-support-agent`,
3. answers the exact `A-1001` prompt from tool output,
4. prints both 1–5 relevance scores, and
5. reports whether Application Insights tracing is enabled.

The capstone creates a persistent prompt-agent version and Responses/evaluator traffic;
those operations and optional telemetry incur normal service cost. Delete
`contoso-support-agent` from the Foundry project when you finish if it is not needed.
Remove any knowledge attachment or guardrail deployment added in **Your turn**, and
disable/delete any online evaluation rule carried forward from M10. No local dataset or
result artifact is created by the safe default path.
