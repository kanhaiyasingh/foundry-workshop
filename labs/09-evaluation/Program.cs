using System.Text.Json;
using System.Text.Json.Serialization;
using FoundryWorkshop.Shared;

// Cell 0 [markdown]
// # M9 · Evaluation
//
// Goal: measure answer quality systematically — build a test set, score it with
// quality, agent, and custom evaluators, then run a batch evaluate() that logs to
// the Foundry portal.
//
// You'll use: the C# equivalents of azure-ai-evaluation's RelevanceEvaluator,
// GroundednessEvaluator, IntentResolutionEvaluator, ToolCallAccuracyEvaluator,
// a custom callable, and evaluate(...).
//
// So far you've built agents. Now you'll grade them. "It looked good when I tried it"
// doesn't scale — you need numbers you can track across prompt changes, model swaps,
// and releases. That's offline evaluation: run a fixed test set through your app and
// score each answer with evaluators.
//
// The arc you'll build:
//
// test dataset ──▶ quality evaluators   (relevance, groundedness, coherence)
//    (jsonl)   ──▶ agent  evaluators    (intent resolution, tool-call accuracy)
//              ──▶ custom evaluator      (your own scoring rule)
//              ──▶ evaluate(...) batch   ──▶ metrics + Foundry portal run
//
// The quality-loop diagram is at ../../assets/eval-observability.png.
//
// Note — One project, one grader model:
// Most quality/agent evaluators are LLM-as-judge — they call a model to score each
// answer. The reference derives a separate admin project from a hashed subscription
// suffix; we just reuse this project's CHAT_MODEL as the judge. If .env isn't ready,
// complete the workshop setup first.

