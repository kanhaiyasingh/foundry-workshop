# M6 - Agent memory

## Objective

Create a preview memory store, extract durable user preferences from a conversation, and
retrieve those facts under an isolated user scope.

## Prerequisites

- `PROJECT_ENDPOINT`, `CHAT_MODEL`, and `EMBEDDING_MODEL`
- Memory enabled for the Foundry project/region
- Permission to manage project memory stores

## Run

```powershell
dotnet run --project .\labs\06-agent-memory -- --check
dotnet run --project .\labs\06-agent-memory
```

Source: [`labs/06-agent-memory/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/06-agent-memory/Program.cs)

## Code flow

1. Delete and recreate the workshop-named memory store for deterministic reruns.
2. Submit user/assistant messages to `:update_memories`.
3. Poll the update operation because extraction is asynchronous service behavior.
4. Search the same scope for coding preferences and print the raw memory response.

## Expected output

```text
Created memory store: csharp-workshop-dev-preferences
Recalled memories:
{ ... C#, concise, code-first, VS Code, Windows ... }
```

Exact extracted wording varies by the configured chat model.

## Your Turn

Write another preference, retrieve it with the same scope, then query with a different
scope and confirm the first user's memory is absent.

## Cleanup and cost

The lab leaves the named memory store for inspection. Delete it when finished. Extraction
and semantic retrieval consume model, embedding, and storage capacity.

## Parity and preview caveats

Azure.AI.Projects 2.0 has no stable memory client. The lab therefore implements the real
`2025-11-15-preview` REST operations and surfaces service failures directly.
