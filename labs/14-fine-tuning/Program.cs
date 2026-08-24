// M14 - Fine-Tuning & Distillation
//
// Goal: use a gpt-4.1-mini teacher to generate task-specific training data, prepare a
// small Phi-4-mini student for LoRA fine-tuning, and compare teacher, base, and
// fine-tuned accuracy.
//
// Cell 0 [markdown]
// Big models are accurate but expensive; small models are cheap but generic.
// Distillation uses the teacher to label a narrow dataset, trains a small LoRA
// adapter for the student, compares teacher/base/fine-tuned accuracy, and then loads
// the public base weights plus adapter for offline inference.
//
// The notebook's full Olive/PEFT path needs torch, transformers<5, peft, olive-ai,
// and a CUDA GPU (the reference uses a serverless A100). This .NET lab preserves the
// pipeline and commands, but the safe path never downloads those multi-GB
// dependencies or executes GPU training.
// Full guide: docs/modules/14-fine-tuning.md
// Check:       dotnet run --project .\labs\14-fine-tuning -- --check
// Safe run:    dotnet run --project .\labs\14-fine-tuning
// Distill:     dotnet run --project .\labs\14-fine-tuning -- --distill
// Submit:      dotnet run --project .\labs\14-fine-tuning -- --submit [--monitor]
// Job status:  dotnet run --project .\labs\14-fine-tuning -- --status <job-id> [--monitor]
// Inference:   dotnet run --project .\labs\14-fine-tuning -- --infer
//
// The safe path makes no Azure calls and never starts Olive or a fine-tuning job.

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FoundryWorkshop.Shared;

// Cell 1 [code]: print the current date and time.
Console.WriteLine($"Current date and time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}");

if (args.Any(arg => arg.Equals("--check", StringComparison.OrdinalIgnoreCase)))
{
    using var checkContext = new WorkshopContext("M14 - Fine-tuning and distillation", args);
    Console.WriteLine("Configuration check only; no Azure calls were made.");
    PrintSetting(checkContext.Config, "PROJECT_ENDPOINT", "required by --distill, --submit, --status, and --infer");
    PrintSetting(checkContext.Config, "CHAT_MODEL", "teacher; defaults to gpt-4.1-mini");
    PrintSetting(checkContext.Config, "STUDENT_MODEL", "student; defaults to microsoft/Phi-4-mini-instruct");
    PrintSetting(checkContext.Config, "FINE_TUNE_MODEL", "required by --submit");
    PrintSetting(checkContext.Config, "FINE_TUNE_TRAINING_TYPE", "defaults to globalStandard");
    PrintSetting(checkContext.Config, "FINE_TUNED_MODEL", "required by --infer");
    return 0;
}

