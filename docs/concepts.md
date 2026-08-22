# Concepts

The workshop has one through-line:

> Start with one model call and finish with a grounded, tool-using, evaluated,
> observable agent inside one Foundry project.

The C# projects and preserved Python notebooks teach the same architectural ideas. Their
syntax and SDK abstractions differ, but the service boundaries, identity model, resource
requirements, and expected learning outcomes are shared.

## Clients and endpoints

`AIProjectClient` provides stable project/agent operations. The shared library also uses
authenticated REST where a stable .NET wrapper does not exist.

![The inference path](assets/inference-path.png)

Project-scoped Responses and agents use:

```text
https://<account>.services.ai.azure.com/api/projects/<project>
```

Classic embeddings, fine-tuning, and some evaluator routes use the account endpoint:

```text
https://<account>.services.ai.azure.com
```

The shared `WorkshopConfig.AccountUri` derives that split consistently.

## Agents and tools

A prompt agent is a versioned model, instructions, and tool definition. Function tools
follow a loop: receive `function_call`, validate/execute in trusted host code, return
`function_call_output`, and let the model synthesize the final answer.

![Anatomy of a Foundry agent](assets/agent-anatomy.png)

MCP moves tool discovery and invocation behind a standard remote protocol. Work IQ adds
delegated Microsoft 365 permission trimming.

## Grounding and memory

RAG retrieves approved documents before generation. M4 builds Azure AI Search resources
with the stable SDK and exposes the knowledge base through MCP.

![Grounding with Foundry IQ](assets/rag-foundry-iq.png)

Memory is different: it extracts durable, scope-isolated user facts across conversations.
The API remains preview, so M6 uses a typed C# workflow over REST.

## Orchestration

M7 creates Microsoft Agent Framework `ChatClientAgent` instances for a router and
specialists. Routing keeps each specialist's instructions narrow and testable.

![Multi-agent router](assets/multi-agent-router.png)

## Trust loop

Evaluation, observability, guardrails, red teaming, and human approval provide different
controls:

- evaluation measures quality before release;
- tracing explains runtime behavior;
- guardrails block known unsafe classes;
- red teaming searches for bypasses;
- human approval prevents irreversible tool execution without authorization.

![Evaluation and observability](assets/eval-observability.png)

M14 changes the model only after a baseline and validated data exist. M15 combines the
patterns and scores the resulting support response.
