# Microsoft Foundry Workshop for C#/.NET

Build an enterprise AI application from first inference through production quality
controls using **C#**, **.NET 10**, and **Microsoft Foundry**.

This fork uses **C#/.NET as the primary hands-on track**. The original Python notebooks
and generators remain in the repository as an optional [reference track](python-reference.md);
they are not required for the C# labs.

!!! tip "One solution, one shared bootstrap"
    Every lab is a runnable console project in `FoundryWorkshop.sln`. Configuration,
    identity, authenticated REST, SSE, and response parsing live in
    `FoundryWorkshop.Shared`.

## Learning journey

| Module | Outcome |
| --- | --- |
| [M1](modules/01-first-inference.md) | Responses, embeddings, streaming |
| [M2](modules/02-your-first-agent.md) | Versioned prompt agents |
| [M3](modules/03-tools-and-function-calling.md) | Hosted and local tools |
| [M4](modules/04-grounding-rag.md) | Search, Foundry IQ, citations |
| [M5](modules/05-mcp-tools.md) | Remote MCP |
| [M5b](modules/05b-work-iq.md) | Permission-aware Microsoft 365 context |
| [M6](modules/06-agent-memory.md) | Durable scoped memory |
| [M7](modules/07-multi-agent-orchestration.md) | Agent Framework routing |
| [M8](modules/08-deep-research.md) | Bounded cited research |
| [M9](modules/09-evaluation.md) | Offline metrics and LLM judging |
| [M10](modules/10-observability.md) | OpenTelemetry and continuous evaluation |
| [M11](modules/11-guardrails.md) | Prompt Shields, PII, blocklists |
| [M12](modules/12-red-teaming.md) | Automated adversarial probes |
| [M13](modules/13-human-in-loop-rest.md) | Approvals and raw REST/SSE |
| [M14](modules/14-fine-tuning.md) | Distillation data and SFT orchestration |
| [M15](modules/15-capstone.md) | Grounded, tool-using, measured support agent |

## Run the workshop

1. Complete [Setup](setup.md).
2. Read the [Concepts](concepts.md).
3. Start with [M1](modules/01-first-inference.md) and continue in order.
4. Use `--check` on Azure-dependent labs to inspect configuration without making calls.

The guides show expected output, but the source of truth is each linked `Program.cs`.

!!! warning "Setup is a prerequisite"
    Complete restore, build, `.env` configuration, Azure sign-in, role assignment, and
    the M1 smoke test before the facilitated session. Optional labs have additional
    service requirements listed in [Setup](setup.md).