return await LabHost.RunAsync(
    "M14 - Fine-tuning and distillation",
    args,
    async context =>
    {
        // Cell 2 [markdown]
        // ## 1. Configure
        //
        // The teacher is CHAT_MODEL. The student is the open-weights model that the
        // notebook fine-tunes with Olive/PEFT. Open-model licensing determines whether
        // derivative training and local distribution are permitted.

        // Cell 3 [code]
        var teacherModel = context.Config.Get("CHAT_MODEL", "gpt-4.1-mini");
        var studentModel = context.Config.Get("STUDENT_MODEL", "microsoft/Phi-4-mini-instruct");
        var dataDirectory = Path.Combine(Environment.CurrentDirectory, "finetune_data");
        Directory.CreateDirectory(dataDirectory);
        var trainFile = Path.Combine(dataDirectory, "train.jsonl");
        var azureSftFile = Path.Combine(dataDirectory, "train.azure-sft.jsonl");

        Console.WriteLine($"Teacher : {teacherModel} (labels the data)");
        Console.WriteLine($"Student : {studentModel} (gets fine-tuned)");
        Console.WriteLine($"Train   : {Path.GetRelativePath(Environment.CurrentDirectory, trainFile).Replace('\\', '/')}");

        // Cell 4 [markdown]
        // Expected output:
        //   Teacher : gpt-4.1-mini (labels the data)
        //   Student : microsoft/Phi-4-mini-instruct (gets fine-tuned)
        //   Train   : finetune_data/train.jsonl
        //
        // Only the teacher is called over the API. The student id identifies the
        // open-weights model used by Olive and PEFT.

        // Cell 5 [markdown]
        // ## 2. The task: a domain classifier
        //
        // Distillation needs a narrow, well-defined task. A shared rubric is used for
        // teacher labels and student prompts, and the deterministic SEVERITY/REASON
        // format makes labeling and scoring straightforward.

        // Cell 6 [code]
        var example = CreateClassificationPrompt(
            "Coolant loop B pump showing degraded flow; crew swapped to backup. No crew impact.");
        Console.WriteLine($"\n{example.System[..120]} ...");
        Console.WriteLine($"\nUSER: {example.User[..90]} ...");

        // Cell 7 [markdown]
        // Expected output:
        //   You are an expert ISS Flight Controller. Classify the daily station status report ...
        //
        //   USER: Classify this report:
        //
        //   Coolant loop B pump showing degraded flow; crew swapped to backup. No ...

        // Cell 8 [markdown]
        // ## 3. Distillation - the teacher generates training data
        //
        // --distill explicitly opts into two billable teacher calls per scenario:
        // a creative report pass, then a deterministic classification pass. Each
        // {system,user,assistant} triple is a synthetic training example in the exact
        // format the student must learn.

        // Cell 9 [code]
        TrainingRow[] rows;
        if (context.HasFlag("--distill"))
        {
            context.Config.Require("PROJECT_ENDPOINT");
            rows = new TrainingRow[LabData.Scenarios.Length];
            for (var index = 0; index < LabData.Scenarios.Length; index++)
            {
                rows[index] = await MakeTrainingExampleAsync(
                    context,
                    teacherModel,
                    LabData.Scenarios[index]);
                Console.WriteLine(
                    $"Distilled {index + 1}/{LabData.Scenarios.Length}: {LabData.Scenarios[index]}");
            }
        }
        else
        {
            rows =
            [
                CreateTrainingRow(
                    "Nominal ops; all systems green; routine filter swap completed.",
                    "SEVERITY: NOMINAL\nREASON: Routine maintenance with all systems nominal."),
                CreateTrainingRow(
                    "External ammonia coolant leak on loop A; isolated; redundancy lost.",
                    "SEVERITY: WARNING\nREASON: Loss of cooling redundancy from an external coolant leak.")
            ];
        }

        await WriteNotebookJsonlAsync(trainFile, rows);
        await WriteAzureSftJsonlAsync(azureSftFile, rows);
        Console.WriteLine($"\nWrote {rows.Length} example rows -> {Path.GetRelativePath(Environment.CurrentDirectory, trainFile).Replace('\\', '/')}");
        Console.WriteLine("Real distillation: loop MakeTrainingExampleAsync over 500+ scenarios.");

        // Cell 10 [markdown]
        // Expected output for the safe path:
        //   Wrote 2 example rows -> finetune_data/train.jsonl
        //   Real distillation: loop MakeTrainingExampleAsync over 500+ scenarios.
        //
        // train.jsonl preserves the notebook's exact {system,user,assistant} records.
        // train.azure-sft.jsonl is the C#/REST adaptation with a messages array.

        var notebookStats = ValidateNotebookJsonl(trainFile);
        var azureStats = ValidateAzureSftJsonl(azureSftFile);
        Console.WriteLine(
            $"Validated JSONL: {notebookStats.Rows} rows, {notebookStats.Messages} messages, " +
            $"{notebookStats.Characters} content characters.");
        Console.WriteLine(
            "Severity counts: " +
            string.Join(", ", notebookStats.SeverityCounts.Select(item => $"{item.Key}={item.Value}")));
        Console.WriteLine(
            $"Azure SFT conversion: {azureStats.Rows} rows, {azureStats.Messages} messages; " +
            "every row ends with assistant.");

        // Cell 11 [markdown]
        // ## 4. LoRA fine-tune with Olive
        //
        // LoRA freezes the base model and trains small adapter matrices over selected
        // attention/MLP projections rather than retraining billions of weights.

        // Cell 12 [code]
        // GPU REQUIRED. This cell only prints the notebook's command. It does not
        // execute Olive, download the model, allocate a GPU, or start training.
        var oliveCommand = string.Join(
            " ",
            "olive finetune",
            "--method lora",
            $"--model_name_or_path {studentModel}",
            "--trust_remote_code",
            "--data_name json",
            $"--data_files {Path.GetRelativePath(Environment.CurrentDirectory, trainFile).Replace('\\', '/')}",
            "--text_template \"{system}\\n{user}\\n{assistant}\"",
            "--target_modules qkv_proj,o_proj,gate_up_proj,down_proj",
            "--max_steps 100",
            "--output_path finetune_data/adapter");
        Console.WriteLine($"\nFine-tune command (run on a GPU host):\n\n  {oliveCommand}");
        Console.WriteLine("\n(Not executed here - see the GPU warning in the guide.)");

        // Cell 13 [markdown]
        // Expected output:
        //   Fine-tune command (run on a GPU host):
        //
        //     olive finetune --method lora --model_name_or_path microsoft/Phi-4-mini-instruct ...
        //
        //   (Not executed here - see the GPU warning in the guide.)
        //
        // Olive writes a LoRA adapter, typically tens of MB. The reference A100 run of
        // 100 steps takes about 15-20 minutes. Provisioning the serverless GPU,
        // container image, and blob transfer is platform orchestration outside this
        // fine-tuning cell.

        // C#/REST adaptation: upload the converted chat-SFT file and submit only after the
        // current invocation explicitly includes --submit.
        if (context.HasFlag("--submit"))
        {
            context.Config.Require("PROJECT_ENDPOINT");
            var fineTuneModel = context.Config.Require(
                "FINE_TUNE_MODEL",
                "Choose a fine-tunable model/version supported in this region.");
            Console.WriteLine(
                "\nWARNING: --submit uploads data and creates a billable fine-tuning job.");
            var fileId = await UploadFileAsync(context, azureSftFile);
            Console.WriteLine($"Uploaded training file: {fileId}");
            var job = await SubmitJobAsync(context, fineTuneModel, fileId);
            Console.WriteLine($"Submitted job {job.Id}: {job.Status}");

            if (context.HasFlag("--monitor"))
            {
                await MonitorJobAsync(context, job.Id);
            }
            else
            {
                Console.WriteLine(
                    $"Check later with --status {job.Id}, or add --monitor to a submission invocation.");
            }
        }
        else if (TryGetOption(args, "--status", out var requestedJobId))
        {
            context.Config.Require("PROJECT_ENDPOINT");
            if (context.HasFlag("--monitor"))
            {
                await MonitorJobAsync(context, requestedJobId);
            }
            else
            {
                var status = await GetJobAsync(context, requestedJobId);
                Console.WriteLine($"Job {requestedJobId}: {GetRequiredString(status.RootElement, "status")}");
            }
        }
        else
        {
            Console.WriteLine("\nNo fine-tuning job submitted. Add --submit only when you intend to incur training cost.");
        }

        // Cell 14 [markdown]
        // ## 5. Evaluate: teacher vs. base vs. student
        //
        // Evaluate all three roles on the same held-out reports. The win condition is
        // the fine-tuned student beating its base self.

        // Cell 15 [code]
        // These are the notebook's illustrative reference measurements, not values
        // produced by this run.
        var results = new[]
        {
            new BenchmarkResult("gpt-4.1-mini (teacher)", 0.80),
            new BenchmarkResult("Phi-4-mini (base)", 0.457),
            new BenchmarkResult("Phi-4-mini (fine-tuned)", 0.514)
        };
        Console.WriteLine($"\n{"model",-28}{"accuracy",9}");
        Console.WriteLine(new string('-', 37));
        foreach (var result in results)
        {
            var bar = new string('█', (int)Math.Round(result.Accuracy * 20));
            Console.WriteLine(
                $"{result.Model,-28}{FormatPercent(result.Accuracy),8}  {bar}");
        }

        var gain = results[2].Accuracy - results[1].Accuracy;
        Console.WriteLine(
            $"\nFine-tuning gain: {gain.ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture)}  " +
            $"(base {FormatPercent(results[1].Accuracy)} -> " +
            $"fine-tuned {FormatPercent(results[2].Accuracy)})");

        // Cell 16 [markdown]
        // Expected output:
        //   model                        accuracy
        //   -------------------------------------
        //   gpt-4.1-mini (teacher)          80.0%  ████████████████
        //   Phi-4-mini (base)               45.7%  █████████
        //   Phi-4-mini (fine-tuned)         51.4%  ██████████
        //
        //   Fine-tuning gain: +5.7%  (base 45.7% -> fine-tuned 51.4%)

        // Cell 17 [markdown]
        // ## 6. Load the adapter for local inference
        //
        // The notebook's payoff is offline inference: load the public base weights,
        // apply the shipped LoRA adapter, and classify without an API call.

        // Cell 18 [code]
        // PEFT has no equivalent in the dependencies of this .NET workshop. The notebook's
        // local path loads the public base weights and adapter with PeftModel. This port
        // preserves the expected prompt and offers an explicit REST adaptation only when
        // --infer names a deployed fine-tuned model through FINE_TUNED_MODEL.
        if (context.HasFlag("--infer"))
        {
            context.Config.Require("PROJECT_ENDPOINT");
            var fineTunedModel = context.Config.Require(
                "FINE_TUNED_MODEL",
                "Set this to the deployment name of the fine-tuned model.");
            var prompt = CreateClassificationPrompt(
                "Cabin pressure dropping rapidly; crew donned masks; leak unisolated.");
            var output = await CompleteChatAsync(
                context,
                fineTunedModel,
                [
                    new("system", prompt.System),
                    new("user", prompt.User)
                ],
                temperature: 0.0,
                maxTokens: 80);
            Console.WriteLine($"\nFine-tuned deployment inference:\n{output}");
        }
        else
        {
            Console.WriteLine(
                "\nLocal adapter inference is not executed. The notebook's PEFT path expects:\n" +
                "SEVERITY: CRITICAL\n" +
                "REASON: Rapid cabin depressurization is an immediate threat to crew safety.");
        }

        // Cell 19 [markdown]
        // Expected notebook output:
        //   SEVERITY: CRITICAL
        //   REASON: Rapid cabin depressurization is an immediate threat to crew safety.
        //
        // Olive and PEFT/Transformers APIs evolve; the notebook pins
        // transformers==4.53.3 and advises re-checking flags across versions.
        //
        // Cell 20 [markdown]
        // Your Turn:
        // 1. Grow the balanced scenario set to 500+ rows and check the printed class counts.
        // 2. Increase Olive --max_steps to 200-300 and measure plateauing or overfitting.
        // 3. Change STUDENT_MODEL, repeat fine-tuning/evaluation, and compare adapter size.
    });

