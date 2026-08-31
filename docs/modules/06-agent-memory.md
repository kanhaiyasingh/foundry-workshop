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

Source: [`labs/06-agent-memory/Program.cs`](https://github.com/kanhaiyasingh/foundry-workshop/blob/main/labs/06-agent-memory/Program.cs)

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

1. **Teach it something new.** Add “I've switched to nullable reference types everywhere”
   to `conversation`, submit another update, wait for extraction, then search in a new
   call and confirm that the memory is returned.
2. **Prove isolation.** Change `scope` to `"workshop-user-sam"` and run the same search. It
   should not return Dana's preferences.
3. **Go production-style.** Resolve `scope` from `context.Config.Require("USER_ID")`
   instead of a fixed string, so one application instance can serve users with isolated
   memory.

## Cleanup and cost

The lab leaves the named memory store for inspection. Delete it when finished. Extraction
and semantic retrieval consume model, embedding, and storage capacity.

## Parity and preview caveats

Azure.AI.Projects 2.0 has no stable memory client. The lab therefore implements the real
`2025-11-15-preview` REST operations and surfaces service failures directly.
