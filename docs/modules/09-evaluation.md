# M9 - Evaluation

## Objective

Create repeatable quality checks that work offline, write row-level JSONL results, and
optionally add a real Foundry LLM-as-judge score.

## Prerequisites

Offline mode needs only the .NET build. `--cloud` additionally needs
`PROJECT_ENDPOINT`, `CHAT_MODEL`, and Foundry User access.

## Run

```powershell
dotnet run --project .\labs\09-evaluation
dotnet run --project .\labs\09-evaluation -- --cloud
```

Source: [`labs/09-evaluation/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/09-evaluation/Program.cs)

## Code flow

The dataset contains two correct answers and one deliberate factual error. C# evaluators
calculate expected-term coverage and lexical groundedness. Cloud mode requests strict
JSON from a judge model. Every result is written to
`artifacts/m09-evaluation-results.jsonl`.

## Expected output

```text
PASS coverage=... grounded=... - What does DefaultAzureCredential do?
FAIL coverage=... grounded=... - How large are ... vectors?
Passed 2/3. Results: ...m09-evaluation-results.jsonl
```

Cloud scores vary; the wrong dimension should remain the weak row.

## Your Turn

1. **Break a good row.** Change the first `EvalRecord.Response` so that it contradicts
   `Context`, rerun, and watch `lexical_groundedness` drop and the row flip to `FAIL`.
2. **Add fluency and similarity.** Implement `Fluency` and `Similarity` C# evaluators
   beside `Coverage` and `Grounded`. Add `GroundTruth` to `EvalRecord` for similarity and
   write both new scores to the JSONL output.
3. **Tighten your custom rule.** Raise the coverage threshold in `rowPassed` from `0.5`
   to `0.8` and rerun. More rows should fail.

## Cleanup and cost

Delete `artifacts/` when results are no longer needed. Offline mode is free; cloud mode
uses judge-model tokens.

## Parity and preview caveats

There is no stable C# counterpart to the full Python `azure-ai-evaluation` package.
This lab uses real deterministic C# evaluators plus a runnable Responses-based judge,
not placeholder evaluator calls.
