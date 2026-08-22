# M8 - Deep research

## Objective

Let a reasoning model plan a bounded search/fetch loop over an approved corpus and produce
a cited synthesis without crossing the knowledge boundary.

## Prerequisites

- `PROJECT_ENDPOINT`
- `RESEARCH_MODEL` set to a deployment that supports function tools

## Run

```powershell
dotnet run --project .\labs\08-deep-research -- --check
dotnet run --project .\labs\08-deep-research
```

Source: [`labs/08-deep-research/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/08-deep-research/Program.cs)

## Code flow

1. Define a four-document paper corpus.
2. Expose `search` and `fetch` function schemas.
3. Let the model choose calls, execute them in C#, and return results with
   `previous_response_id`.
4. Stop when the model returns no tool call or fail at the iteration safety limit.
5. Require `[doc-id]` citations and explicit uncertainty outside the corpus.

## Expected output

```text
Iteration 1: <n> tool call(s)
Iteration 2: <n> tool call(s)
...
<comparison citing [doc-001], [doc-002], and [doc-003]>
```

The sequence is model-dependent; the bounded stop condition is deterministic.

## Your Turn

Ask an out-of-scope fusion-energy question and confirm the model declines. Then replace
the dictionary search body with the M4 knowledge-base retrieval call without changing the
orchestration loop.

## Cleanup and cost

No resources persist. Reasoning models and repeated tool-loop calls can consume more tokens
than a single response; keep the iteration cap and inspect usage.

## Parity and preview caveats

The loop uses the Responses REST tool shape. The specialized deep-research model catalog
and supported tools vary by region.
