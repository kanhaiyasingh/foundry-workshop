# Original Python notebook reference

The C#/.NET labs are the primary participant track in this fork. The original Python
workshop remains available for comparison and for participants who prefer Jupyter.

## What is preserved

- `docs/modules/01-first-inference.ipynb` through `15-capstone.ipynb`, including M5b
- `scripts/gen_*.py`, which generate the notebooks
- `scripts/nbbuild.py` and the diagram generator
- `pyproject.toml`, which defines the Python runtime and documentation dependencies

The MkDocs site links to the C# guides rather than rendering the notebooks. Open the
notebooks directly in VS Code or Jupyter.

## Python prerequisites

- Python 3.11-3.13; Python 3.12 is recommended
- Azure CLI 2.60 or later
- VS Code with the Python and Jupyter extensions, or another Jupyter client
- The same Azure resources, roles, deployment names, and `.env` values used by the C#
  track

## Install

=== "PowerShell"

    ```powershell
    py -3.12 -m venv .venv
    .\.venv\Scripts\Activate.ps1
    python -m pip install --upgrade pip
    pip install -e .
    python -m ipykernel install --user --name foundry-workshop `
      --display-name "Microsoft Foundry Workshop (Python)"
    ```

=== "macOS/Linux"

    ```bash
    python3.12 -m venv .venv
    source .venv/bin/activate
    python -m pip install --upgrade pip
    pip install -e .
    python -m ipykernel install --user --name foundry-workshop \
      --display-name "Microsoft Foundry Workshop (Python)"
    ```

Copy and configure the shared environment file if it does not already exist:

```powershell
Copy-Item .env.example .env
az login
```

Open `docs/modules/01-first-inference.ipynb`, select the
**Microsoft Foundry Workshop (Python)** kernel, and run cells from top to bottom.

## Important differences

- The C# solution uses centrally pinned NuGet packages; the notebooks use dependencies
  from `pyproject.toml`.
- Preview SDK shapes may differ between languages. Follow the code in the selected track
  rather than translating calls during the session.
- Both tracks can create persistent agents, indexes, policies, connections, and other
  resources. Facilitators should assign unique project/resource names if tracks run in
  parallel.
- The C# troubleshooting guide covers shared service-level issues. The original
  `docs/modules/debug.md` retains Python-specific investigation notes.

Do not commit `.env`, notebook outputs containing sensitive data, or generated artifacts.
