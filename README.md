# Microsoft Foundry: End-to-End Workshop

A hands-on, end-to-end coding workshop for building enterprise AI agents and apps
on **Microsoft Foundry (Azure AI Foundry)** — Azure's unified platform for models,
agents, knowledge, evaluation, and observability.

The labs are runnable **Jupyter notebooks**. You run each one against your own
Foundry project, starting from a single `az login` and progressing to a grounded,
tool-using, evaluated, observable agent.

> **New here? Follow the [Setup](#setup) section below in order.** It takes about
> 15–20 minutes and ends with a smoke test that proves your environment works
> before you open the first notebook.

---

## What you'll build

| #   | Module | You'll learn |
| --- | ------ | ------------ |
| 1   | First inference | Chat, embeddings, streaming, the Responses API |
| 2   | Your first agent | Versioned prompt agents |
| 3   | Tools & function calling | Code Interpreter + custom tools |
| 4   | Grounding / RAG | Azure AI Search knowledge bases (Foundry IQ) |
| 5   | MCP tools | Connect an agent to a Model Context Protocol server |
| 5b  | Work IQ | Ground an agent in live Microsoft 365 work context |
| 6   | Agent memory | Cross-turn context |
| 7   | Multi-agent orchestration | Router + specialists |
| 8   | Deep research | Agentic research loops with cited synthesis |
| 9   | Evaluation | Quality, agent, and custom evaluators |
| 10  | Observability | OpenTelemetry tracing + continuous evaluation |
| 11  | Guardrails | Prompt Shields, PII, custom blocklists |
| 12  | Red teaming | Automated adversarial scans |
| 13  | Human-in-the-loop & REST | Approvals + raw REST invocation |
| 14  | Fine-tuning | Knowledge distillation to a small model |
| 15  | Capstone | Combine it all, plus where to go next |

> 💡 You can **read** every lab without Azure — each one shows its expected output
> in prose. You only need a Foundry project when you want to **run** the cells.

---

## Prerequisites

Before you start, make sure you have:

- An **Azure subscription** with permission to create resources.
- **Python 3.11–3.13** (3.12 recommended).
- **Azure CLI** v2.60+ — [install](https://learn.microsoft.com/cli/azure/install-azure-cli),
  then add the extension: `az extension add -n cognitiveservices`.
- **[uv](https://docs.astral.sh/uv/getting-started/installation/)** (recommended) — or plain `python -m venv`.
- **VS Code** with the Python + Jupyter extensions (any Jupyter client works).

---

## Setup

Do these steps in order.

### 1. Get the code

```bash
git clone https://github.com/kanhaiyasingh/foundry-workshop.git
cd foundry-workshop
```

### 2. Create a virtual environment and install

=== "uv (recommended)"

    ```bash
    uv venv --python 3.12 .venv
    source .venv/bin/activate          # Windows: .venv\Scripts\activate
    uv pip install -e .                # runtime SDKs to RUN the labs
    uv pip install -e ".[docs]"        # optional: to preview the docs site
    ```

=== "pip"

    ```bash
    python -m venv .venv
    source .venv/bin/activate          # Windows: .venv\Scripts\activate
    pip install -e .                   # runtime SDKs to RUN the labs
    pip install -e ".[docs]"           # optional: to preview the docs site
    ```

> ⚠️ **Pre-release SDKs.** Foundry's SDKs move fast and are pinned in
> `pyproject.toml`. If an import breaks after an upstream release, pin back to the
> versions there.

### 3. Register the Jupyter kernel

```bash
python -m ipykernel install --user --name foundry-workshop \
  --display-name "Microsoft Foundry: End-to-End Workshop"
```

When you open a notebook, select the **Microsoft Foundry: End-to-End Workshop** kernel.

### 4. Create a Foundry project and deploy models

In the **Microsoft Foundry** portal:

1. **Create a project** (this also creates its Foundry account). Copy the
   **project endpoint** — it looks like
   `https://<account>.services.ai.azure.com/api/projects/<project>`.
2. In **Build → Models**, deploy:
   - **`gpt-4.1-mini`** — chat model, used by every lab
   - **`text-embedding-3-large`** — embeddings (M4, M7)
   - *(optional)* **`o4-mini`** — reasoning (M1 notes, M13)
   - *(optional)* **`o3-deep-research`** (M8)
3. Grant your signed-in identity the **Foundry User** role (role ID
   `53ca6127-db72-4b80-b1b0-d745d6d5456d`) on the Foundry resource. Don't use
   *Azure AI Developer* — it's scoped to hubs, not the projects these labs use.

Then sign in locally:

```bash
az login
```

> 📋 For the full per-service access checklist (Search, Storage, Cosmos) and the
> optional **MCP (M5)** / **Work IQ (M5b)** prerequisites, see
> **[docs/setup.md](docs/setup.md)**.

### 5. Configure your environment

Copy the template and fill in your values. **Every lab loads these exact variable
names**, so set them once:

```bash
cp .env.example .env     # Windows: Copy-Item .env.example .env
```

At minimum, set these in `.env`:

```ini
AZURE_SUBSCRIPTION_ID=<your-subscription-id>
PROJECT_ENDPOINT=https://<account>.services.ai.azure.com/api/projects/<project>
CHAT_MODEL=gpt-4.1-mini
EMBEDDING_MODEL=text-embedding-3-large
```

The remaining variables in `.env.example` are optional and only needed by specific
labs (each is commented with the module that uses it). Authentication is keyless —
it uses your `az login` identity, so **no model keys go in notebooks or `.env`**.

> 🔒 **Never commit `.env`.** It's already git-ignored — keep your subscription id,
> endpoints, and any keys out of version control.

### 6. Smoke test

Confirm your environment can reach your project. Save this as `smoke_test.py` and
run `python smoke_test.py`:

```python
import os
from dotenv import load_dotenv
from azure.identity import DefaultAzureCredential
from azure.ai.projects import AIProjectClient

load_dotenv()
client = AIProjectClient(
    endpoint=os.environ["PROJECT_ENDPOINT"],
    credential=DefaultAzureCredential(),
).get_openai_client()

resp = client.responses.create(
    model=os.environ.get("CHAT_MODEL", "gpt-4.1-mini"),
    input="Reply with exactly: Foundry is ready.",
)
print(resp.output_text)
```

Expected output:

```
Foundry is ready.
```

If you see that line, you're set. A `401`/`403` means your identity is missing the
**Foundry User** role; a `DefaultAzureCredential` error usually means you need to
run `az login`.

---

## Run the labs

1. Open **`docs/modules/01-first-inference.ipynb`** in VS Code (or `jupyter lab`).
2. Select the **Microsoft Foundry: End-to-End Workshop** kernel.
3. Run the cells top to bottom, then move on to `02-…`, `03-…`, and so on.

Work through the modules in order — each builds on the patterns of the last.

> 📖 Prefer to read first? Preview the whole workshop as a site with
> `mkdocs serve` (requires the optional `".[docs]"` install) and open
> <http://127.0.0.1:8000>.

---

## Project layout

```
docs/modules/     The lab notebooks (01-… through 15-…) — what you run
docs/setup.md     Full setup reference (RBAC, optional-lab prerequisites)
scripts/          Notebook generators (for maintainers — see below)
.env.example      Template for your local .env
pyproject.toml    Dependencies and pins
```

---

## For maintainers

Lab notebooks are **generated** programmatically — never hand-edited as JSON. Each
module has a generator under `scripts/gen_<id>.py` that builds its notebook via
`scripts/nbbuild.py`. To regenerate a notebook and rebuild the site:

```bash
PYTHONPATH=scripts python scripts/gen_01_first_inference.py
mkdocs build --strict
```

---

## License

MIT
