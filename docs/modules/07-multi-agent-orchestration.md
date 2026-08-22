# M7 - Multi-agent orchestration

## Objective

Build a router and focused specialists with Microsoft Agent Framework, then dispatch
policy and technical questions to the correct agent.

## Prerequisites

- `PROJECT_ENDPOINT` and `CHAT_MODEL`
- A model that follows short classification instructions reliably

## Run

```powershell
dotnet run --project .\labs\07-multi-agent-orchestration -- --check
dotnet run --project .\labs\07-multi-agent-orchestration
```

Source: [`labs/07-multi-agent-orchestration/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/07-multi-agent-orchestration/Program.cs)

## Code flow

`FoundryChatClient` adapts project Responses to `Microsoft.Extensions.AI.IChatClient`.
Three `ChatClientAgent` specialists and one router share that client. The router outputs
one label; trusted C# code maps it to an agent, invokes the specialist, and records the
route in console output.

## Expected output

```text
Question: Can I work from another country ...
Route: POLICY -> policy-specialist
...
Question: Why does my Foundry project endpoint return 403 ...
Route: TECHNICAL -> technical-specialist
...
```

## Your Turn

Add a security specialist and a `SECURITY` route. Create a test table of ambiguous
questions and measure routing accuracy before changing prompts.

## Cleanup and cost

Agents are in-process Agent Framework objects, so no agent definitions persist. Each
question invokes the router and one specialist, consuming two model calls.

## Parity and preview caveats

The orchestration uses stable Microsoft Agent Framework 1.15 abstractions on .NET 8.
Foundry-specific hosted-agent packages are not needed for this local console workflow.
