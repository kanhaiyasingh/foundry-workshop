// M14 objective: prepare validated SFT data, compare illustrative baselines, and optionally submit.
// Prerequisites: offline mode needs only .NET; submission needs PROJECT_ENDPOINT,
// FINE_TUNE_MODEL, quota, supported regional capacity, and file/job permissions.
// Run: dotnet run --project .\labs\14-fine-tuning
// Submit: dotnet run --project .\labs\14-fine-tuning -- --submit [--monitor]
// Expect offline: 10 training rows, 2 validation rows, and clearly precomputed scores.
// Submission ids, status, duration, and final service payload are variable.

using System.Net.Http.Headers;
using System.Text.Json;
using FoundryWorkshop.Shared;

// Step 1: Generate the narrow incident-severity task and split it into train/validation JSONL.
return await LabHost.RunAsync(
    "M14 - Fine-tuning and distillation",
    args,
    async context =>
    {
        var outputDirectory = Path.Combine(Environment.CurrentDirectory, "artifacts", "m14");
        Directory.CreateDirectory(outputDirectory);
        var trainingPath = Path.Combine(outputDirectory, "training.jsonl");
        var validationPath = Path.Combine(outputDirectory, "validation.jsonl");
        var examples = BuildExamples();
        await WriteJsonlAsync(trainingPath, examples[..10]);
        await WriteJsonlAsync(validationPath, examples[10..]);

        // Step 2: Validate chat roles/content and print the deterministic row counts.
        var trainingRows = ValidateSftFile(trainingPath);
        var validationRows = ValidateSftFile(validationPath);
        Console.WriteLine($"Validated SFT data: {trainingRows} training rows, {validationRows} validation rows.");

        // Step 3: Show the comparison shape; these bundled values are not live measurements.
        var comparison = new[]
        {
            new { model = "Teacher (precomputed)", accuracy = 0.91, costIndex = 10.0 },
            new { model = "Small base (precomputed)", accuracy = 0.46, costIndex = 1.0 },
            new { model = "Fine-tuned student (precomputed)", accuracy = 0.72, costIndex = 1.0 }
        };
        Console.WriteLine("\nLocal comparison (illustrative workshop benchmark, not a live measurement):");
        foreach (var item in comparison)
        {
            Console.WriteLine($"  {item.model,-34} accuracy={item.accuracy:P0} relative-cost={item.costIndex:F1}");
        }

        if (!context.HasFlag("--submit"))
        {
            Console.WriteLine(
                $"\nData is ready at {outputDirectory}. Add --submit to upload it and create a Foundry SFT job.");
            return;
        }

        // Step 4: Upload both files and submit a two-epoch supervised Foundry job.
        context.Config.Require("PROJECT_ENDPOINT");
        var model = context.Config.Require(
            "FINE_TUNE_MODEL",
            "Set a fine-tunable base model/version supported by your Foundry region.");
        var trainingFileId = await UploadFileAsync(context, trainingPath);
        var validationFileId = await UploadFileAsync(context, validationPath);
        Console.WriteLine($"Uploaded training={trainingFileId}, validation={validationFileId}");

        var jobsUri = new Uri(
            context.Config.AccountUri,
            "openai/fine_tuning/jobs?api-version=2025-04-01-preview");
        using var submitted = await context.Rest.SendJsonAsync(
            HttpMethod.Post,
            jobsUri,
            new
            {
                model,
                training_file = trainingFileId,
                validation_file = validationFileId,
                method = new { type = "supervised" },
                hyperparameters = new { n_epochs = 2, learning_rate_multiplier = 1.0 },
                trainingType = context.Config.Get("FINE_TUNE_TRAINING_TYPE", "globalStandard"),
                suffix = "csharp-workshop"
            },
            FoundryRestClient.CognitiveServicesScope);
        var jobId = submitted.RootElement.GetProperty("id").GetString()
                    ?? throw new InvalidOperationException("Fine-tuning submission returned no job id.");
        Console.WriteLine($"Submitted job {jobId}: {submitted.RootElement.GetProperty("status").GetString()}");

        // Step 5: Optionally poll every 15 seconds until a terminal service status.
        if (context.HasFlag("--monitor"))
        {
            await MonitorJobAsync(context, jobId);
        }
        else
        {
            Console.WriteLine("Re-run with --submit --monitor to poll a newly submitted job to a terminal state.");
        }
    });

