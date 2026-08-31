# M1 - First inference

## Objective

Call Foundry through classic chat completions, batch embeddings, streaming chat, and the
Responses API while preserving the notebook's complete first-inference flow.

## Prerequisites

- Completed [setup](../setup.md)
- `PROJECT_ENDPOINT`, `CHAT_MODEL`, and `EMBEDDING_MODEL`
- Foundry User access to both deployments

## Run

```powershell
dotnet run --project .\labs\01-first-inference -- --check
dotnet run --project .\labs\01-first-inference
```

Source: [`labs/01-first-inference/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/01-first-inference/Program.cs)

## Code flow

1. Load and print the project, chat deployment, and embedding deployment.
2. Build `AIProjectClient` and the account-scoped OpenAI data-plane route.
3. Run classic chat completions and print model, token usage, and answer text.
4. Embed three strings in one batch and preview every vector.
5. Stream a chat-completions response incrementally.
6. Make the minimal Responses API call used by later agent labs.

## Expected output

```text
Model  : <chat deployment>
Tokens : <model-dependent count>
<catastrophic-forgetting explanation>
Model      : <embedding deployment>
Dimensions : <deployment-dependent dimensions>
[0] [<first three values>, ...]  (<dimensions> dims)
[1] [<first three values>, ...]  (<dimensions> dims)
[2] [<first three values>, ...]  (<dimensions> dims)
<streamed Microsoft Foundry sentence>
Saturn is a planet famous for its prominent ring system.
```

Model wording, token usage, vectors, and embedding dimensions vary by deployment.

## Your Turn

1. **Swap the model.** If you deployed a reasoning model, set `REASONING_MODEL` in `.env`,
   read it with `context.Config.Require("REASONING_MODEL")`, and use it in
   `GetProjectResponsesClientForModel(...)`. How does the answer style change?
2. **Compare token usage.** Ask a long question and a short one, then print
   `usage.total_tokens` from each chat-completions response.
3. **Embed and compare.** Embed two similar sentences and two different ones, normalize
   the vectors, and compute cosine similarity in C#. The similar pair should score higher.

## Cleanup and cost

This lab creates no persistent resources. It consumes model and embedding tokens.

## Parity and preview caveats

Responses uses the native stable SDK. Classic chat and embeddings use authenticated REST
because this project routes those operations through the account data plane.
