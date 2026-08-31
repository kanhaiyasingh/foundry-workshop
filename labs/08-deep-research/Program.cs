// M8 - Deep Research
//
// Goal: run an agentic research loop - a reasoning model that plans, searches a knowledge
// source, iterates, and returns a cited synthesis.
// You'll use: o3-deep-research through the Responses API with function tools, plus a chat
// model for the final report.
//
// A normal chat answer is one shot. Deep research is different: you pose a hard question,
// and a reasoning model (o3-deep-research) plans an investigation - it decides what to
// search, reads what it fetches, searches again to fill gaps, and only then concludes.
// A second, cheaper model turns those findings into a clean, cited report.
//
// The loop you'll build:
//
// question -> o3-deep-research ---> search(query)  -+
//              ^                   fetch(doc-id)    | iterate until the model
//              +------ tool results <--------------+ stops calling tools
//                                      |
//                                      v
//                         gpt-4.1-mini synthesizes a cited report
//
// One project, two model roles:
// The reference can deploy o3-deep-research in a separate region behind an APIM gateway.
// In this single-project C# setup both deployments use the same project endpoint. The
// Responses API keeps the research chain through previous_response_id. Deep-research models
// are preview and can run for minutes; the shared REST client uses a 600-second timeout.
//
// Full guide: docs/modules/08-deep-research.md
// Check: dotnet run --project .\labs\08-deep-research -- --check
// Run:   dotnet run --project .\labs\08-deep-research

using System.Text.Json;
using FoundryWorkshop.Shared;

// Notebook cell: print the current date and time.
Console.WriteLine($"Current date and time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff}");

