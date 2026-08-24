// M9 objective: score a small dataset with deterministic C# checks and an optional LLM judge.
// Full guide: docs/modules/09-evaluation.md
// Prerequisites: offline mode needs only .NET; --cloud needs PROJECT_ENDPOINT and CHAT_MODEL.
// Run: dotnet run --project .\labs\09-evaluation
// Cloud: dotnet run --project .\labs\09-evaluation -- --cloud
// Expect: row-level PASS/FAIL output, a 2/3 lexical baseline, and a JSONL artifact.

using System.Text.Json;
using FoundryWorkshop.Shared;

// Step 1: Define two correct rows and one deliberately incorrect embedding answer.
return await LabHost.RunAsync(
    "M9 - Evaluation",
    args,
    async context =>
    {
        var records = new[]
        {
            new EvalRecord(
                "What does DefaultAzureCredential do?",
                "DefaultAzureCredential tries environment, managed identity, and developer credentials without hard-coded secrets.",
                "It tries several credential sources and uses the first one that works, so no secret is stored in code.",
                "credential sources managed identity secrets"),
            new EvalRecord(
                "How large are text-embedding-3-large vectors?",
                "text-embedding-3-large returns 3072-dimensional vectors by default.",
                "It returns 1536 dimensions.",
                "3072 dimensions"),
            new EvalRecord(
                "What does the Responses API provide?",
                "The Responses API is a stateful model and agent surface with tools and streaming.",
                "It is the stateful API used for models, agents, tools, and streaming.",
                "stateful agents tools streaming")
        };
        // Expected result:
        //   Three evaluation rows ready; the embedding-dimension response is intentionally wrong.

        var outputDirectory = Path.Combine(Environment.CurrentDirectory, "artifacts");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "m09-evaluation-results.jsonl");
        await using var writer = new StreamWriter(outputPath, false);

        // Step 2: Apply deterministic coverage and groundedness checks to each row.
        var passed = 0;
        foreach (var record in records)
        {
            var coverage = Coverage(record.Response, record.ExpectedTerms);
            var grounded = Grounded(record.Response, record.Context);
            // Expected result:
            //   Deterministic coverage and groundedness scores computed for this row.
            double? judgeScore = null;
            string? judgeReason = null;

            // Step 3: Optionally request a structured 1-5 quality judgment from Foundry.
            if (context.HasFlag("--cloud"))
            {
                context.Config.Require(
                    "PROJECT_ENDPOINT",
                    "The default offline evaluator needs no Azure configuration; --cloud enables the LLM judge.");
                using var judge = await context.Rest.CreateResponseAsync(new
                {
                    model = context.Config.ChatModel,
                    input = $"""
                        Score the RESPONSE against CONTEXT from 1 to 5.
                        Return JSON only with a numeric score field and a short string reason field.
                        QUERY: {record.Query}
                        CONTEXT: {record.Context}
                        RESPONSE: {record.Response}
                        """,
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
                                    score = new { type = "number" },
                                    reason = new { type = "string" }
                                },
                                required = new[] { "score", "reason" },
                                additionalProperties = false
                            }
                        }
                    }
                });
                using var judged = JsonDocument.Parse(JsonHelpers.GetOutputText(judge.RootElement));
                judgeScore = judged.RootElement.GetProperty("score").GetDouble();
                judgeReason = judged.RootElement.GetProperty("reason").GetString();
                // Expected result with --cloud:
                //   LLM judge score: <model-dependent 1-5 value>
                //   LLM judge reason: <model-generated short reason>
            }

            // Step 4: Persist every score; judge values vary and appear in the JSONL artifact.
            var rowPassed = coverage >= 0.5 && grounded >= 0.5 && (judgeScore is null || judgeScore >= 3);
            passed += rowPassed ? 1 : 0;
            var result = new
            {
                record.Query,
                record.Response,
                key_term_coverage = coverage,
                lexical_groundedness = grounded,
                llm_judge_score = judgeScore,
                llm_judge_reason = judgeReason,
                passed = rowPassed
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(result, JsonHelpers.Web));
            Console.WriteLine($"{(rowPassed ? "PASS" : "FAIL")} coverage={coverage:P0} grounded={grounded:P0} - {record.Query}");
            // Expected output:
            //   <PASS|FAIL> coverage=<percentage> grounded=<percentage> - <query>
        }

        // The offline 2/3 result exposes lexical-metric limits; it is not a factual-accuracy claim.
        Console.WriteLine($"Passed {passed}/{records.Length}. Results: {outputPath}");
        Console.WriteLine(
            "C# has no stable azure-ai-evaluation equivalent; deterministic evaluators run offline and --cloud adds a real Foundry LLM judge.");
        // Expected output:
        //   FAIL coverage=40% grounded=12% - What does DefaultAzureCredential do?
        //   PASS coverage=100% grounded=50% - How large are text-embedding-3-large vectors?
        //   PASS coverage=100% grounded=60% - What does the Responses API provide?
        //   Passed 2/3. Results: <absolute artifact path>
    });

static double Coverage(string response, string expectedTerms)
{
    var terms = Tokenize(expectedTerms);
    return terms.Count == 0
        ? 0
        : terms.Count(term => response.Contains(term, StringComparison.OrdinalIgnoreCase)) / (double)terms.Count;
}

static double Grounded(string response, string context)
{
    var responseTerms = Tokenize(response);
    var contextTerms = Tokenize(context);
    return responseTerms.Count == 0
        ? 0
        : responseTerms.Count(contextTerms.Contains) / (double)responseTerms.Count;
}

static HashSet<string> Tokenize(string value) =>
    value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(term => term.Trim('.', ',', ':', ';', '-', '(', ')').ToLowerInvariant())
        .Where(term => term.Length >= 5)
        .ToHashSet();

internal sealed record EvalRecord(
    string Query,
    string Context,
    string Response,
    string ExpectedTerms);

// Your Turn:
// 1. Break a good row. Change the first EvalRecord.Response so it contradicts Context,
//    rerun, and watch lexical_groundedness drop and the row flip to FAIL.
// 2. Add fluency and similarity. Implement Fluency and Similarity C# evaluators beside
//    Coverage and Grounded; add GroundTruth to EvalRecord for similarity and write both
//    new scores to the JSONL output.
// 3. Tighten your custom rule. Raise the coverage threshold in rowPassed from 0.5 to
//    0.8 and rerun; more rows should fail.
