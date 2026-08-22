# C#/.NET troubleshooting

## Build and package guidance

The workshop targets `net8.0`; install the .NET 8 SDK even if a newer SDK is present.
Package versions are pinned centrally in `Directory.Packages.props`. Restore the whole
solution after a package change:

```powershell
dotnet nuget locals global-packages --list
dotnet restore .\FoundryWorkshop.sln
dotnet build .\FoundryWorkshop.sln --no-restore
```

Do not update one lab in isolation. `Azure.AI.Projects`, its agent/OpenAI extensions, and
the OpenAI client must remain compatible. Experimental API warnings are suppressed only
at the call sites that require them; all other warnings are build errors.

## Authentication and RBAC

- Run `az login` and `az account show`.
- Confirm `PROJECT_ENDPOINT` includes `/api/projects/<project>`.
- Assign **Foundry User** to the caller on the Foundry resource.
- Set `AZURE_TENANT_ID` for cross-tenant accounts.
- Set `AZURE_AUTH_MODE=cli` if the default credential chain selects an unintended source.

A `401` usually means token/audience/authentication. A `403` usually means RBAC, service
auth mode, firewall, or network isolation.

## Project endpoint versus account endpoint

Responses and agents use the project endpoint. Embeddings, classic deployment routes,
fine-tuning REST, and some evaluator surfaces use the account endpoint. The shared library
derives `AccountUri`; do not strip endpoint segments independently in each lab.

Symptoms of the wrong route include `404`, `Missed model deployment`, and
`API version not supported`.

## Azure AI Search and Foundry IQ

For keyless indexing:

1. Configure Search to accept Entra ID (`aadOrApiKey` or the current equivalent).
2. Assign Search Service Contributor and Search Index Data Contributor.
3. Assign the project managed identity read access when it retrieves directly.
4. For knowledge-base MCP, use the supported Foundry RemoteTool/knowledge-base connection;
   an ordinary Azure AI Search connection can return `401`.

## Cosmos DB firewall and versioned agents

Projects configured with customer-managed Cosmos DB may persist agent definitions there.
If version creation reports that Cosmos DB firewall settings blocked a Foundry backend
address, adding the participant's workstation IP does not fix it. Configure a supported
private endpoint/managed network path or permit the required service access.

## Work IQ

Work IQ needs tenant admin consent, a licensed user, and delegated/OBO authentication.
A raw unauthenticated MCP URL returns `401`, and a local stdio process cannot be reached
by Foundry. Use the currently supported Work IQ connection/tool flow for the tenant. The
M5b lab fails with a precise configuration message rather than pretending permission-aware
retrieval succeeded.

## Application Insights

Use the full connection string. M10 records model name, response id, length, and custom
workshop tags, but does not record prompt or response content. Verify local clock, outbound
ingestion access, and the selected Application Insights resource when traces do not appear.

## Preview REST failures

Memory, online evaluation rules, guardrail ARM shapes, fine-tuning, and Work IQ evolve.
The guides state their API versions and payload strategy. On `400`, inspect the complete
service response printed by the shared HTTP wrapper and compare it with the current
Microsoft Foundry REST documentation for your region before changing code.
