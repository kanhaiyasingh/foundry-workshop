# M14 - Fine-Tuning & Distillation

> **Goal:** shrink a big model into a small, cheap one that mimics it. Use a
> `gpt-4.1-mini` **teacher** to generate training data, then **LoRA-fine-tune** a
> small **student** (Phi-4-mini) with Olive/PEFT, and compare teacher vs. base vs.
> fine-tuned accuracy.
>
> **You'll use:** C# REST calls for distillation and optional Microsoft Foundry
> file/job operations, plus the notebook's illustrative Olive/PEFT recipe.

Big models are accurate but expensive; small models are cheap but generic.
**Knowledge distillation** gets you both: a strong **teacher** labels training
data, and a small **student** learns to imitate it on your narrow task.

```text
teacher (gpt-4.1-mini) --generates labelled data--> train.jsonl
                                                        |
                          Olive LoRA fine-tune (GPU) <--+
                                  |
                          LoRA adapter --> evaluate: teacher vs base vs student
                                  |
                          load locally with PEFT --> offline inference
```

> [!WARNING]
> The full notebook pipeline needs heavy Python ML dependencies and a GPU.
> This C# port prints the exact Olive recipe but never executes it. `--distill`,
> `--submit`, and `--infer` are separate explicit gates for billable REST calls.
> The default run is local and safe.