static ClassificationPrompt CreateClassificationPrompt(string reportText)
{
    const string system =
        "You are an expert ISS Flight Controller. Classify the daily station status " +
        "report into exactly one severity level.\n\n" +
        "SEVERITY (highest to lowest):\n" +
        "1. CRITICAL  - immediate threat to crew safety or vehicle integrity.\n" +
        "2. WARNING   - loss of a critical system function or redundancy.\n" +
        "3. CAUTION   - degraded component performance or localized failure.\n" +
        "4. ADVISORY  - minor off-nominal condition, no impact.\n" +
        "5. NOMINAL   - normal operations.\n\n" +
        "Respond in the format:\nSEVERITY: <level>\nREASON: <one sentence>";
    return new(system, $"Classify this report:\n\n{reportText}");
}

static TrainingRow CreateTrainingRow(string report, string assistant)
{
    var prompt = CreateClassificationPrompt(report);
    return new(prompt.System, prompt.User, assistant);
}

static async Task<TrainingRow> MakeTrainingExampleAsync(
    WorkshopContext context,
    string teacherModel,
    string scenario)
{
    var report = await CompleteChatAsync(
        context,
        teacherModel,
        [
            new("system", "Write a realistic 1-paragraph ISS daily status report."),
            new("user", $"Scenario: {scenario}")
        ],
        temperature: 0.8,
        maxTokens: 400);
    var prompt = CreateClassificationPrompt(report);
    var label = await CompleteChatAsync(
        context,
        teacherModel,
        [
            new("system", prompt.System),
            new("user", prompt.User)
        ],
        temperature: 0.1,
        maxTokens: 120);
    return new(prompt.System, prompt.User, label);
}