return await LabHost.RunAsync(
    "M9 - Evaluation",
    args,
    async context =>
    {
        // Cell 1 [code]
        // Print the current date and time.
        var currentDateTime = DateTime.Now;
        Console.WriteLine($"Current date and time: {currentDateTime:yyyy-MM-dd HH:mm:ss.ffffff}");

        // Cell 2 [markdown]
        // ## 1. Configure
        //
        // Use the same .env as every lab. Evaluators that act as LLM judges need the
        // OpenAI-style account endpoint (not the /api/projects/... path), so derive it
        // from PROJECT_ENDPOINT — there is no extra variable to set.

        // Cell 3 [code]
        var projectEndpoint = context.Config.ProjectEndpoint;
        var chatModel = context.Config.ChatModel; // Exact notebook default: gpt-4.1-mini.
        var aoaiEndpoint = context.Config.AccountUri;

        Console.WriteLine($"Project : {projectEndpoint}");
        Console.WriteLine($"AOAI    : {aoaiEndpoint}");
        Console.WriteLine($"Judge   : {chatModel}");

        // Cell 4 [markdown]
        // Expected output:
        // Project : https://<account>.services.ai.azure.com/api/projects/<project>
        // AOAI    : https://<account>.services.ai.azure.com/
        // Judge   : gpt-4.1-mini
        //
        // The judge and the model under test happen to be the same deployment here; in
        // production you often grade a cheap model with a stronger judge.

        // Cell 5 [markdown]
        // ## 2. The grader's model config
        //
        // azure-ai-evaluation needs an AzureOpenAIModelConfiguration describing the
        // judge deployment, plus a DefaultAzureCredential for keyless Entra auth. The
        // credential is passed to each evaluator (not baked into the config), which also
        // sidesteps a known Python 3.13 validation quirk in the 1.16.x SDK.

        // Cell 6 [code]
        // C# adaptation: WorkshopContext creates the same keyless TokenCredential and
        // holds the deployment configuration. The stable C# package does not mirror
        // AzureOpenAIModelConfiguration, so cloud judges use the project's Responses
        // REST surface with strict JSON schema. AOAI_ENDPOINT is still derived and
        // displayed because the Python evaluator's classic chat route requires it.
        var judge = new EvaluationJudge(context, chatModel);
        Console.WriteLine("credential   : ready");
        Console.WriteLine($"model_config : ready -> {chatModel}");

        // Cell 7 [markdown]
        // Expected output:
        // credential   : ready
        // model_config : ready -> gpt-4.1-mini
        //
        // Warning — API is evolving:
        // Evaluator constructors and result keys shift between azure-ai-evaluation minor
        // versions. The Python lab is written for 1.16.x (pinned in pyproject.toml). If a
        // field name differs, check the installed version. This C# lab keeps the same
        // metric names, score range, threshold, prompts, and output contract explicitly.

        // Cell 8 [markdown]
        // ## 3. A small test dataset
        //
        // Evaluation starts with data: rows of query → response, plus the context the
        // answer should be grounded in and a ground_truth to compare against. The
        // reference captures these from a live agent thread; we hand-write four rows so
        // the lab is self-contained — and make row 3 deliberately wrong so the scores
        // have something to catch.

        // Cell 9 [code]
        var records = new[]
        {
            new EvalRecord(
                "What does DefaultAzureCredential do in a Foundry app?",
                "DefaultAzureCredential tries credential sources in order (environment, " +
                "managed identity, az login) and uses the first that works — no secrets in code.",
                "It authenticates by trying several sources in sequence — environment " +
                "variables, managed identity, then your az login session — and uses the first " +
                "that succeeds, so you never hard-code secrets.",
                "Authenticates via a chain of sources (env, managed identity, az login); " +
                "requires no secrets in code."),
            new EvalRecord(
                "How does agent versioning work in Foundry?",
                "An agent is stored under a stable name. create_version stores a new version " +
                "whenever the definition changes; callers reference the name.",
                "Each agent has a stable name, and create_version stores a new numbered version " +
                "whenever the definition changes. Callers reference the agent by name, so they " +
                "keep working as you publish new versions.",
                "Agents are stored by name; create_version makes a new version on each " +
                "change; callers reference by name."),
            new EvalRecord(
                "What embedding size does text-embedding-3-large return?",
                "text-embedding-3-large returns 3072-dimensional vectors.",
                "The text-embedding-3-large model returns 1536-dimensional vectors by default.",
                "text-embedding-3-large returns 3072-dimensional vectors."),
            new EvalRecord(
                "What is the Responses API used for?",
                "The Responses API is the modern stateful surface that powers agents and tools; " +
                "a minimal call takes a model and an input and returns output_text.",
                "It's Foundry's modern, stateful interface that powers agents and tools. A " +
                "minimal call passes a model and an input, and the reply is in output_text.",
                "Modern stateful API that powers agents and tools; minimal call takes " +
                "model + input, returns output_text.")
        };

        var dataPath = Path.Combine(Environment.CurrentDirectory, "eval_test_data.jsonl");
        await WriteJsonLinesAsync(dataPath, records);
        Console.WriteLine($"Wrote {records.Length} rows -> {Path.GetFileName(dataPath)}");
        Console.WriteLine(
            "Row 3 is intentionally wrong (1536 vs 3072) — watch groundedness flag it.");

        // Cell 10 [markdown]
        // Expected output:
        // Wrote 4 rows -> eval_test_data.jsonl
        // Row 3 is intentionally wrong (1536 vs 3072) — watch groundedness flag it.
        //
        // A .jsonl file (one JSON object per line) is the format evaluate() consumes in
        // section 7. Real datasets have dozens to hundreds of rows; the shape is identical.

        // Cell 11 [markdown]
        // ## 4. Quality evaluators
        //
        // The bread-and-butter scores. Each is an LLM-as-judge returning a 1–5 score
        // plus a pass/fail against threshold 3. Spot-check three on single rows:
        //
        // - Relevance — does the answer address the question? (query, response)
        // - Groundedness — is it supported by context? (query, response, context)
        // - Coherence — is it logically structured? (query, response)

        // Cell 12 [code]
        var good = records[0];
        var bad = records[2];

        var goodRelevance = await judge.ScoreAsync(
            "relevance",
            good,
            "Score how directly and completely RESPONSE addresses QUERY.");
        var goodGroundedness = await judge.ScoreAsync(
            "groundedness",
            good,
            "Score whether every factual claim in RESPONSE is supported by CONTEXT.");
        var badGroundedness = await judge.ScoreAsync(
            "groundedness",
            bad,
            "Score whether every factual claim in RESPONSE is supported by CONTEXT.");

        Console.WriteLine("GOOD row");
        Console.WriteLine($"  relevance    : {FormatMetric(goodRelevance)}");
        Console.WriteLine($"  groundedness : {FormatMetric(goodGroundedness)}");
        Console.WriteLine();
        Console.WriteLine("BAD row (wrong dimension)");
        Console.WriteLine($"  groundedness : {FormatMetric(badGroundedness)}");

        // Cell 13 [markdown]
        // Expected output:
        // GOOD row
        //   relevance    : {'relevance': 5.0, 'relevance_result': 'pass',
        //                   'relevance_threshold': 3}
        //   groundedness : {'groundedness': 5.0, 'groundedness_result': 'pass',
        //                   'groundedness_threshold': 3}
        //
        // BAD row (wrong dimension)
        //   groundedness : {'groundedness': 2.0, 'groundedness_result': 'fail',
        //                   'groundedness_threshold': 3}
        //
        // Exact cloud scores vary, but the contrast is the point: the grounded answer
        // scores high, while the contradicted one (1536 vs context's 3072) gets flagged
        // fail. That's the signal you couldn't see by eyeballing.

        // Cell 14 [markdown]
        // ## 5. Agent-specific evaluators
        //
        // Quality scores judge the answer. Agent evaluators judge the behaviour — did it
        // understand intent and call the right tools? These also use the judge model:
        //
        // - IntentResolutionEvaluator — did the agent grasp what the user wanted?
        // - TaskAdherenceEvaluator — did it follow its instructions?
        // - ToolCallAccuracyEvaluator — did it call the right tool with the right args?
        //
        // Feed a captured turn directly. For live agent threads, the Python SDK ships
        // AIAgentConverter to turn thread_id/run_id into this shape.

        // Cell 15 [code]
        const string capturedQuery =
            "How many dimensions does text-embedding-3-large output?";
        const string capturedResponse =
            "It returns 3072-dimensional vectors. [kb:embeddings]";

        var toolCalls = new object[]
        {
            new
            {
                type = "tool_call",
                tool_call_id = "call_1",
                name = "kb_search",
                arguments = new { query = "text-embedding-3-large dimensions" }
            }
        };
        var toolDefinitions = new object[]
        {
            new
            {
                name = "kb_search",
                description = "Search the knowledge base for a query.",
                parameters = new
                {
                    type = "object",
                    properties = new { query = new { type = "string" } },
                    required = new[] { "query" }
                }
            }
        };

        var intent = await judge.ScoreAgentAsync(
            "intent_resolution",
            capturedQuery,
            capturedResponse,
            "Score whether RESPONSE correctly identifies and resolves the user's intent.");
        var adherence = await judge.ScoreAgentAsync(
            "task_adherence",
            capturedQuery,
            capturedResponse,
            "Score whether RESPONSE follows the task implied by QUERY.");
        var toolAccuracy = await judge.ScoreToolCallAsync(
            capturedQuery,
            toolCalls,
            toolDefinitions);

        Console.WriteLine($"intent resolution : {FormatMetric(intent)}");
        Console.WriteLine($"task adherence    : {FormatMetric(adherence)}");
        Console.WriteLine($"tool-call accuracy: {FormatMetric(toolAccuracy)}");

        // Cell 16 [markdown]
        // Expected output:
        // intent resolution : {'intent_resolution': 5.0,
        //                      'intent_resolution_result': 'pass', ...}
        // task adherence    : {'task_adherence': 4.0,
        //                      'task_adherence_result': 'pass', ...}
        // tool-call accuracy: {'tool_call_accuracy': 5.0,
        //                      'tool_call_accuracy_result': 'pass', ...}
        //
        // Tip — Capturing real agent threads:
        // Instead of hand-building tool_calls, point
        // AIAgentConverter(project_client).convert(thread_id, run_id) at a real run from
        // M3 to produce eval-ready rows. The converter's exact signature is evolving
        // across SDK releases — pin azure-ai-evaluation and check its version if a field
        // differs. In C#, capture the same query, response, tool-call, and tool-definition
        // fields from the Responses/Agents REST payload before calling these evaluators.

        // Cell 17 [markdown]
        // ## 6. A custom evaluator
        //
        // Built-ins won't cover every rule your domain cares about. A custom evaluator is
        // just a callable returning a score dictionary — no LLM required. Here we enforce
        // a contract: the answer must cover the key terms from its ground_truth. This is
        // simple, deterministic, and cheap.

        // Cell 18 [code]
        var coverageEvaluator = new KeyTermCoverageEvaluator();
        var goodCoverage = coverageEvaluator.Evaluate(
            records[0].Response,
            records[0].GroundTruth);
        var badCoverage = coverageEvaluator.Evaluate(
            records[2].Response,
            records[2].GroundTruth);
        Console.WriteLine($"good row: {FormatCoverage(goodCoverage)}");
        Console.WriteLine($"bad  row: {FormatCoverage(badCoverage)}");

        // Cell 19 [markdown]
        // Expected output:
        // good row: {'key_term_coverage': 0.8, 'key_term_pass': True}
        // bad  row: {'key_term_coverage': 0.43, 'key_term_pass': False}
        //
        // Any object with a call operation returning a {metric: value} dictionary is a
        // valid evaluator — evaluate() treats the class exactly like built-ins. Use this
        // for business rules: citation format, banned phrases, length bounds, schema checks.

        // Cell 20 [markdown]
        // ## 7. Batch evaluate → metrics + portal
        //
        // Spot-checks are for debugging; evaluate() is the real run. It applies all
        // evaluators across every row of the .jsonl, aggregates metrics, and — when
        // azure_ai_project is supplied — uploads the run to the Foundry portal and
        // returns a studio_url. column_mapping tells each evaluator which dataset
        // columns to read.

        // Cell 21 [code]
        // C# adaptation: there is no direct stable C# evaluate(...) facade matching the
        // Python 1.16.x API. The loop below preserves its column mappings and local JSONL
        // result artifact. Each built-in score is a strict-schema LLM judge call and the
        // same dataset is submitted to Foundry Evals REST, which returns the portal report
        // URL. The custom callable remains local because Foundry cannot serialize a C#
        // delegate.
        var batchRows = new List<BatchResult>();
        foreach (var record in records)
        {
            var relevance = await judge.ScoreAsync(
                "relevance",
                record,
                "Score how directly and completely RESPONSE addresses QUERY.");
            var groundedness = await judge.ScoreAsync(
                "groundedness",
                record,
                "Score whether every factual claim in RESPONSE is supported by CONTEXT.");
            var coherence = await judge.ScoreAsync(
                "coherence",
                record,
                "Score whether RESPONSE is clear, logically structured, and internally consistent.");
            var keyTerm = coverageEvaluator.Evaluate(record.Response, record.GroundTruth);

            batchRows.Add(new BatchResult(
                record,
                relevance,
                groundedness,
                coherence,
                keyTerm));
        }

        var resultsPath = Path.Combine(Environment.CurrentDirectory, "eval_results.jsonl");
        await WriteJsonLinesAsync(resultsPath, batchRows.Select(ToArtifactRow));

        var metrics = new Dictionary<string, double>
        {
            ["relevance.relevance"] = batchRows.Average(row => row.Relevance.Score),
            ["groundedness.groundedness"] = batchRows.Average(row => row.Groundedness.Score),
            ["coherence.coherence"] = batchRows.Average(row => row.Coherence.Score),
            ["key_term.key_term_coverage"] = batchRows.Average(row => row.KeyTerm.Coverage),
            ["key_term.key_term_pass"] = batchRows.Average(row => row.KeyTerm.Passed ? 1.0 : 0.0)
        };

        Console.WriteLine("Aggregate metrics:");
        foreach (var metric in metrics)
        {
            Console.WriteLine($"  {metric.Key,-32} {metric.Value:0.##}");
        }

        var portalUrl = await PublishFoundryEvaluationAsync(context, records, chatModel);
        Console.WriteLine();
        Console.WriteLine($"Portal: {portalUrl}");
        Console.WriteLine($"Results: {resultsPath}");

        // Cell 22 [markdown]
        // Expected output:
        // Aggregate metrics:
        //   relevance.relevance                4.75
        //   groundedness.groundedness          4.25
        //   coherence.coherence                4.50
        //   key_term.key_term_coverage         0.71
        //   key_term.key_term_pass             0.75
        //
        // Portal: https://ai.azure.com/.../evaluation/<run-id>
        //
        // Exact LLM scores vary. The means hover high because three of four rows are
        // solid; the wrong embedding row drags groundedness and key_term down — exactly
        // the regression signal you want. Open the studio/report URL to see per-row
        // scores, judge reasoning, and a trend line across runs. This C# version writes
        // all row-level scores and reasons to eval_results.jsonl.

        // Cell 23 [markdown]
        // Tip — This is the foundation for the next lab:
        // Offline evaluation runs before you ship. In M10 you'll wire the same evaluators
        // to run continuously on live production traffic — the other half of the quality
        // loop in the diagram above.

        // Cell 24 [markdown]
        // ## 🧪 Your turn
        //
        // 1. Break a good row. Edit row 1's response in records to contradict its context,
        //    then rerun. The C# program rewrites the JSONL before section 7; watch
        //    groundedness drop and the row flip to fail.
        // 2. Add Fluency + Similarity. Add fluency and similarity evaluator prompts to
        //    the batch (similarity also needs ground_truth in its mapping) and compare
        //    the new JSONL columns.
        // 3. Tighten your custom rule. Construct KeyTermCoverageEvaluator(threshold: 0.8)
        //    and re-run — more rows fail. This is how you turn a soft expectation into an
        //    enforceable gate.
        //
        // You built a test set, scored it with quality, agent, and custom evaluators,
        // and ran a batch evaluation that logs to the Foundry portal.
        // Next: watch those same signals on live traffic with tracing and continuous
        // evaluation in M10 · Observability & Tracing.
    },
    "PROJECT_ENDPOINT");