Source: [`labs/14-fine-tuning/Program.cs`](https://github.com/kanhaiyasingh/foundry-workshop/blob/main/labs/14-fine-tuning/Program.cs)

Like the notebook's first code cell, the program timestamps the run:

```csharp
Console.WriteLine(
    $"Current date and time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}");
```

Expected output:

```text
Current date and time: <local date and time>
```

## Run and safety flags

```powershell
# Inspect optional configuration; no Azure calls
dotnet run --project .\labs\14-fine-tuning -- --check

# Safe: write and validate the two exact demo records; no Azure calls or training
dotnet run --project .\labs\14-fine-tuning

# Billable: two teacher calls per scenario
dotnet run --project .\labs\14-fine-tuning -- --distill

# Costly: upload data and create a fine-tuning job
dotnet run --project .\labs\14-fine-tuning -- --submit
dotnet run --project .\labs\14-fine-tuning -- --submit --monitor

# Read or monitor an existing job; does not create another job
dotnet run --project .\labs\14-fine-tuning -- --status <job-id>
dotnet run --project .\labs\14-fine-tuning -- --status <job-id> --monitor

# Billable inference against an already deployed fine-tuned model
dotnet run --project .\labs\14-fine-tuning -- --infer
```

The configuration defaults and gates are:

| Variable | Default | Used by |
|---|---|---|
| `PROJECT_ENDPOINT` | none | All REST paths |
| `CHAT_MODEL` | `gpt-4.1-mini` | Teacher for `--distill` |
| `STUDENT_MODEL` | `microsoft/Phi-4-mini-instruct` | Printed Olive recipe |
| `FINE_TUNE_MODEL` | none | Base model/version for `--submit` |
| `FINE_TUNE_TRAINING_TYPE` | `globalStandard` | `--submit` |
| `FINE_TUNED_MODEL` | none | Deployed model used by `--infer` |

## 1. Configure

The teacher is this project's `CHAT_MODEL`; the student is a small open-weights
model. Open models matter because their licenses determine whether training
derivative models and local distribution are permitted.

```csharp
var teacherModel = context.Config.Get("CHAT_MODEL", "gpt-4.1-mini");
var studentModel = context.Config.Get(
    "STUDENT_MODEL",
    "microsoft/Phi-4-mini-instruct");
```

Expected output:

```text
Teacher : gpt-4.1-mini (labels the data)
Student : microsoft/Phi-4-mini-instruct (gets fine-tuned)
Train   : finetune_data/train.jsonl
```

Only the teacher is called over the API. The student id identifies the local
model that Olive and PEFT download.

## 2. The task: a domain classifier

Distillation needs a **narrow, well-defined task**. This one reads an ISS daily
status report and classifies its severity:

1. `CRITICAL` - immediate threat to crew safety or vehicle integrity.
2. `WARNING` - loss of a critical system function or redundancy.
3. `CAUTION` - degraded component performance or localized failure.
4. `ADVISORY` - minor off-nominal condition, no impact.
5. `NOMINAL` - normal operations.

`CreateClassificationPrompt` returns the same system rubric and user report for
the teacher and student. It requires:

```text
SEVERITY: <level>
REASON: <one sentence>
```

Expected output:

```text
You are an expert ISS Flight Controller. Classify the daily station status report ...

USER: Classify this report:

Coolant loop B pump showing degraded flow; crew swapped to backup. No ...
```

A deterministic format makes labeling, JSONL validation, and evaluation simple.

## 3. Distillation - the teacher generates training data

For each scenario, `MakeTrainingExampleAsync` performs the notebook's two passes:

1. Generate a realistic one-paragraph report with temperature `0.8` and at most
   400 tokens.
2. Classify that report with temperature `0.1` and at most 120 tokens.

The billable loop runs only with `--distill`. Without that flag, the lab writes
the notebook's exact two hand-labeled records:

```json
{"system":"You are an expert ISS Flight Controller. Classify the daily station status report into exactly one severity level.\n\nSEVERITY (highest to lowest):\n1. CRITICAL  - immediate threat to crew safety or vehicle integrity.\n2. WARNING   - loss of a critical system function or redundancy.\n3. CAUTION   - degraded component performance or localized failure.\n4. ADVISORY  - minor off-nominal condition, no impact.\n5. NOMINAL   - normal operations.\n\nRespond in the format:\nSEVERITY: <level>\nREASON: <one sentence>","user":"Classify this report:\n\nNominal ops; all systems green; routine filter swap completed.","assistant":"SEVERITY: NOMINAL\nREASON: Routine maintenance with all systems nominal."}
{"system":"You are an expert ISS Flight Controller. Classify the daily station status report into exactly one severity level.\n\nSEVERITY (highest to lowest):\n1. CRITICAL  - immediate threat to crew safety or vehicle integrity.\n2. WARNING   - loss of a critical system function or redundancy.\n3. CAUTION   - degraded component performance or localized failure.\n4. ADVISORY  - minor off-nominal condition, no impact.\n5. NOMINAL   - normal operations.\n\nRespond in the format:\nSEVERITY: <level>\nREASON: <one sentence>","user":"Classify this report:\n\nExternal ammonia coolant leak on loop A; isolated; redundancy lost.","assistant":"SEVERITY: WARNING\nREASON: Loss of cooling redundancy from an external coolant leak."}
```

Expected output:

```text
Wrote 2 example rows -> finetune_data/train.jsonl
Real distillation: loop MakeTrainingExampleAsync over 500+ scenarios.
Validated JSONL: 2 rows, 6 messages, ... content characters.
Severity counts: NOMINAL=1, WARNING=1
Azure SFT conversion: 2 rows, 6 messages; every row ends with assistant.
```

`train.jsonl` intentionally preserves the notebook/Olive top-level
`{system,user,assistant}` shape. The program also creates
`train.azure-sft.jsonl`, converting each exact record to Microsoft's chat-SFT
`messages` shape:

```json
{"messages":[{"role":"system","content":"..."},{"role":"user","content":"..."},{"role":"assistant","content":"..."}]}
```

Both files are parsed line by line. Validation rejects invalid JSON, missing or
empty fields, invalid severity labels, the wrong role order, or rows that do not
end in `assistant`. The printed severity counts make class imbalance visible.
The reference uses **500+ balanced rows**; two rows demonstrate format only and
are not a production-quality training set.

## 4. LoRA fine-tune with Olive

LoRA freezes the base model and trains small adapter matrices over selected
attention/MLP projections. The safe run prints, but never executes:

```text
olive finetune --method lora --model_name_or_path microsoft/Phi-4-mini-instruct \
  --trust_remote_code --data_name json --data_files finetune_data/train.jsonl \
  --text_template "{system}\n{user}\n{assistant}" \
  --target_modules qkv_proj,o_proj,gate_up_proj,down_proj \
  --max_steps 100 --output_path finetune_data/adapter

(Not executed here - see the GPU warning in the guide.)
```

Olive writes a small **LoRA adapter** (typically tens of MB), not a full model
copy. The reference A100 run takes about 15-20 minutes for 100 steps. Olive
flags and PEFT/Transformers APIs evolve; the source notebook pins
`transformers==4.53.3`.

The reference runs this command as a serverless A100 job. Provisioning, image,
and blob orchestration are platform concerns, not part of the fine-tuning cell.

### Optional Microsoft Foundry REST submission

The C# adaptation can upload `train.azure-sft.jsonl` and submit a supervised
fine-tuning job, but **only** when the invocation includes `--submit`.

The upload uses `purpose=fine-tune` and
`openai/files?api-version=2025-04-01-preview`. Submission uses
`openai/fine_tuning/jobs?api-version=2025-04-01-preview`, two epochs, learning
rate multiplier `1.0`, the configured training type, and suffix
`csharp-workshop-m14`.

Expected service-dependent output:

```text
WARNING: --submit uploads data and creates a billable fine-tuning job.
Uploaded training file: <file-id>
Submitted job <job-id>: <service-status>
Check later with --status <job-id>, or add --monitor to a submission invocation.
```

`--monitor` polls every 15 seconds until `succeeded`, `failed`, or `cancelled`
and then prints the final service payload. `--status <job-id>` performs one
read unless combined with `--monitor`. IDs, timestamps, duration, error details,
and final payloads vary by resource.

> [!CAUTION]
> The two demo rows are for format inspection, not useful model training.
> Build a supported, adequately sized, balanced dataset and verify regional
> model support before deliberately running `--submit`.

## 5. Evaluate: teacher vs. base vs. student

Use the same held-out reports for all three models. The win condition is the
fine-tuned student beating its base self. The lab prints the notebook's
**illustrative precomputed values**, not live measurements:

```text
model                        accuracy
-------------------------------------
gpt-4.1-mini (teacher)          80.0%  ████████████████
Phi-4-mini (base)               45.7%  █████████
Phi-4-mini (fine-tuned)         51.4%  ██████████

Fine-tuning gain: +5.7%  (base 45.7% -> fine-tuned 51.4%)
```

The student gains about six points and closes part of the teacher gap. It does
not need to match the teacher; the target is good enough, cheap, and local.
Measure held-out accuracy and token/inference cost with your own run before
claiming an improvement.

## 6. Load the adapter for local inference

The notebook loads the public Phi-4-mini weights and applies the adapter with
PEFT, then performs deterministic local generation. Its expected output is:

```text
SEVERITY: CRITICAL
REASON: Rapid cabin depressurization is an immediate threat to crew safety.
```

This repository intentionally adds no multi-gigabyte Torch/Transformers/PEFT
stack to the .NET lab. The default path therefore documents the local PEFT
boundary rather than pretending that Azure REST is offline. As a C#/REST
adaptation, `--infer` sends the same system/user prompt to an already deployed
fine-tuned model named by `FINE_TUNED_MODEL`; that call is billable and online.

## Costs, permissions, and cleanup

- `--distill` incurs two teacher calls per scenario.
- `--submit` uploads a file and creates a potentially expensive training job.
- `--infer` invokes a deployed model.
- Fine-tuning requires a supported model/version, available regional capacity,
  quota, and permissions to upload files and create/read jobs.
- Open-model fine-tuning commonly requires
  `FINE_TUNE_TRAINING_TYPE=globalStandard`.
- Training jobs, uploaded files, GPU compute, and model deployments can all
  incur material cost.
- Delete unneeded uploaded files, jobs where supported, adapters, and model
  deployments using your organization's approved process. Remove local
  `finetune_data` artifacts when no longer needed.
- Confirm `PROJECT_ENDPOINT` points to the intended resource before submission;
  jobs sent to another resource will not appear in the expected portal project.

## Your Turn

1. **Grow the dataset.** Loop `MakeTrainingExampleAsync` over a longer,
   **balanced** scenario list with equal counts per severity. The reference
   notes that an imbalanced student over-predicts `CAUTION`; use the printed
   statistics to catch that.
2. **Train longer.** Increase Olive `--max_steps` to `200-300` and re-evaluate.
   Does the gain widen, plateau, or overfit?
3. **Swap the student.** Point `STUDENT_MODEL` at another small open model and
   repeat data preparation, fine-tuning, and evaluation. Compare held-out
   accuracy and adapter size with Phi-4-mini.

You walked the complete distillation pipeline: teacher-generated labeled data,
a LoRA recipe for the student, teacher/base/student evaluation, and the local
adapter inference boundary. Next: [M15 - Capstone](15-capstone.md).
