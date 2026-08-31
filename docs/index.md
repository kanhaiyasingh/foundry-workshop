# Microsoft Foundry: End-to-End Workshop

Build an enterprise AI application from first inference through production quality
controls using **Python**, **C#/.NET 10**, and **Microsoft Foundry**.

## Choose a language track

| Track | Choose it when | Required locally | Setup | First lab |
|---|---|---|---|---|
| Python | You prefer interactive cells and inline notebook output | Python 3.11-3.13 and Jupyter | [Python setup](setup.md) | [Track overview](python-reference.md) and [M1 notebook](modules/01-first-inference.ipynb) |
| C#/.NET | You prefer console applications and strongly typed .NET code | .NET 10 SDK and a supported C# IDE | [C# setup](csharp-setup.md) | [M1 C# guide](modules/01-first-inference.md) |

Both tracks use the same Foundry project, model deployments, and overall learning
journey. Neither track depends on the other. Complete one setup guide and follow that
track through M1-M15; use the other implementation only for comparison.

## Learning journey

| Module | Python | C#/.NET | Outcome |
|---|---|---|---|
| M1 | [Notebook](modules/01-first-inference.ipynb) | [Guide](modules/01-first-inference.md) | Responses, embeddings, streaming |
| M2 | [Notebook](modules/02-your-first-agent.ipynb) | [Guide](modules/02-your-first-agent.md) | Versioned prompt agents |
| M3 | [Notebook](modules/03-tools-and-function-calling.ipynb) | [Guide](modules/03-tools-and-function-calling.md) | Hosted and local tools |
| M4 | [Notebook](modules/04-grounding-rag-foundry-iq.ipynb) | [Guide](modules/04-grounding-rag.md) | Search, Foundry IQ, citations |
| M5 | [Notebook](modules/05-mcp-tools.ipynb) | [Guide](modules/05-mcp-tools.md) | Remote MCP |
| M5b | [Notebook](modules/05b-work-iq.ipynb) | [Guide](modules/05b-work-iq.md) | Microsoft 365 context |
| M6 | [Notebook](modules/06-agent-memory.ipynb) | [Guide](modules/06-agent-memory.md) | Durable scoped memory |
| M7 | [Notebook](modules/07-multi-agent-orchestration.ipynb) | [Guide](modules/07-multi-agent-orchestration.md) | Multi-agent routing |
| M8 | [Notebook](modules/08-deep-research.ipynb) | [Guide](modules/08-deep-research.md) | Bounded cited research |
| M9 | [Notebook](modules/09-evaluation.ipynb) | [Guide](modules/09-evaluation.md) | Evaluation and judging |
| M10 | [Notebook](modules/10-observability-tracing.ipynb) | [Guide](modules/10-observability.md) | Tracing and monitoring |
| M11 | [Notebook](modules/11-guardrails.ipynb) | [Guide](modules/11-guardrails.md) | Guardrails |
| M12 | [Notebook](modules/12-red-teaming.ipynb) | [Guide](modules/12-red-teaming.md) | Adversarial testing |
| M13 | [Notebook](modules/13-human-in-the-loop-and-rest.ipynb) | [Guide](modules/13-human-in-loop-rest.md) | Approval and REST |
| M14 | [Notebook](modules/14-fine-tuning-distillation.ipynb) | [Guide](modules/14-fine-tuning.md) | Fine-tuning |
| M15 | [Notebook](modules/15-capstone.ipynb) | [Guide](modules/15-capstone.md) | Capstone |

## Run the workshop

1. Choose Python or C#/.NET using the table above.
2. Complete only the setup page for that track.
3. Read the matching [Python concepts](concepts.md) or
   [C# concepts](csharp-concepts.md).
4. Start at M1 in the same track and work through M15 in order.

!!! warning "Use unique resource names"
    Both tracks can create persistent Azure resources. Do not run the same
    resource-creating lab in both languages against one project with identical names.
