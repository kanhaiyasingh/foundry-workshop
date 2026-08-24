// M8 objective: let a reasoning model investigate a bounded corpus, then use a chat model
// to turn the findings into a cited report.
// Full guide: docs/modules/08-deep-research.md
// Prerequisites: PROJECT_ENDPOINT, CHAT_MODEL, and a deployed tool-capable RESEARCH_MODEL
// (defaults to o3-deep-research).
// Check: dotnet run --project .\labs\08-deep-research -- --check
// Run:   dotnet run --project .\labs\08-deep-research
// Expect: model-dependent search/fetch iterations, tool history, a separately synthesized
// report with [doc-id] citations, and an out-of-scope boundary test.

using System.Text.Json;
using FoundryWorkshop.Shared;

// Step 1: Define the only documents the research model is allowed to use.
var corpus = new Dictionary<string, (string Title, string Text)>
{
    ["doc-001"] = (
        "Prototypical Networks for Few-Shot Learning",
        "Prototypical networks learn a metric space where classification is performed by " +
        "computing distances to per-class prototypes. Strong on miniImageNet 5-way 5-shot; " +
        "simpler than matching networks."),
    ["doc-002"] = (
        "Model-Agnostic Meta-Learning (MAML)",
        "MAML learns an initialization that adapts to a new task in a few gradient steps. " +
        "Model-agnostic; competitive few-shot accuracy but costly second-order gradients."),
    ["doc-003"] = (
        "Matching Networks for One Shot Learning",
        "Matching networks use attention over a labelled support set to classify with one " +
        "example per class; introduced episodic training."),
    ["doc-004"] = (
        "Linformer: Self-Attention with Linear Complexity",
        "Linformer projects keys and values to a low-rank form, reducing self-attention " +
        "from O(n^2) to O(n) in sequence length.")
};
// Expected result:
//   Corpus: 4 docs