static async Task<string> CompleteChatAsync(
    WorkshopContext context,
    string model,
    IReadOnlyList<ChatMessage> messages,
    double temperature,
    int maxTokens)
{
    var uri = new Uri(
        context.Config.AccountUri,
        $"openai/deployments/{Uri.EscapeDataString(model)}/chat/completions?api-version=2024-10-21");
    using var response = await context.Rest.SendJsonAsync(
        HttpMethod.Post,
        uri,
        new
        {
            messages,
            temperature,
            max_completion_tokens = maxTokens
        },
        FoundryRestClient.CognitiveServicesScope);
    return response.RootElement
               .GetProperty("choices")[0]
               .GetProperty("message")
               .GetProperty("content")
               .GetString()
               ?.Trim()
           ?? throw new InvalidDataException("Teacher response contained no message content.");
}

static async Task WriteNotebookJsonlAsync(string path, IEnumerable<TrainingRow> rows)
{
    await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
    foreach (var row in rows)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(row, LabData.JsonOptions));
    }
}

static async Task WriteAzureSftJsonlAsync(string path, IEnumerable<TrainingRow> rows)
{
    await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
    foreach (var row in rows)
    {
        var converted = new
        {
            messages = new[]
            {
                new ChatMessage("system", row.System),
                new ChatMessage("user", row.User),
                new ChatMessage("assistant", row.Assistant)
            }
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(converted, LabData.JsonOptions));
    }
}

