// M8 objective: let a reasoning model search and fetch a bounded corpus, then cite a report.
// Prerequisites: PROJECT_ENDPOINT and a tool-capable RESEARCH_MODEL deployment.
// Check: dotnet run --project .\labs\08-deep-research -- --check
// Run:   dotnet run --project .\labs\08-deep-research
// Expect: model-dependent search/fetch iterations ending in a report with [doc-id] citations.

using System.Text.Json;
using FoundryWorkshop.Shared;

// Step 1: Define the only documents the research model is allowed to use.
var corpus = new Dictionary<string, (string Title, string Text)>
{
    ["doc-001"] = (
        "Prototypical Networks for Few-Shot Learning",
        "Prototypical networks classify by distance to per-class prototypes in a learned metric space."),
    ["doc-002"] = (
        "Model-Agnostic Meta-Learning",
        "MAML learns an initialization that adapts to a new task in a few gradient steps but uses costly gradients."),
    ["doc-003"] = (
        "Matching Networks for One Shot Learning",
        "Matching networks use attention over a labelled support set and episodic training."),
    ["doc-004"] = (
        "Linformer",
        "Linformer reduces self-attention complexity through low-rank projections.")
};

// Step 2: Configure the bounded loop and expose search/fetch schemas to the model.
return await LabHost.RunAsync(
    "M8 - Deep research",
    args,
    async context =>
    {
        var researchModel = context.Config.Require(
            "RESEARCH_MODEL",
            "Deploy a reasoning/deep-research capable model, or set RESEARCH_MODEL to a tool-capable deployment.");
        const int maxIterations = 6;
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

        // Step 3: Ask the fixed comparison question and retain response state across iterations.
        object input =
            "Compare the few-shot learning approaches in this corpus. Search broadly, fetch relevant papers, " +
            "and cite document ids. Say explicitly when the corpus cannot support a claim.";
        string? previousResponseId = null;

        // Step 4: Execute proposed calls in C# until the model concludes or reaches the safety cap.
        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            using var response = await context.Rest.CreateResponseAsync(new
            {
                model = researchModel,
                instructions = """
                    You are a bounded deep-research agent. Use search and fetch repeatedly before concluding.
                    Cite sources as [doc-id] and never use facts outside the supplied corpus.
                    """,
                input,
                previous_response_id = previousResponseId,
                tools
            });
            previousResponseId = response.RootElement.GetProperty("id").GetString();
            var calls = JsonHelpers.GetFunctionCalls(response.RootElement).ToArray();
            Console.WriteLine($"Iteration {iteration}: {calls.Length} tool call(s)");
            if (calls.Length == 0)
            {
                // The final wording and path vary; success requires supported [doc-id] citations.
                Console.WriteLine(JsonHelpers.GetOutputText(response.RootElement));
                return;
            }

            var outputs = new List<object>();
            foreach (var call in calls)
            {
                var callId = call.GetProperty("call_id").GetString();
                var name = call.GetProperty("name").GetString();
                using var arguments = JsonDocument.Parse(call.GetProperty("arguments").GetString() ?? "{}");
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
                outputs.Add(new
                {
                    type = "function_call_output",
                    call_id = callId,
                    output = JsonSerializer.Serialize(result)
                });
            }

            input = outputs;
        }

        throw new InvalidOperationException(
            $"Research exceeded the safety limit of {maxIterations} iterations.");
    },
    "PROJECT_ENDPOINT",
    "RESEARCH_MODEL");

// Step 5: Search returns only matching corpus records; an empty result marks the boundary.
static object SearchCorpus(
    string query,
    IReadOnlyDictionary<string, (string Title, string Text)> corpus)
{
    var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(term => term.Trim(' ', '.', ',', '?').ToLowerInvariant())
        .Where(term => term.Length > 3)
        .ToHashSet();
    return corpus
        .Where(item => terms.Any(term =>
            $"{item.Value.Title} {item.Value.Text}".Contains(term, StringComparison.OrdinalIgnoreCase)))
        .Select(item => new
        {
            id = item.Key,
            title = item.Value.Title,
            summary = item.Value.Text
        })
        .ToArray();
}

// Step 6: Fetch returns one approved document or an explicit not-found result.
static object FetchDocument(
    string id,
    IReadOnlyDictionary<string, (string Title, string Text)> corpus) =>
    corpus.TryGetValue(id, out var document)
        ? new { id, title = document.Title, text = document.Text }
        : new { id, error = "Document not found." };

// Your Turn: add doc-005, ask a cross-topic question, then lower maxIterations and test
// an out-of-corpus prompt to observe the cost/quality and knowledge-boundary behavior.
