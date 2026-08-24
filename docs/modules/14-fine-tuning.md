# M14 - Fine-tuning and distillation

## Objective

Create and validate SFT distillation data, compare an illustrative teacher/base/student
benchmark offline, and optionally upload files plus submit/monitor a Foundry fine-tuning
job through C# REST.

## Prerequisites

Offline mode needs only .NET. Submission needs:

- `PROJECT_ENDPOINT`
- `FINE_TUNE_MODEL` set to a supported model/version
- Fine-tuning quota and regional availability
- Permission to upload files and create jobs
- Optional `FINE_TUNE_TRAINING_TYPE` (default `globalStandard`)

## Run

```powershell
dotnet run --project .\labs\14-fine-tuning
dotnet run --project .\labs\14-fine-tuning -- --submit
dotnet run --project .\labs\14-fine-tuning -- --submit --monitor
```

Source: [`labs/14-fine-tuning/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/14-fine-tuning/Program.cs)

## Code flow

1. Generate balanced incident-severity chat examples.
2. Split train/validation JSONL.
3. Validate JSON, role/content presence, user/assistant coverage, and assistant-final order.
4. Print the bundled comparison clearly labelled as precomputed/illustrative.
5. With `--submit`, upload both files using multipart REST and create an SFT job.
6. With `--monitor`, poll until the service reports a terminal job status.

## Expected output

```text
Validated SFT data: 10 training rows, 2 validation rows.
Local comparison (illustrative workshop benchmark, not a live measurement):
  Teacher ... 91%
  Small base ... 46%
  Fine-tuned student ... 72%
Data is ready at ...artifacts\m14
```

Submission prints uploaded file ids and a job id/status.

## Your Turn

1. **Grow the dataset.** Expand `BuildExamples()` with equal counts per severity and write
   a larger `training.jsonl`. Compare class counts so that one label is not over-predicted.
2. **Train longer.** Increase `hyperparameters.n_epochs`, resubmit, and re-evaluate. Does
   the gain widen, plateau, or overfit?
3. **Swap the student.** Set `FINE_TUNE_MODEL` to another supported small base model,
   resubmit, and compare held-out accuracy and service-reported model metadata. This
   Foundry REST path does not produce a local LoRA adapter size.

## Cleanup and cost

Fine-tuning jobs, uploaded files, and model deployments can incur material cost. Delete
files and deployments when no longer required. `artifacts/m14` is local and git-ignored.

## Parity and preview caveats

The Python workshop's local PyTorch/LoRA path is intentionally not imitated in C#.
This lab preserves the distillation outcome with data validation, a no-Azure comparison,
and real Foundry file/job REST using `2025-04-01-preview`. Supported models, training
types, and job payloads vary by region.
