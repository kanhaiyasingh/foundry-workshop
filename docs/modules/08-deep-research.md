# M8 - Deep research

## Objective

Let a reasoning model plan a bounded search/fetch loop over an approved corpus and produce
a cited synthesis without crossing the knowledge boundary.

> **Goal:** Run an agentic research loop in which a reasoning model plans, searches an
> approved knowledge source, iterates, and returns a cited synthesis.
>
> **You'll use:** `RESEARCH_MODEL` through the Responses API with `search` and `fetch`
> function tools, plus `CHAT_MODEL` to write the final report.

## How the research loop works

A normal chat answer is one shot. Deep research is different: a reasoning model decides
what to search, reads the documents it fetches, searches again to fill gaps, and concludes
only when it has enough evidence. The C# host executes every tool call against the bounded
corpus and returns the result to the same response chain. A second, cheaper chat model
turns the findings into a clean, cited report.

```text
question -> RESEARCH_MODEL ----> search(query) ----+
              ^                  fetch(doc-id)      |  repeat while tools are requested
              +---- tool results <-----------------+
                                   |
                                   v
                         CHAT_MODEL writes a cited report
```

!!! note "One project, two model roles"
    The C# lab uses one project endpoint for both deployments. `RESEARCH_MODEL` performs
    the slower planning and tool calling; `CHAT_MODEL` performs the faster final synthesis.
    The research loop stops when the model requests no more tools or reaches the six-
    iteration cap. Deep-research model and tool support varies by deployment and region.

## Prerequisites

- `PROJECT_ENDPOINT`
- `RESEARCH_MODEL` set to a deployment that supports function tools; defaults to
  `o3-deep-research`
- `CHAT_MODEL` set to a chat deployment for final synthesis

## Run

```powershell
dotnet run --project .\labs\08-deep-research -- --check
dotnet run --project .\labs\08-deep-research
```

Source: [`labs/08-deep-research/Program.cs`](https://github.com/kanhaiyasingh/foundry-workshop/blob/main/labs/08-deep-research/Program.cs)

## Code flow

1. Load the research and synthesis deployments and print their roles.
2. Define a four-document paper corpus.
3. Expose `search` and `fetch` function schemas.
4. Let the research model choose calls, execute them in C#, and return results with
   `previous_response_id`.
5. Stop when the research model returns no tool call or reaches the iteration safety limit.
6. Pass the findings to `CHAT_MODEL` and preserve every `[doc-id]` citation.
7. Run an out-of-scope fusion-energy question to verify the knowledge boundary.

## Expected output

```text
Project   : https://<account>.services.ai.azure.com/api/projects/<project>
Research  : <RESEARCH_MODEL deployment>
Synthesis : <CHAT_MODEL deployment>
Iteration 1
   search("<query>") -> <n> hit(s)
Iteration 2
   fetch("<doc-id>")
...
Iterations : <model-dependent count>
Tool calls : [search, fetch, ...]
<comparison citing [doc-001], [doc-002], and [doc-003]>
```

The tool sequence is model-dependent; the six-iteration stop condition is deterministic.
The lab then prints a second research result explaining that the corpus cannot support the
fusion-energy question.

## Your Turn

1. **Add a document.** Add `doc-005` about cross-lingual transfer to `corpus`, then ask a
   multilingual-NLP question. Confirm the loop searches, fetches, and cites `[doc-005]`.
2. **Watch it iterate.** Ask, "Contrast few-shot metric methods with efficient attention."
   Inspect the printed `search(...)`, `fetch(...)`, and final `Tool calls : [...]` lines;
   you should see multiple search/fetch rounds.
3. **Tune the cap.** Lower `maxIterations` to `1` and observe the loop stop early instead
   of producing a complete report. Raise it again and watch the model dig deeper. This is
   the cost/quality dial.

## Cleanup and cost

No resources persist. Reasoning models and repeated tool-loop calls can consume more tokens
than a single response. The separate chat deployment keeps report writing on the cheaper
model; keep the iteration cap and inspect usage.

## Parity and preview caveats

The loop uses the Responses REST tool shape. The specialized deep-research model catalog
and supported tools vary by region.