static async Task WriteJsonLinesAsync<T>(string path, IEnumerable<T> rows)
{
    var jsonLines = new JsonSerializerOptions(JsonHelpers.Web)
    {
        WriteIndented = false
    };
    await using var writer = new StreamWriter(path, false);
    foreach (var row in rows)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(row, jsonLines));
    }
}

static string FormatMetric(EvaluationMetric metric) =>
    $"{{'{metric.Name}': {metric.Score:0.0}, " +
    $"'{metric.Name}_result': '{(metric.Passed ? "pass" : "fail")}', " +
    $"'{metric.Name}_threshold': {EvaluationMetric.PassThreshold}}}";

static string FormatCoverage(CoverageMetric metric) =>
    $"{{'key_term_coverage': {metric.Coverage:0.##}, " +
    $"'key_term_pass': {(metric.Passed ? "True" : "False")}}}";

static object ToArtifactRow(BatchResult row) => new
{
    row.Record.Query,
    row.Record.Response,
    row.Record.Context,
    ground_truth = row.Record.GroundTruth,
    relevance = row.Relevance,
    groundedness = row.Groundedness,
    coherence = row.Coherence,
    key_term_coverage = row.KeyTerm.Coverage,
    key_term_pass = row.KeyTerm.Passed
};

static async Task<string> PublishFoundryEvaluationAsync(
    WorkshopContext context,
    IReadOnlyCollection<EvalRecord> records,
    string chatModel)
{
    using var evaluation = await context.Rest.SendProjectJsonAsync(
        HttpMethod.Post,
        "openai/v1/evals",
        new
        {
            name = $"M9 evaluation {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            data_source_config = new
            {
                type = "custom",
                item_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string" },
                        response = new { type = "string" },
                        context = new { type = "string" },
                        ground_truth = new { type = "string" }
                    },
                    required = new[] { "query", "response", "context", "ground_truth" }
                }
            },
            testing_criteria = new object[]
            {
                BuiltInCriterion(
                    "relevance",
                    "builtin.relevance",
                    chatModel,
                    new { query = "{{item.query}}", response = "{{item.response}}" }),
                BuiltInCriterion(
                    "groundedness",
                    "builtin.groundedness",
                    chatModel,
                    new
                    {
                        query = "{{item.query}}",
                        response = "{{item.response}}",
                        context = "{{item.context}}"
                    }),
                BuiltInCriterion(
                    "coherence",
                    "builtin.coherence",
                    chatModel,
                    new { query = "{{item.query}}", response = "{{item.response}}" })
            }
        });

    var evaluationId = evaluation.RootElement.GetProperty("id").GetString()
        ?? throw new JsonException("Foundry create-eval response omitted id.");

    var content = records.Select(record => new
    {
        item = new
        {
            record.Query,
            record.Response,
            record.Context,
            ground_truth = record.GroundTruth
        }
    }).ToArray();

    using var run = await context.Rest.SendProjectJsonAsync(
        HttpMethod.Post,
        $"openai/v1/evals/{Uri.EscapeDataString(evaluationId)}/runs",
        new
        {
            name = $"M9 dataset run {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            data_source = new
            {
                type = "jsonl",
                source = new { type = "file_content", content }
            }
        });

    var runId = run.RootElement.GetProperty("id").GetString()
        ?? throw new JsonException("Foundry create-run response omitted id.");
    var current = run.RootElement.Clone();

    for (var attempt = 0; attempt < 60; attempt++)
    {
        var status = current.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;
        if (status is "completed" or "failed" or "cancelled")
        {
            if (status != "completed")
            {
                throw new InvalidOperationException(
                    $"Foundry evaluation run {runId} ended with status {status}: {current}");
            }

            return GetPortalUrl(current, evaluationId, runId);
        }

        await Task.Delay(TimeSpan.FromSeconds(5));
        using var polled = await context.Rest.SendProjectJsonAsync(
            HttpMethod.Get,
            $"openai/v1/evals/{Uri.EscapeDataString(evaluationId)}/runs/{Uri.EscapeDataString(runId)}");
        current = polled.RootElement.Clone();
    }

    throw new TimeoutException(
        $"Foundry evaluation run {runId} did not finish within five minutes.");
}