static DatasetStats ValidateNotebookJsonl(string path)
{
    var rows = 0;
    var messages = 0;
    var characters = 0;
    var severityCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (var line in File.ReadLines(path))
    {
        rows++;
        using var document = ParseJsonLine(path, rows, line);
        var root = document.RootElement;
        foreach (var property in new[] { "system", "user", "assistant" })
        {
            var content = GetRequiredString(root, property);
            characters += content.Length;
            messages++;
        }

        var assistant = GetRequiredString(root, "assistant");
        var severityLine = assistant.Split('\n', 2)[0];
        const string prefix = "SEVERITY: ";
        var severity = severityLine.StartsWith(prefix, StringComparison.Ordinal)
            ? severityLine[prefix.Length..].TrimEnd()
            : string.Empty;
        if (!severityLine.StartsWith(prefix, StringComparison.Ordinal) ||
            !LabData.ValidSeverities.Contains(severity))
        {
            throw new InvalidDataException(
                $"{path} row {rows}: assistant must start with a valid SEVERITY label.");
        }

        severityCounts[severity] = severityCounts.GetValueOrDefault(severity) + 1;
    }

    if (rows == 0)
    {
        throw new InvalidDataException($"{path}: JSONL file is empty.");
    }

    return new(rows, messages, characters, severityCounts);
}

static DatasetStats ValidateAzureSftJsonl(string path)
{
    var rows = 0;
    var messages = 0;
    var characters = 0;
    foreach (var line in File.ReadLines(path))
    {
        rows++;
        using var document = ParseJsonLine(path, rows, line);
        if (!document.RootElement.TryGetProperty("messages", out var rowMessages) ||
            rowMessages.ValueKind != JsonValueKind.Array ||
            rowMessages.GetArrayLength() != 3)
        {
            throw new InvalidDataException($"{path} row {rows}: messages must contain exactly three turns.");
        }

        var expectedRoles = new[] { "system", "user", "assistant" };
        var index = 0;
        foreach (var message in rowMessages.EnumerateArray())
        {
            if (GetRequiredString(message, "role") != expectedRoles[index])
            {
                throw new InvalidDataException(
                    $"{path} row {rows}: expected role {expectedRoles[index]} at message {index + 1}.");
            }

            characters += GetRequiredString(message, "content").Length;
            messages++;
            index++;
        }
    }

    if (rows == 0)
    {
        throw new InvalidDataException($"{path}: JSONL file is empty.");
    }

    return new(rows, messages, characters, new SortedDictionary<string, int>());
}

static JsonDocument ParseJsonLine(string path, int row, string line)
{
    try
    {
        return JsonDocument.Parse(line);
    }
    catch (JsonException ex)
    {
        throw new InvalidDataException($"{path} row {row}: invalid JSON.", ex);
    }
}

static string GetRequiredString(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property) ||
        property.ValueKind != JsonValueKind.String ||
        string.IsNullOrWhiteSpace(property.GetString()))
    {
        throw new InvalidDataException($"Required string property '{propertyName}' is missing or empty.");
    }

    return property.GetString()!;
}