return await LabHost.RunAsync(
    "M8 - Deep research",
    args,
    async context =>
    {
        // 1. Configure
        //
        // Two model deployments: the research model does the planning/tool-calling, and the
        // synthesis model writes the report. RESEARCH_MODEL defaults to o3-deep-research.
        var config = context.Config;
        var projectEndpoint = config.ProjectEndpoint;
        var researchModel = config.Get("RESEARCH_MODEL", "o3-deep-research");
        var synthesisModel = config.ChatModel;
        const int maxIterations = 6;

        Console.WriteLine($"Project   : {projectEndpoint}");
        Console.WriteLine($"Research  : {researchModel}");
        Console.WriteLine($"Synthesis : {synthesisModel}");

        // Expected output:
        //   Project   : https://<account>.services.ai.azure.com/api/projects/<project>
        //   Research  : o3-deep-research
        //   Synthesis : gpt-4.1-mini
        //
        // Splitting the roles is deliberate: reasoning models are powerful but slow and
        // pricey, so you let one think and a cheaper one write.

        // 2. Build the client
        //
        // This is the familiar project bootstrap. A deep-research call can run for minutes,
        // so the shared FoundryRestClient uses a ten-minute timeout. The same authenticated
        // project client performs fast synthesis with the CHAT_MODEL deployment.
        var openAiClient = context.Rest;
        var researchClient = context.Rest;
        Console.WriteLine("openai_client   : ready");
        Console.WriteLine("research_client : ready (timeout=600s)");

        // Expected output:
        //   openai_client   : ready
        //   research_client : ready (timeout=600s)
        //
        // A timeout during research usually means a short default HTTP timeout. The shared
        // C# REST client is configured for 600 seconds to prevent that.

        // 3. A knowledge source + two tools
        //
        // The model cannot search the open web here - it researches a knowledge source you
        // control. This tiny corpus of paper abstracts keeps the lab self-contained. The
        // search tool finds relevant documents and fetch reads one document in full.
        var corpus = new Dictionary<string, (string Title, string Text)>
        {
            ["doc-001"] = (
                "Prototypical Networks for Few-Shot Learning",
                "Prototypical networks learn a metric space where classification is " +
                "performed by computing distances to per-class prototypes. Strong on " +
                "miniImageNet 5-way 5-shot; simpler than matching networks."),
            ["doc-002"] = (
                "Model-Agnostic Meta-Learning (MAML)",
                "MAML learns an initialization that adapts to a new task in a few gradient " +
                "steps. Model-agnostic; competitive few-shot accuracy but costly " +
                "second-order gradients."),
            ["doc-003"] = (
                "Matching Networks for One Shot Learning",
                "Matching networks use attention over a labelled support set to classify " +
                "with one example per class; introduced episodic training."),
            ["doc-004"] = (
                "Linformer: Self-Attention with Linear Complexity",
                "Linformer projects keys and values to a low-rank form, reducing " +
                "self-attention from O(n^2) to O(n) in sequence length.")
        };

        object ToolSearch(string query)
        {
            var terms = query
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length > 3)
                .Select(word => word.ToLowerInvariant().Trim('.', ',', '?'))
                .ToHashSet();
            var hits = corpus
                .Where(item =>
                {
                    var documentTerms = $"{item.Value.Title} {item.Value.Text}"
                        .ToLowerInvariant()
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .ToHashSet();
                    return terms.Overlaps(documentTerms);
                })
                .Select(item => new
                {
                    id = item.Key,
                    title = item.Value.Title,
                    summary = item.Value.Text[..Math.Min(120, item.Value.Text.Length)] + "..."
                })
                .ToArray();
            var preview = query[..Math.Min(48, query.Length)].Replace("'", "\\'");
            Console.WriteLine($"   search('{preview}') -> {hits.Length} hit(s)");
            return new { results = hits };
        }

        object ToolFetch(string documentId)
        {
            Console.WriteLine($"   fetch('{documentId.Replace("'", "\\'")}')");
            return corpus.TryGetValue(documentId, out var document)
                ? new { id = documentId, title = document.Title, text = document.Text }
                : new { error = "not found" };
        }

        // The Python notebook uses the nested Chat Completions function-tool shape. The C#
        // port uses the equivalent flat Responses API function-tool shape.
        var tools = new object[]
        {
            new
            {
                type = "function",
                name = "search",
                description = "Search the research corpus; returns doc ids + summaries.",
                parameters = new
                {
                    type = "object",
                    properties = new { query = new { type = "string" } },
                    required = new[] { "query" }
                }
            },
            new
            {
                type = "function",
                name = "fetch",
                description = "Fetch the full text of one document by its id.",
                parameters = new
                {
                    type = "object",
                    properties = new { document_id = new { type = "string" } },
                    required = new[] { "document_id" }
                }
            }
        };
        Console.WriteLine($"Corpus: {corpus.Count} docs | tools: search, fetch");

        // Expected output:
        //   Corpus: 4 docs | tools: search, fetch
        //
        // Swap in a real knowledge base:
        // In production, ToolSearch and ToolFetch call a Foundry IQ knowledge base instead
        // of a dictionary - the same grounding built in M4. Read SEARCH_ENDPOINT from .env
        // and POST to the knowledge base retrieve API. Provisioning is covered in the
        // Platform docs; the research loop below is unchanged.

        // 4. The deep-research loop
        //
        // This is the heart of the lab. Give the research model the question and tool
        // schemas, then loop. Each turn the model either calls tools (C# executes them and
        // feeds results back) or stops, signaling that it has enough to conclude. Track
        // iterations and tool calls for observability, and cap the loop for safety.
        async Task<ResearchResult> RunDeepResearchAsync(string question)
        {
            const string instructions = """
                You are a deep-research assistant. Investigate the user's question using the
                search and fetch tools: search broadly, fetch the most relevant documents,
                and search again to fill gaps. Cite document ids like [doc-001]. If the
                corpus does not cover the question, say so explicitly rather than guessing.
                """;
            object input = question;
            string? previousResponseId = null;
            var responseIds = new List<string>();
            var toolCallsMade = new List<string>();
            string? finalIterationText = null;

            for (var iteration = 1; iteration <= maxIterations; iteration++)
            {
                Console.WriteLine($"Iteration {iteration}");
                var request = new Dictionary<string, object?>
                {
                    ["model"] = researchModel,
                    ["instructions"] = instructions,
                    ["input"] = input,
                    ["tools"] = tools
                };
                if (previousResponseId is not null)
                {
                    request["previous_response_id"] = previousResponseId;
                }

                using var response = await researchClient.CreateResponseAsync(request);
                previousResponseId = response.RootElement.GetProperty("id").GetString();
                if (previousResponseId is not null)
                {
                    responseIds.Add(previousResponseId);
                }

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
                        toolCallsMade,
                        responseIds);
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
                        "search" => ToolSearch(
                            arguments.RootElement.GetProperty("query").GetString() ??
                            string.Empty),
                        "fetch" => ToolFetch(
                            arguments.RootElement.GetProperty("document_id").GetString() ??
                            string.Empty),
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
                toolCallsMade,
                responseIds);
        }
        Console.WriteLine("run_deep_research() ready");

        // Expected output:
        //   run_deep_research() ready
        //
        // The loop ends when the research model returns no function calls - that is the
        // model signaling "I've gathered enough." maxIterations is the guardrail against a
        // model that keeps searching forever. responseIds is the C# Responses API equivalent
        // of the Python notebook's retained message chain.

        // 5. Run a question -> synthesize a cited report
        //
        // Pose a real question, run the loop, then hand the findings to the synthesis model
        // to format a clean report. Splitting research from writing keeps the expensive
        // reasoning focused and lets a fast model do the prose.
        const string question =
            "What are the main approaches to few-shot learning in the corpus, " +
            "and how do they differ? Cite the documents.";
        var research = await RunDeepResearchAsync(question);

        using var synthesis = await openAiClient.CreateResponseAsync(new
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
        Console.WriteLine(
            $"Tool calls : [{string.Join(", ", research.ToolCalls.Select(name => $"'{name}'"))}]\n");
        Console.WriteLine(report);

        // Expected output (the model-dependent wording and iteration count can vary):
        //   Iteration 1
        //      search('few-shot learning approaches') -> 3 hit(s)
        //   Iteration 2
        //      fetch('doc-001')
        //      fetch('doc-002')
        //      fetch('doc-003')
        //   Iteration 3
        //
        //   Iterations : 3
        //   Tool calls : ['search', 'fetch', 'fetch', 'fetch']
        //
        //   ## Few-Shot Learning Approaches in the Corpus
        //   - Metric-based approaches cite [doc-001] and [doc-003].
        //   - Optimization-based MAML cites [doc-002].
        //
        // Notice the shape: search -> fetch promising hits -> conclude -> cited report. The
        // model planned the investigation; you supplied only the tools.

        // 6. Respect the knowledge boundary
        //
        // A trustworthy researcher admits what it does not know. Ask something the corpus
        // cannot cover and watch the model decline rather than hallucinate. The system
        // instructions require it to say explicitly when the corpus falls short.
        var outOfScope = await RunDeepResearchAsync(
            "What are the latest breakthroughs in nuclear fusion energy?");

        Console.WriteLine($"\nIterations : {outOfScope.Iterations}");
        Console.WriteLine(
            $"Tool calls : [{string.Join(", ", outOfScope.ToolCalls.Select(name => $"'{name}'"))}]\n");
        Console.WriteLine(outOfScope.Findings);

        // Expected output:
        //   Iteration 1
        //      search('nuclear fusion energy breakthroughs') -> 0 hit(s)
        //   Iteration 2
        //
        //   Iterations : 2
        //   Tool calls : ['search']
        //
        //   The corpus does not contain documents on nuclear fusion energy - it covers
        //   few-shot learning and transformer efficiency in NLP. I cannot answer this from
        //   the available knowledge source.
        //
        // Grounding beats guessing:
        // The empty search result is the signal. With nothing to fetch, a well-prompted
        // research model reports the boundary instead of inventing citations. This honesty
        // is what M9 measures.

        // Your turn
        //
        // 1. Add a document. Add doc-005 about cross-lingual transfer to corpus, then ask a
        //    multilingual-NLP question. Confirm the loop searches, fetches, and cites it.
        // 2. Watch it iterate. Ask, "Contrast few-shot metric methods with efficient
        //    attention," then inspect research.ToolCalls. You should see multiple
        //    search/fetch rounds.
        // 3. Tune the cap. Lower maxIterations to 1 and observe the loop stop early with a
        //    thinner report; raise it and watch the model dig deeper. This is the
        //    cost/quality dial.
        //
        // You ran an agentic deep-research loop - plan, search, fetch, iterate - and turned
        // its findings into a cited report while honoring the knowledge boundary.
        // Next: measure answer quality, groundedness, and safety systematically in
        // M9 - Evaluation.
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL");

internal sealed record ResearchResult(
    string Findings,
    int Iterations,
    IReadOnlyList<string> ToolCalls,
    IReadOnlyList<string> ResponseIds);
