# M4 - Grounding and RAG with Foundry IQ

## Objective

Create an Azure AI Search index, upload an approved corpus, build a Foundry IQ knowledge
source/base, retrieve directly, and optionally ask a grounded MCP-backed model.

## Prerequisites

- `PROJECT_ENDPOINT`, `CHAT_MODEL`, and `SEARCH_ENDPOINT`
- Search Service Contributor
- Search Index Data Contributor
- `SEARCH_CONNECTION` for the optional agent MCP call

## Run

```powershell
dotnet run --project .\labs\04-grounding-rag -- --check
dotnet run --project .\labs\04-grounding-rag
```

Source: [`labs/04-grounding-rag/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/04-grounding-rag/Program.cs)

## Code flow

1. `SearchIndexClient` creates a three-field index.
2. `SearchClient` uploads workshop facts and runs direct retrieval.
3. Search SDK 12 creates a `SearchIndexKnowledgeSource` and `KnowledgeBase`.
4. If `SEARCH_CONNECTION` exists, the Responses API attaches the knowledge-base MCP
   endpoint and asks for a cited answer.

## Expected output

```text
Direct retrieval:
  [3] Foundry IQ: ...
Knowledge base 'foundry-workshop-facts-kb' is ready.
Grounded answer: ...
```

Without a connection, the program prints the exact setting needed for the agent step.

## Your Turn

Add a `url` field, upload another fact, and require a URL citation in the grounded answer.
Test a question absent from the corpus and require an explicit "I don't know."

## Cleanup and cost

Delete `foundry-workshop-facts`, its knowledge source, and its knowledge base after the
lab. Search service capacity and model calls can incur cost.

## Parity and preview caveats

Index, document, knowledge-source, and knowledge-base operations use the stable Search
12.0 SDK. Agent attachment uses the preview knowledge-base MCP route because no equally
stable high-level Foundry agent wrapper covers this path.
