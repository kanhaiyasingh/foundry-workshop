# Microsoft Foundry Workshop for C#/.NET

An end-to-end, participant-ready workshop for building enterprise AI applications and
agents on **Microsoft Foundry** with **C# and .NET 10**.

The journey starts with one Responses API call and ends with a grounded, tool-using,
evaluated, observable support agent. All numbered labs M1-M15, including M5b, are
runnable console applications. Azure-dependent calls need configured resources; the
solution restores and builds without Azure credentials.

## Language tracks

The **C#/.NET track is the primary participant experience in this fork**:

- `labs/` contains one runnable .NET console project for every scenario.
- `src/FoundryWorkshop.Shared/` contains common configuration, authentication, REST,
  streaming, and JSON helpers.
- `docs/modules/*.md` contains the C# lab guides.

The original Python workshop has been preserved for comparison and for participants who
prefer notebooks:

- `docs/modules/*.ipynb` contains the original Jupyter labs.
- `scripts/gen_*.py` contains their notebook generators.
- `pyproject.toml` contains the Python dependencies.

The two tracks use the same Foundry project and environment-variable contract. Do not run
both versions of a resource-creating lab against the same shared project at the same time
unless the facilitator has assigned unique resource names.

## Architecture

```text
FoundryWorkshop.sln
├── src/FoundryWorkshop.Shared/       configuration, identity, REST, SSE, JSON
├── labs/01-first-inference/          one .NET 10 console project per lab
│   └── ...
├── labs/15-capstone/
└── docs/                             MkDocs participant guides
```

Package versions are centrally pinned in `Directory.Packages.props`. The shared library
loads `.env` with DotNetEnv, uses `DefaultAzureCredential` by default
(`AzureCliCredential` with `AZURE_AUTH_MODE=cli`), and centralizes the Foundry project,
account, ARM, JSON, and SSE patterns.

## Labs

| Lab | Scenario |
| --- | --- |
| M1 | Responses, embeddings, and SSE streaming |
| M2 | Versioned Foundry prompt agent |
| M3 | Function calling and optional Code Interpreter |
| M4 | Azure AI Search and a Foundry IQ knowledge base |
| M5 | Remote MCP tools |
| M5b | Permission-aware Work IQ |
| M6 | Preview Agent Memory REST API |
| M7 | Microsoft Agent Framework router and specialists |
| M8 | Bounded deep-research loop with citations |
| M9 | Deterministic evaluation and optional LLM judge |
| M10 | OpenTelemetry, Azure Monitor, and continuous evaluation |
| M11 | Prompt Shields, PII, custom blocklists, and RAI policy ARM REST |
| M12 | C# adversarial scan with baseline and encoded attacks |
| M13 | Human approval, raw REST, multi-turn, and SSE |
| M14 | Distillation data, SFT validation, comparison, and fine-tuning REST |
| M15 | Grounded support capstone with tools, evaluation, and tracing |

## Start

```powershell
git clone https://github.com/malaika2820/foundry-workshop-c-.git
Set-Location foundry-workshop-c-
Copy-Item .env.example .env
az login
dotnet restore .\FoundryWorkshop.sln
dotnet build .\FoundryWorkshop.sln --no-restore
dotnet run --project .\labs\01-first-inference -- --check
dotnet run --project .\labs\01-first-inference
```

On macOS/Linux, replace backslashes with `/` and use `cp .env.example .env`.

Read the complete [setup guide](docs/setup.md) before the first Azure call. Each lab has
a detailed guide under [`docs/modules`](docs/modules), including prerequisites,
expected output, exercises, cleanup/cost notes, and preview caveats.

Use the [Python reference-track guide](docs/python-reference.md) only if you need to run
the preserved notebooks. Python is not required for the C# labs.

## Offline validation

```powershell
dotnet run --project .\labs\09-evaluation
dotnet run --project .\labs\12-red-teaming -- --offline
dotnet run --project .\labs\14-fine-tuning
```

Generated lab output goes under `artifacts/`, which is git-ignored.

## Documentation

If MkDocs is already available:

```powershell
mkdocs serve
```

The GitHub Pages workflow installs only the MkDocs dependencies; Python is not part of
the workshop runtime.

## License

MIT
