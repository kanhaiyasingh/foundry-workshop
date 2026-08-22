# M1 - First inference

## Objective

Call a Foundry model through the stable `Azure.AI.Projects` 2.x client, create an
embedding through the account data plane, and consume Responses API SSE deltas.

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

1. `AIProjectClient` creates a native `ProjectResponsesClient`.
2. The first call verifies model inference.
3. An authenticated REST call uses the account endpoint for embeddings.
4. `FoundryRestClient.StreamResponseTextAsync` parses `response.output_text.delta`
   events without buffering the full response.

## Expected output

```text
Response: Foundry is ready.
Embedding dimensions: 3072
Streaming: The Responses API ...
```

The wording of the streamed sentence can vary. The embedding dimension reflects the
configured deployment.

## Your Turn

Change the embedding input to two strings and inspect both vectors. Then update the
streaming prompt and render deltas with a different console color.

## Cleanup and cost

This lab creates no persistent resources. It consumes model and embedding tokens.

## Parity and preview caveats

Responses use the native stable SDK. The embedding call intentionally uses REST because
the workshop demonstrates the endpoint split explicitly: project for Responses, account
for classic embedding deployment routes.