static async Task<string> UploadFileAsync(WorkshopContext context, string path)
{
    var uri = new Uri(context.Config.AccountUri, "openai/files?api-version=2025-04-01-preview");
    using var request = await context.Rest.CreateRequestAsync(
        HttpMethod.Post,
        uri,
        FoundryRestClient.CognitiveServicesScope);
    using var content = new MultipartFormDataContent();
    content.Add(new StringContent("fine-tune"), "purpose");
    await using var stream = File.OpenRead(path);
    using var fileContent = new StreamContent(stream);
    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/jsonl");
    content.Add(fileContent, "file", Path.GetFileName(path));
    request.Content = content;
    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    using var response = await client.SendAsync(request);
    var payload = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw new HttpRequestException(
            $"File upload returned {(int)response.StatusCode}: {payload}",
            null,
            response.StatusCode);
    }

    using var json = JsonDocument.Parse(payload);
    return GetRequiredString(json.RootElement, "id");
}

static async Task<FineTuneJob> SubmitJobAsync(
    WorkshopContext context,
    string model,
    string trainingFileId)
{
    var uri = new Uri(
        context.Config.AccountUri,
        "openai/fine_tuning/jobs?api-version=2025-04-01-preview");
    using var response = await context.Rest.SendJsonAsync(
        HttpMethod.Post,
        uri,
        new
        {
            model,
            training_file = trainingFileId,
            method = new { type = "supervised" },
            hyperparameters = new { n_epochs = 2, learning_rate_multiplier = 1.0 },
            trainingType = context.Config.Get("FINE_TUNE_TRAINING_TYPE", "globalStandard"),
            suffix = "csharp-workshop-m14"
        },
        FoundryRestClient.CognitiveServicesScope);
    return new(
        GetRequiredString(response.RootElement, "id"),
        GetRequiredString(response.RootElement, "status"));
}

static Task<JsonDocument> GetJobAsync(WorkshopContext context, string jobId)
{
    var uri = new Uri(
        context.Config.AccountUri,
        $"openai/fine_tuning/jobs/{Uri.EscapeDataString(jobId)}?api-version=2025-04-01-preview");
    return context.Rest.SendJsonAsync(
        HttpMethod.Get,
        uri,
        null,
        FoundryRestClient.CognitiveServicesScope);
}

static async Task MonitorJobAsync(WorkshopContext context, string jobId)
{
    while (true)
    {
        using var job = await GetJobAsync(context, jobId);
        var status = GetRequiredString(job.RootElement, "status");
        Console.WriteLine($"Job {jobId}: {status}");
        if (LabData.TerminalStatuses.Contains(status))
        {
            Console.WriteLine(job.RootElement);
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(15));
    }
}

static bool TryGetOption(string[] arguments, string option, out string value)
{
    for (var index = 0; index < arguments.Length; index++)
    {
        if (!arguments[index].Equals(option, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        value = arguments[index + 1];
        return true;
    }

    value = string.Empty;
    return false;
}

static void PrintSetting(WorkshopConfig config, string name, string note)
{
    Console.WriteLine($"  {(config.IsConfigured(name) ? "[ready]" : "[optional]")} {name} - {note}");
}

static string FormatPercent(double value) =>
    $"{(value * 100).ToString("F1", CultureInfo.InvariantCulture)}%";

internal sealed record ClassificationPrompt(string System, string User);

internal sealed record TrainingRow(string System, string User, string Assistant);

internal sealed record ChatMessage(string Role, string Content);

internal sealed record BenchmarkResult(string Model, double Accuracy);

internal sealed record FineTuneJob(string Id, string Status);

internal sealed record DatasetStats(
    int Rows,
    int Messages,
    int Characters,
    IReadOnlyDictionary<string, int> SeverityCounts);

internal static class LabData
{
    internal static readonly string[] Scenarios =
    [
        "routine maintenance day",
        "ammonia coolant leak detected",
        "thruster misfire during reboost",
        "science payload software crash"
    ];

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static readonly HashSet<string> ValidSeverities =
    [
        "CRITICAL",
        "WARNING",
        "CAUTION",
        "ADVISORY",
        "NOMINAL"
    ];

    internal static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "succeeded",
        "failed",
        "cancelled"
    };
}
