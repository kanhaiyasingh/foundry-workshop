# M8 - Deep research

## Objective

Let a reasoning model plan a bounded search/fetch loop over an approved corpus and produce
a cited synthesis without crossing the knowledge boundary.

> **Goal:** Run an agentic research loop in which a reasoning model plans, searches an
> approved knowledge source, iterates, and returns a cited synthesis.
>
> **You'll use:** The `RESEARCH_MODEL` deployment through the Responses API with
> `search` and `fetch` function tools.

## How the research loop works

A normal chat answer is one shot. Deep research is different: the model decides what to
search, reads the documents it fetches, searches again to fill gaps, and concludes only
when it has enough evidence. The C# host executes every tool call against the bounded
corpus and returns the result to the same response chain.

```text
question -> RESEARCH_MODEL ----> search(query) ----+
                  |                                |
                  +---- tool results <-------------+  repeat while tools are requested
                  |
                  +-------------------------------> cited report with [doc-id] sources
```

!!! note "One project, one bounded research loop"
    The C# lab uses one project endpoint and the configured `RESEARCH_MODEL` for both
    investigation and the final cited response. The loop stops when the model requests
    no more tools, or fails safely after six iterations. Deep-research model and tool
    support varies by deployment and region.

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

1. **Add a document.** Add `doc-005` about cross-lingual transfer to `corpus`, then ask a
   multilingual-NLP question. Confirm the loop searches, fetches, and cites `[doc-005]`.
2. **Watch it iterate.** Ask, "Contrast few-shot metric methods with efficient attention."
   Inspect each printed `Iteration <n>: <count> tool call(s)` line; you should see multiple
   search/fetch rounds.
3. **Tune the cap.** Lower `maxIterations` to `1` and observe the loop stop early instead
   of producing a complete report. Raise it again and watch the model dig deeper. This is
   the cost/quality dial.

## Cleanup and cost

No resources persist. Reasoning models and repeated tool-loop calls can consume more tokens
than a single response; keep the iteration cap and inspect usage.

## Parity and preview caveats

The loop uses the Responses REST tool shape. The specialized deep-research model catalog
and supported tools vary by region.