// Step 2: Configure separate research and synthesis models plus the bounded loop.
return await LabHost.RunAsync(
    "M8 - Deep research",
    args,
    async context =>
    {
        var config = context.Config;
        var researchModel = config.Get("RESEARCH_MODEL", "o3-deep-research");
        var synthesisModel = config.ChatModel;
        const int maxIterations = 6;

        Console.WriteLine($"Project   : {config.ProjectEndpoint}");
        Console.WriteLine(
            $"Research  : {researchModel}" +
            (config.IsConfigured("RESEARCH_MODEL") ? string.Empty : " (default)"));
        Console.WriteLine($"Synthesis : {synthesisModel}");
        // Expected output:
        //   Project   : https://<account>.services.ai.azure.com/api/projects/<project>
        //   Research  : <RESEARCH_MODEL deployment>
        //   Synthesis : <CHAT_MODEL deployment>
        // Splitting the roles lets the reasoning model investigate while a faster, cheaper
        // chat model writes the final report.

        // Step 3: Expose search/fetch schemas to the research model.
        var tools = new object[]
        {
            new
            {
                type = "function",
                name = "search",
                description = "Search the bounded paper corpus and return document ids with summaries.",
                parameters = new
                {
                    type = "object",
                    properties = new { query = new { type = "string" } },
                    required = new[] { "query" },
                    additionalProperties = false
                }
            },
            new
            {
                type = "function",
                name = "fetch",
                description = "Fetch one paper by document_id.",
                parameters = new
                {
                    type = "object",
                    properties = new { document_id = new { type = "string" } },
                    required = new[] { "document_id" },
                    additionalProperties = false
                }
            }
        };
        // Expected result:
        //   Tools: search, fetch
        //   Maximum iterations: 6

        // Step 4: Run the agentic research loop and retain response state across iterations.
        async Task<ResearchResult> RunDeepResearchAsync(string question)
        {
            object input = question;
            string? previousResponseId = null;
            var toolCallsMade = new List<string>();
            string? finalIterationText = null;

            for (var iteration = 1; iteration <= maxIterations; iteration++)
            {
                Console.WriteLine($"Iteration {iteration}");
                using var response = await context.Rest.CreateResponseAsync(new
                {
                    model = researchModel,
                    instructions = """
                        You are a deep-research assistant. Investigate the user's question using the
                        search and fetch tools: search broadly, fetch the most relevant documents,
                        and search again to fill gaps. Cite document ids like [doc-001]. If the
                        corpus does not cover the question, say so explicitly rather than guessing.
                        """,
                    input,
                    previous_response_id = previousResponseId,
                    tools
                });
                previousResponseId = response.RootElement.GetProperty("id").GetString();
                var responseText = JsonHelpers.GetOutputText(response.RootElement);
                finalIterationText = responseText;
                var calls = JsonHelpers.GetFunctionCalls(response.RootElement).ToArray();
                if (calls.Length == 0)
                {
                    return new ResearchResult(
                        string.IsNullOrWhiteSpace(responseText)
                            ? "(model concluded without a summary message)"
                            : responseText,
                        iteration,
                        toolCallsMade);
                }

                var outputs = new List<object>();
                foreach (var call in calls)
                {
                    var callId = call.GetProperty("call_id").GetString();
                    var name = call.GetProperty("name").GetString() ?? string.Empty;
                    using var arguments = JsonDocument.Parse(
                        call.GetProperty("arguments").GetString() ?? "{}");
                    object result = name switch
                    {
                        "search" => SearchCorpus(
                            arguments.RootElement.GetProperty("query").GetString() ?? string.Empty,
                            corpus),
                        "fetch" => FetchDocument(
                            arguments.RootElement.GetProperty("document_id").GetString() ?? string.Empty,
                            corpus),
                        _ => new { error = $"Unknown tool '{name}'." }
                    };
                    toolCallsMade.Add(name);
                    outputs.Add(new
                    {
                        type = "function_call_output",
                        call_id = callId,
                        output = JsonSerializer.Serialize(result)
                    });
                }

                input = outputs;
            }

            return new ResearchResult(
                string.IsNullOrWhiteSpace(finalIterationText)
                    ? "(model concluded without a summary message)"
                    : finalIterationText,
                maxIterations,
                toolCallsMade);
        }
        // Expected result:
        //   The loop stops when the research model returns no tool calls.

        // Step 5: Research the notebook question, then synthesize its findings with CHAT_MODEL.
        const string question =
            "What are the main approaches to few-shot learning in the corpus, " +
            "and how do they differ? Cite the documents.";
        var research = await RunDeepResearchAsync(question);
        using var synthesis = await context.Rest.CreateResponseAsync(new
        {
            model = synthesisModel,
            instructions = """
                You are a research report writer. Turn the findings into a concise,
                well-structured report. Preserve every [doc-id] citation.
                """,
            input = $"Question:\n{question}\n\nFindings:\n{research.Findings}"
        });
        var report = JsonHelpers.GetOutputText(synthesis.RootElement);

        Console.WriteLine($"\nIterations : {research.Iterations}");
        Console.WriteLine($"Tool calls : [{string.Join(", ", research.ToolCalls)}]\n");
        Console.WriteLine(report);
        // Expected output:
        //   Iterations : <model-dependent count>
        //   Tool calls : [search, fetch, ...]
        //   <synthesized comparison preserving [doc-id] citations>

        // Step 6: Confirm the bounded corpus makes unsupported research explicit.
        var outOfScope = await RunDeepResearchAsync(
            "What are the latest breakthroughs in nuclear fusion energy?");
        Console.WriteLine($"\nIterations : {outOfScope.Iterations}");
        Console.WriteLine($"Tool calls : [{string.Join(", ", outOfScope.ToolCalls)}]\n");
        Console.WriteLine(outOfScope.Findings);
        // Expected output:
        //   <research findings that explicitly say the corpus cannot answer the question>
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL");

// Step 7: Search returns matching corpus summaries and logs each research action.
static object SearchCorpus(
    string query,
    IReadOnlyDictionary<string, (string Title, string Text)> corpus)
{
    var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(term => term.Trim(' ', '.', ',', '?').ToLowerInvariant())
        .Where(term => term.Length > 3)
        .ToHashSet();
    var hits = corpus
        .Where(item => terms.Any(term =>
            $"{item.Value.Title} {item.Value.Text}".Contains(term, StringComparison.OrdinalIgnoreCase)))
        .Select(item => new
        {
            id = item.Key,
            title = item.Value.Title,
            summary = item.Value.Text.Length > 120
                ? item.Value.Text[..120] + "..."
                : item.Value.Text
        })
        .ToArray();
    Console.WriteLine($"   search({JsonSerializer.Serialize(query)}) -> {hits.Length} hit(s)");
    return new { results = hits };
}
// Expected result:
//   search returns matching document ids, titles, and summaries, or no hits.

// Step 8: Fetch returns one approved document or an explicit not-found result.
static object FetchDocument(
    string id,
    IReadOnlyDictionary<string, (string Title, string Text)> corpus)
{
    Console.WriteLine($"   fetch({JsonSerializer.Serialize(id)})");
    return corpus.TryGetValue(id, out var document)
        ? new { id, title = document.Title, text = document.Text }
        : new { id, error = "not found" };
}
// Expected result:
//   fetch returns one document, or "not found."

// Your Turn:
// 1. Add a document. Add doc-005 about cross-lingual transfer to corpus, then ask a
//    multilingual-NLP question. Confirm the loop searches, fetches, and cites [doc-005].
// 2. Watch it iterate. Ask, "Contrast few-shot metric methods with efficient attention."
//    Inspect the printed search/fetch lines and final Tool calls history; you should see
//    multiple search/fetch rounds.
// 3. Tune the cap. Lower maxIterations to 1 and observe the loop stop early instead of
//    producing a complete report. Raise it again and watch the model dig deeper. This is
//    the cost/quality dial.

internal sealed record ResearchResult(
    string Findings,
    int Iterations,
    IReadOnlyList<string> ToolCalls);
