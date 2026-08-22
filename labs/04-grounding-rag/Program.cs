using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using FoundryWorkshop.Shared;

return await LabHost.RunAsync(
    "M4 - Grounding and RAG with Foundry IQ",
    args,
    async context =>
    {
        const string indexName = "foundry-workshop-facts";
        const string knowledgeSourceName = "foundry-workshop-facts-source";
        const string knowledgeBaseName = "foundry-workshop-facts-kb";
        var searchEndpoint = context.Config.RequireUri(
            "SEARCH_ENDPOINT",
            "Grant Search Service Contributor and Search Index Data Contributor first.");

        var indexClient = new SearchIndexClient(searchEndpoint, context.Credential);
        var index = new SearchIndex(indexName)
        {
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
                new SearchableField("title"),
                new SearchableField("content")
            }
        };
        await indexClient.CreateOrUpdateIndexAsync(index);

        var documents = new[]
        {
            new SearchDocument
            {
                ["id"] = "1",
                ["title"] = "Foundry projects",
                ["content"] = "A Microsoft Foundry project scopes agents, evaluations, knowledge, and traces."
            },
            new SearchDocument
            {
                ["id"] = "2",
                ["title"] = "Keyless authentication",
                ["content"] = "DefaultAzureCredential uses developer identity locally and managed identity in Azure."
            },
            new SearchDocument
            {
                ["id"] = "3",
                ["title"] = "Foundry IQ",
                ["content"] = "Foundry IQ exposes an Azure AI Search knowledge base as an MCP retrieval tool with citations."
            }
        };
        var searchClient = indexClient.GetSearchClient(indexName);
        await searchClient.UploadDocumentsAsync(documents);

        Console.WriteLine("Direct retrieval:");
        var search = await searchClient.SearchAsync<SearchDocument>(
            "How does Foundry ground agents?",
            new SearchOptions { Size = 3 });
        await foreach (var result in search.Value.GetResultsAsync())
        {
            Console.WriteLine(
                $"  [{result.Document["id"]}] {result.Document["title"]}: {result.Document["content"]}");
        }

        var sourceParameters = new SearchIndexKnowledgeSourceParameters(indexName);
        sourceParameters.SearchFields.Add(new SearchIndexFieldReference("content"));
        sourceParameters.SourceDataFields.Add(new SearchIndexFieldReference("title"));
        var source = new SearchIndexKnowledgeSource(knowledgeSourceName, sourceParameters)
        {
            Description = "Workshop facts indexed by the C# M4 lab."
        };
        await indexClient.CreateOrUpdateKnowledgeSourceAsync(source);

        var knowledgeBase = new KnowledgeBase(
            knowledgeBaseName,
            [new KnowledgeSourceReference(knowledgeSourceName)])
        {
            Description = "C# workshop knowledge base for grounded agent answers."
        };
        await indexClient.CreateOrUpdateKnowledgeBaseAsync(knowledgeBase);
        Console.WriteLine($"Knowledge base '{knowledgeBaseName}' is ready.");

        if (context.Config.IsConfigured("SEARCH_CONNECTION"))
        {
            var mcpUrl = $"{searchEndpoint.ToString().TrimEnd('/')}/knowledgebases/{knowledgeBaseName}" +
                         "/mcp?api-version=2025-11-01-preview";
            using var response = await context.Rest.CreateResponseAsync(new
            {
                model = context.Config.ChatModel,
                input = "How does Foundry ground an agent? Cite the retrieved title.",
                tools = new object[]
                {
                    new
                    {
                        type = "mcp",
                        server_label = "knowledge_base",
                        server_url = mcpUrl,
                        require_approval = "never",
                        project_connection_id = context.Config.Require("SEARCH_CONNECTION")
                    }
                }
            });
            Console.WriteLine($"Grounded answer: {JsonHelpers.GetOutputText(response.RootElement)}");
        }
        else
        {
            Console.WriteLine(
                "Set SEARCH_CONNECTION to the Foundry RemoteTool connection name to run the grounded agent call.");
        }
    },
    "PROJECT_ENDPOINT",
    "CHAT_MODEL",
    "SEARCH_ENDPOINT");
