# M9 - Evaluation

## Objective

Build the notebook's complete evaluation flow in C#: create the same four-row JSONL
test set, spot-check quality and agent behavior, apply a deterministic custom rule,
run a row-level batch, aggregate metrics, and optionally publish a Foundry evaluation
run for portal inspection.

## Prerequisites

- .NET 10
- `PROJECT_ENDPOINT` in the repository `.env`
- `CHAT_MODEL` for LLM judging, defaulting exactly to `gpt-4.1-mini`
- Foundry User access and `az login`

The account endpoint used by Python's `azure-ai-evaluation` package is derived from
`PROJECT_ENDPOINT` by removing `/api/projects/<project>`. The C# adaptation displays
that endpoint but uses the project's Responses and Evals REST surfaces because there
is no stable C# facade matching Python `azure-ai-evaluation` 1.16.x.

## Run

```powershell
dotnet run --project .\labs\09-evaluation -- --check
dotnet run --project .\labs\09-evaluation
```

The run uses the configured judge with strict JSON-schema output, then submits the
same dataset to Foundry Evals REST and polls for the portal report URL.

Source: [`labs/09-evaluation/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/09-evaluation/Program.cs)

## Flow restored from the notebook

1. Print the current date/time and load `PROJECT_ENDPOINT` plus the
   `gpt-4.1-mini` judge default.
2. Derive and display the OpenAI-style account endpoint and initialize keyless
   authentication.
3. Write the exact four evaluation cases to `eval_test_data.jsonl`; row 3 retains
   the deliberate `1536` versus `3072` contradiction.
4. Spot-check relevance and groundedness on the quality path.
5. Evaluate the captured agent turn for intent resolution, task adherence, and
   tool-call accuracy with the original `kb_search` call and schema.
6. Apply `KeyTermCoverageEvaluator(minLength: 4, threshold: 0.5)`.
7. Batch relevance, groundedness, coherence, and key-term coverage across
   every row; write reasons and scores to `eval_results.jsonl`; print aggregate
   metrics.
8. Create and run the equivalent project-scoped Foundry evaluation with relevance,
   groundedness, and coherence evaluators, then print its report URL. The C# custom
   delegate remains in the local artifact because a REST evaluation cannot serialize
   a C# callable.

## Expected output

The run preserves the notebook contrast:

```text
GOOD row
  relevance    : {'relevance': 5.0, 'relevance_result': 'pass', ...}
  groundedness : {'groundedness': 5.0, 'groundedness_result': 'pass', ...}

BAD row (wrong dimension)
  groundedness : {'groundedness': 2.0, 'groundedness_result': 'fail', ...}

good row: {'key_term_coverage': 0.8, 'key_term_pass': True}
bad  row: {'key_term_coverage': 0.75, 'key_term_pass': True}
Aggregate metrics:
  ...
Portal: ...
Results: ...\eval_results.jsonl
```

Judge scores vary. The important regression signal is that the wrong embedding
answer scores lower on groundedness. The notebook's expected `0.43` custom score is
stale: its own key-term algorithm produces `0.75`, so the default `0.5` threshold
still passes that row.

## Your Turn

1. **Break a good row.** Edit row 1's `response` in `records` to contradict its
   `context`, then rerun. The program rewrites the JSONL before the batch; watch
   groundedness drop and the row flip to fail.
2. **Add Fluency + Similarity.** Add fluency and similarity evaluator prompts to
   the batch. Similarity also needs `ground_truth` in its mapping; compare the new
   JSONL columns.
3. **Tighten your custom rule.** Construct
   `KeyTermCoverageEvaluator(threshold: 0.8)` and rerun. More rows should fail,
   turning a soft expectation into an enforceable gate.

## Artifacts, cleanup, and cost

- `eval_test_data.jsonl` is the exact four-row source dataset.
- `eval_results.jsonl` contains all local row-level scores and judge reasons.
- The run creates persistent evaluation/run resources visible in the Foundry portal
  and consumes judge-model and evaluation-service tokens.

Delete the two local JSONL files when they are no longer needed. Portal evaluation
runs remain project artifacts; retain them for trend comparison or remove them
through normal project governance.

## Next

M10 applies the same signals to live traffic with tracing and continuous
evaluation: [M10 - Observability & Tracing](10-observability.md).
