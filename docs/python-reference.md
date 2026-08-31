# Python notebook track

The Python notebooks are a complete participant track alongside the C#/.NET projects.

## What is preserved

- `docs/modules/01-first-inference.ipynb` through `15-capstone.ipynb`, including M5b
- `scripts/gen_*.py`, which generate the notebooks
- `scripts/nbbuild.py` and the diagram generator
- `pyproject.toml`, which defines the Python runtime and documentation dependencies

The MkDocs site renders the notebooks from their saved outputs and provides a download
link. You can also open them directly in VS Code or Jupyter.

## Start here

1. Complete the single authoritative [Python setup guide](setup.md).
2. Read the [Python concepts](concepts.md).
3. Open `docs/modules/01-first-inference.ipynb`.
4. Select the **Microsoft Foundry: End-to-End Workshop** kernel.
5. Continue through the canonical notebooks in module order.

The canonical notebooks use names such as `01-first-inference.ipynb`. Files ending
in `-yourturn.ipynb`, where present, are optional solved exercise companions. They
are not a separate workshop path and are not required to continue to the next module.

## Important differences

- The C# solution uses centrally pinned NuGet packages; the notebooks use dependencies
  from `pyproject.toml`.
- The tracks share core Foundry settings, but each has additional track-specific
  variables. Python participants should use `.env.example`; C# participants should
  use `.env.csharp.example`.
- Preview SDK shapes may differ between languages. Follow the code in the selected track
  rather than translating calls during the session.
- Both tracks can create persistent agents, indexes, policies, connections, and other
  resources. Use unique project/resource names if tracks run in parallel.
- The C# troubleshooting guide covers shared service-level issues. The original
  `docs/modules/debug.md` retains Python-specific investigation notes.

Do not commit `.env`, notebook outputs containing sensitive data, or generated artifacts.