static object BuiltInCriterion(
    string name,
    string evaluatorName,
    string model,
    object dataMapping) => new
{
    type = "azure_ai_evaluator",
    name,
    evaluator_name = evaluatorName,
    initialization_parameters = new { model },
    data_mapping = dataMapping
};

static string GetPortalUrl(JsonElement run, string evaluationId, string runId)
{
    foreach (var propertyName in new[] { "report_url", "studio_url" })
    {
        if (run.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
        {
            return property.GetString()!;
        }
    }

    return $"(run completed; eval_id={evaluationId}, run_id={runId}, no report_url returned)";
}

internal sealed class EvaluationJudge(
    WorkshopContext context,
    string model)
{
    public async Task<EvaluationMetric> ScoreAsync(
        string metric,
        EvalRecord record,
        string rubric)
    {
        var prompt = $"""
            You are an impartial Microsoft Foundry evaluation judge.
            {rubric}
            Use the integer scale 1 (worst) through 5 (best).
            A score of 3 or higher passes.

            QUERY:
            {record.Query}

            CONTEXT:
            {record.Context}

            GROUND_TRUTH:
            {record.GroundTruth}

            RESPONSE:
            {record.Response}

            Return only JSON matching the supplied schema.
            """;
        return await RequestScoreAsync(metric, prompt);
    }

    public async Task<EvaluationMetric> ScoreAgentAsync(
        string metric,
        string query,
        string response,
        string rubric)
    {
        var prompt = $"""
            You are an impartial Microsoft Foundry agent-behavior judge.
            {rubric}
            Use the integer scale 1 (worst) through 5 (best).
            A score of 3 or higher passes.

            QUERY:
            {query}

            RESPONSE:
            {response}

            Return only JSON matching the supplied schema.
            """;
        return await RequestScoreAsync(metric, prompt);
    }

    public async Task<EvaluationMetric> ScoreToolCallAsync(
        string query,
        IReadOnlyCollection<object> toolCalls,
        IReadOnlyCollection<object> toolDefinitions)
    {
        const string metric = "tool_call_accuracy";
        var prompt = $"""
            You are an impartial Microsoft Foundry tool-call judge.
            Score whether TOOL_CALLS choose the correct tool and valid arguments for QUERY,
            using TOOL_DEFINITIONS as the contract.
            Use the integer scale 1 (worst) through 5 (best).
            A score of 3 or higher passes.

            QUERY:
            {query}

            TOOL_CALLS:
            {JsonSerializer.Serialize(toolCalls, JsonHelpers.Web)}

            TOOL_DEFINITIONS:
            {JsonSerializer.Serialize(toolDefinitions, JsonHelpers.Web)}

            Return only JSON matching the supplied schema.
            """;
        return await RequestScoreAsync(metric, prompt);
    }

    private async Task<EvaluationMetric> RequestScoreAsync(string metric, string prompt)
    {
        // Strict schema preserves the notebook's LLM-judge JSON parsing contract and
        // avoids accepting prose or a success-shaped fallback.
        using var response = await context.Rest.CreateResponseAsync(new
        {
            model,
            input = prompt,
            temperature = 0,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "evaluation",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            score = new { type = "number", minimum = 1, maximum = 5 },
                            reason = new { type = "string" }
                        },
                        required = new[] { "score", "reason" },
                        additionalProperties = false
                    }
                }
            }
        });

        var outputText = JsonHelpers.GetOutputText(response.RootElement);
        using var judged = JsonDocument.Parse(outputText);
        var score = judged.RootElement.GetProperty("score").GetDouble();
        if (score is < 1 or > 5)
        {
            throw new JsonException($"Judge returned out-of-range score {score} for {metric}.");
        }

        var reason = judged.RootElement.GetProperty("reason").GetString()
            ?? throw new JsonException($"Judge omitted reason for {metric}.");
        return new EvaluationMetric(
            metric,
            score,
            score >= EvaluationMetric.PassThreshold ? "pass" : "fail",
            reason);
    }

}