// The balanced examples adapt the notebook's domain-classification task for this C# lab.
static TrainingExample[] BuildExamples()
{
    var rows = new (string Report, string Label)[]
    {
        ("Cooling pump B failed; pump A remains healthy and temperatures are stable.", "WARNING"),
        ("Both cooling pumps stopped and reactor temperature is rising rapidly.", "CRITICAL"),
        ("A scheduled sensor calibration completed with all readings in range.", "INFO"),
        ("External coolant leak removed redundancy but the primary loop remains online.", "WARNING"),
        ("Fire detected in the primary electrical cabinet.", "CRITICAL"),
        ("Nightly backup completed and integrity checks passed.", "INFO"),
        ("Packet loss is elevated on one redundant network path.", "WARNING"),
        ("All database replicas are unavailable and writes have stopped.", "CRITICAL"),
        ("A certificate was renewed before expiration.", "INFO"),
        ("Disk usage reached 82 percent on the reporting server.", "WARNING"),
        ("Customer authentication is failing in every region.", "CRITICAL"),
        ("A new dashboard version deployed successfully.", "INFO")
    };
    return rows.Select(row => new TrainingExample(
        [
            new("system", "Classify incident severity as INFO, WARNING, or CRITICAL. Return only the label."),
            new("user", row.Report),
            new("assistant", row.Label)
        ])).ToArray();
}

static async Task WriteJsonlAsync(string path, IEnumerable<TrainingExample> rows)
{
    var jsonlOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    await using var writer = new StreamWriter(path, false);
    foreach (var row in rows)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(row, jsonlOptions));
    }
}

static int ValidateSftFile(string path)
{
    var count = 0;
    foreach (var line in File.ReadLines(path))
    {
        count++;
        JsonDocument row;
        try
        {
            row = JsonDocument.Parse(line);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} row {count}: invalid JSON.", ex);
        }

        using (row)
        {
            if (!row.RootElement.TryGetProperty("messages", out var messages) ||
                messages.ValueKind != JsonValueKind.Array ||
                messages.GetArrayLength() < 2)
            {
                throw new InvalidDataException($"{path} row {count}: messages must be a non-empty array.");
            }

            var roles = messages.EnumerateArray()
                .Select(message =>
                {
                    var role = message.GetProperty("role").GetString();
                    var content = message.GetProperty("content").GetString();
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        throw new InvalidDataException($"{path} row {count}: message content is empty.");
                    }

                    return role;
                })
                .ToArray();
            if (!roles.Contains("user") || !roles.Contains("assistant") || roles[^1] != "assistant")
            {
                throw new InvalidDataException(
                    $"{path} row {count}: SFT needs user and assistant turns and must end with assistant.");
            }
        }
    }

    return count;
}

static async Task<string> UploadFileAsync(WorkshopContext context, string path)
{
    var uri = new Uri(
        context.Config.AccountUri,
        "openai/files?api-version=2025-04-01-preview");
    using var request = await context.Rest.CreateRequestAsync(
        HttpMethod.Post,
        uri,
        FoundryRestClient.CognitiveServicesScope);
    using var content = new MultipartFormDataContent();
    content.Add(new StringContent("fine-tune"), "purpose");
    var stream = File.OpenRead(path);
    var fileContent = new StreamContent(stream);
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
    return json.RootElement.GetProperty("id").GetString()
           ?? throw new InvalidOperationException("File upload returned no id.");
}

static async Task MonitorJobAsync(WorkshopContext context, string jobId)
{
    var uri = new Uri(
        context.Config.AccountUri,
        $"openai/fine_tuning/jobs/{Uri.EscapeDataString(jobId)}?api-version=2025-04-01-preview");
    while (true)
    {
        using var job = await context.Rest.SendJsonAsync(
            HttpMethod.Get,
            uri,
            null,
            FoundryRestClient.CognitiveServicesScope);
        var status = job.RootElement.GetProperty("status").GetString() ?? "unknown";
        Console.WriteLine($"Job status: {status}");
        if (status is "succeeded" or "failed" or "cancelled")
        {
            Console.WriteLine(job.RootElement);
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(15));
    }
}

internal sealed record TrainingExample(IReadOnlyList<TrainingMessage> Messages);

internal sealed record TrainingMessage(string Role, string Content);

// Your Turn: generate more balanced teacher-labelled data, compare another supported
// student with the same held-out evaluator, and never include held-out rows in training.
