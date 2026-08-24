# M2 - Your first agent

## Objective

Create a versioned prompt agent and continue a conversation through the native
`Azure.AI.Projects.Agents` and `Azure.AI.Extensions.OpenAI` clients.

## Prerequisites

- M1 completed
- `PROJECT_ENDPOINT` and `CHAT_MODEL`
- Permission to create agent versions in the project

## Run

```powershell
dotnet run --project .\labs\02-your-first-agent -- --check
dotnet run --project .\labs\02-your-first-agent
```

Source: [`labs/02-your-first-agent/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/02-your-first-agent/Program.cs)

## Code flow

1. Build a `DeclarativeAgentDefinition` with model and instructions.
2. Create a new version under the stable name `workshop-concierge`.
3. Create a project conversation.
4. Invoke the agent twice with `ProjectResponsesClient`; the second request uses the
   same conversation without resending history.

## Expected output

```text
Created workshop-concierge version <n> (...)
<brief explanation of model calls versus agents>
Follow-up: <five-word summary>
```

Re-running creates another agent version by design.

## Your Turn

1. **Reshape the voice.** Rewrite `definition.Instructions`—for example, make the agent a
   cheerful children's-book narrator—rerun, and confirm that its version and tone change.
2. **Prove idempotency.** Publish the unchanged definition twice and watch
   `version.Version` hold steady. Then change one word and watch it increase.
3. **Give it context.** Add a prior turn to the same project conversation, then send the
   next request through `responses`. See how the agent blends the conversation context
   with its stored instructions.

## Cleanup and cost

Agent definitions persist in the project and model calls consume tokens. Delete unused
agent versions from the Foundry portal when the workshop ends.

## Parity and preview caveats

This lab follows the current official C# quickstarts and uses native 2.0 SDK types.
Agent service capabilities still vary by model and region.