internal sealed class KeyTermCoverageEvaluator(int minLength = 4, double threshold = 0.5)
{
    public CoverageMetric Evaluate(string response, string groundTruth)
    {
        var terms = groundTruth
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= minLength)
            .Select(word => word.ToLowerInvariant().Trim('.', ',', ';', ':', '(', ')'))
            .ToHashSet(StringComparer.Ordinal);
        var haystack = response.ToLowerInvariant();
        var hits = terms.Count(term => haystack.Contains(term, StringComparison.Ordinal));
        var coverage = terms.Count == 0
            ? 0
            : Math.Round(hits / (double)terms.Count, 2);
        return new CoverageMetric(coverage, coverage >= threshold);
    }
}

internal sealed record EvalRecord(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("response")] string Response,
    [property: JsonPropertyName("ground_truth")] string GroundTruth);

internal sealed record EvaluationMetric(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("reason")] string Reason)
{
    public const double PassThreshold = 3;

    [JsonIgnore]
    public bool Passed => Score >= PassThreshold;
}

internal sealed record CoverageMetric(
    [property: JsonPropertyName("key_term_coverage")] double Coverage,
    [property: JsonPropertyName("key_term_pass")] bool Passed);

internal sealed record BatchResult(
    EvalRecord Record,
    EvaluationMetric Relevance,
    EvaluationMetric Groundedness,
    EvaluationMetric Coherence,
    CoverageMetric KeyTerm);
