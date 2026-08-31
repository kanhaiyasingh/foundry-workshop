# M2 - Your First Agent

> **Goal:** turn a raw model into a **named, versioned agent** - give it instructions,
> create it on Foundry, invoke it, then iterate safely.
>
> **You'll use:** the C# `DeclarativeAgentDefinition`,
> `AgentAdministrationClient.CreateAgentVersionAsync`, and the Responses API with an
> `agent_reference`.

In [M1](01-first-inference.md) you called a model directly. An **agent** wraps that
model in a reusable, server-side definition:

> **agent = model + instructions + tools**

The definition lives in your Foundry project under a stable **name**. When you change
the definition, Foundry stores a new **version**, so callers can keep using the same
name while the agent evolves.

![Anatomy of a Foundry agent](../assets/agent-anatomy.png)

If your project and `.env` are not ready, complete [Setup](../setup.md) first.

## Run

```powershell
dotnet run --project .\labs\02-your-first-agent -- --check
dotnet run --project .\labs\02-your-first-agent
```

Source: [`labs/02-your-first-agent/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/02-your-first-agent/Program.cs)

## 1. Configure

The lab reads `PROJECT_ENDPOINT` and `CHAT_MODEL` from the same `.env` used by every
lab. `PROJECT_ENDPOINT` is required; `CHAT_MODEL` defaults to `gpt-4.1-mini`. The lab
then selects the stable name `storytelling-agent`.

```text
Project : https://<account>.services.ai.azure.com/api/projects/<project>
Chat    : gpt-4.1-mini
Agent   : storytelling-agent
```

Keep the name stable because versioning keys off it.

## 2. Build the client

The C# bootstrap follows the same path as M1:
`DefaultAzureCredential` (or the workshop's configured `AzureCliCredential`) to the
project client and its OpenAI-compatible Responses surface. The
`AgentAdministrationClient` provides agent creation and versioning.

```text
project_client : ready
openai_client  : ready
```

A credential error usually means you need `az login`; a `403` usually means your
identity lacks the **Azure AI Developer** role on the project.

## 3. Define and create the agent

`DeclarativeAgentDefinition` is the C# SDK equivalent of the notebook's
`PromptAgentDefinition`. It contains the **model** and **instructions** that shape the
agent's behavior. Tools come in [M3](03-tools-and-function-calling.md).

The first definition uses this exact prompt:

```text
You are a storytelling agent. You craft engaging one-line stories based on user prompts and context.
```

It is stored under `storytelling-agent` with `CreateAgentVersionAsync`.

On the first run in a project where this agent does not exist, the service returns:

```text
Name    : storytelling-agent
Version : 1
Replay  : 1 (unchanged)
```

The C# lab immediately submits the identical definition a second time and fails if the
returned version changes. This verifies the notebook's `create_version` idempotency
claim against the live service: an unchanged definition returns the latest version,
while a changed definition creates the next version. If `storytelling-agent` already
exists, the displayed number can be higher than `1`; the invariant is that `Version`
and `Replay` match.

## 4. Invoke the agent

The lab calls the same Responses API used in M1, but attaches an `agent_reference`
instead of specifying `model`. In C#, `GetProjectResponsesClientForAgent` supplies the
equivalent reference using the stable agent name. Foundry resolves the name and applies
the stored model and instructions.

The exact user prompt is:

```text
Tell me a one-line story about a lighthouse keeper.
```

Expected output resembles:

```text
Every night the keeper lit the lamp for ships that never came - until the night
one finally did, carrying the letter he'd stopped waiting for.
```

Wording varies; what matters is that the response uses the stored storytelling voice
without the caller resending the system prompt.

### Follow-ups and conversations

The notebook makes one independent Responses request here and another independent
request after versioning. The C# lab intentionally does the same: it supplies neither
`PreviousResponseId` nor a project conversation, so reusing a Responses client does
not carry history between calls.

For a direct follow-up in C#, create `CreateResponseOptions`, set
`PreviousResponseId` to the prior `ResponseResult.Id`, and add the next user message to
`InputItems`. For server-managed multi-turn history, explicitly create a project
conversation and bind it to `GetProjectResponsesClientForAgent`. Neither mechanism is
needed for the notebook-equivalent flow.

## 5. Version the agent

The lab changes the instructions while retaining the same name:

```text
You are a storytelling agent with a melancholic, noir voice. You craft a single haunting sentence based on the user's prompt.
```

It calls `CreateAgentVersionAsync` again, then invokes the same lighthouse prompt
through an agent reference to the stable name.

On the first clean-project run, expected output resembles:

```text
Name    : storytelling-agent
Version : 2

The lamp still turns, but the keeper stopped counting the years the sea kept
taking from him.
```

The **name** is the stable contract callers depend on; the **version** is the audit
trail of how the agent evolved. Because the notebook and C# lab use a name-only agent
reference, invocation resolves the latest version. Never rename to iterate -
re-version. On later full runs, the program alternates from the first definition to the
second, so both changed definitions can create higher versions.

## Your Turn

1. **Reshape the voice.** Rewrite the instructions in section 5, for example as a
   cheerful children's-book narrator, and rerun. Confirm the version increments and
   the tone flips.
2. **Prove idempotency.** Observe the unchanged replay already built into section 3:
   `agent.Version` must hold steady. Then change one word and watch it bump.
3. **Give it context.** Build `CreateResponseOptions` and add a second message to
   `InputItems`, such as a system-style preface or a prior turn. Observe how the agent
   blends per-call context with its stored instructions. To make a later request a true
   follow-up, set `PreviousResponseId` or explicitly use a project conversation.

You created a named agent, invoked it via `agent_reference`, and versioned it safely.
Next: give it real tools in [M3 - Tools & Function Calling](03-tools-and-function-calling.md).

## Cleanup and cost

`storytelling-agent` and its versions persist in the Foundry project; `--check` creates
nothing, but a full run can create versions and both Responses calls consume model
tokens. Delete the workshop agent in the Foundry portal when it is no longer needed.

## C# SDK difference

The Python notebook supplies
`extra_body={"agent_reference": {"name": ..., "type": "agent_reference"}}` directly.
The C# 2.0 SDK expresses the same behavior through
`GetProjectResponsesClientForAgent(defaultAgent: name)`, which adds the agent reference
to Responses requests. No project conversation is created because the notebook does
not create one.
