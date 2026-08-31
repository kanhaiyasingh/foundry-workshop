# C#/.NET setup

Complete this page **before the workshop**. The C# labs do not require Python or Jupyter.
If you want to run the preserved notebooks instead, follow the separate
[Python reference-track setup](python-reference.md).

## 1. Install prerequisites

Windows participants need:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure CLI 2.60 or later](https://learn.microsoft.com/cli/azure/install-azure-cli)
- [Git](https://git-scm.com/downloads)
- VS Code with the C# Dev Kit, or Visual Studio 2026, is recommended
- An Azure subscription allowed to use Microsoft Foundry

```powershell
dotnet --list-sdks
az version
git --version
```

Confirm that `dotnet --list-sdks` includes a `10.0.x` SDK. Installing only the .NET 10
runtime is not sufficient. macOS/Linux uses the same validation commands in a shell.

## 2. Get the code and build it

```powershell
git clone https://github.com/malaika2820/foundry-workshop-c-.git
Set-Location foundry-workshop-c-
dotnet restore .\FoundryWorkshop.sln
dotnet build .\FoundryWorkshop.sln --no-restore
```

Cross-platform:

```bash
git clone https://github.com/malaika2820/foundry-workshop-c-.git
cd foundry-workshop-c-
dotnet restore FoundryWorkshop.sln
dotnet build FoundryWorkshop.sln --no-restore
```

The build does not contact Azure or require credentials. It should complete with no
errors. Package versions are pinned centrally in `Directory.Packages.props`; do not
upgrade individual lab projects independently.

## 3. Create or obtain Foundry resources

Create a Foundry project and copy the **project endpoint**, including
`/api/projects/<project>`. Deploy:

- `gpt-4.1-mini` or another Responses/tool-capable chat deployment;
- `text-embedding-3-large` for M1/M6;
- an optional research-capable deployment for M8;
- an optional fine-tunable base model for M14.

Assign your user the **Foundry User** role on the Foundry resource. Labs that create
management-plane resources need Contributor-equivalent rights called out in their guides.

If a facilitator provides a prepared project, use its endpoint and deployment names
instead of creating duplicate resources.

## 4. Sign in to Azure

```powershell
az login
az account set --subscription "<subscription-id>"
az account show --query "{name:name,id:id,tenantId:tenantId}" --output table
```

Use `az login --tenant "<tenant-id>"` when the subscription is in a tenant other than
your default tenant.

## 5. Configure `.env`

```powershell
Copy-Item .env.example .env
notepad .env
```

Set at least:

```ini
AZURE_SUBSCRIPTION_ID=<subscription-guid>
PROJECT_ENDPOINT=https://<account>.services.ai.azure.com/api/projects/<project>
CHAT_MODEL=gpt-4.1-mini
EMBEDDING_MODEL=text-embedding-3-large
```

DotNetEnv walks parent directories, so each console project finds the repository `.env`.
`DefaultAzureCredential` is the default. Set `AZURE_AUTH_MODE=cli` to force
`AzureCliCredential`; `AZURE_TENANT_ID` selects a tenant when required.

Use deployment names, not base model IDs, for model values. Never commit `.env`. Some
optional MCP endpoints and the Application Insights connection string contain access
material.

## 6. Check and smoke-test

```powershell
dotnet run --project .\labs\01-first-inference -- --check
dotnet run --project .\labs\01-first-inference
```

Expected first line from the service:

```text
Response: Foundry is ready.
```

`--check` validates configuration without making model calls. The full command validates
authentication, project routing, the chat deployment, the embedding deployment, and
streaming. Resolve any failure before moving to M2.

## Service-specific access

| Lab | Additional requirement |
| --- | --- |
| M4 | Search Service Contributor + Search Index Data Contributor; Search must accept Entra auth |
| M5 | Publicly reachable MCP endpoint; optional Foundry RemoteTool connection |
| M5b | Microsoft 365 license, Work IQ admin consent, and a supported authenticated remote endpoint |
| M6 | Memory preview availability in the selected project/region |
| M7 | No extra Azure resource; package versions must remain centrally pinned |
| M8 | Optional research-model deployment when using the research-model path |
| M9 | Chat deployment for the optional LLM judge |
| M10 | Application Insights connection string |
| M11 | Azure resource group plus Cognitive Services Contributor/Contributor rights |
| M12 | Chat deployment and permission to run the selected adversarial probes |
| M14 | Fine-tuning availability, quota, a supported model, and file/job permissions |

### Search and Foundry IQ

Set `SEARCH_ENDPOINT`. The signed-in user needs Search Service Contributor to create the
index/knowledge base and Search Index Data Contributor to upload documents. For the agent
MCP step, configure a Foundry RemoteTool connection for the knowledge-base MCP endpoint
and set `SEARCH_CONNECTION`.

### MCP

M5 can use the public Microsoft Learn MCP endpoint:

```ini
MCP_SERVER_URL=https://learn.microsoft.com/api/mcp
MCP_SERVER_LABEL=microsoft_learn
```

For private/custom servers, Foundry must be able to reach the endpoint server-side.

### Work IQ

Work IQ is delegated and permission-aware. Tenant admin consent and the supported Work IQ
application/connection flow are mandatory. A local stdio Work IQ process is not reachable
by a cloud Foundry response. See [M5b](modules/05b-work-iq.md).

## Common commands

```powershell
# Validate the complete C# source tree after any change
dotnet build .\FoundryWorkshop.sln

# Validate one lab's configuration without calling Azure
dotnet run --project .\labs\04-grounding-rag -- --check

# Run a lab
dotnet run --project .\labs\04-grounding-rag
```

Generated output is written under `artifacts/` and is ignored by Git. Consult
[Troubleshooting](troubleshooting.md) for endpoint, RBAC, Search, Work IQ, preview API,
and telemetry failures.

Continue to [Concepts](concepts.md), then [M1](modules/01-first-inference.md).
