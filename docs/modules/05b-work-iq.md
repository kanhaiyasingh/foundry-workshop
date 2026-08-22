# M5b - Work IQ

## Objective

Ask a permission-aware workplace question through Work IQ and verify that the response
contains an actual Work IQ tool call rather than an ungrounded answer.

## Prerequisites

- Licensed Microsoft 365 user
- Tenant admin consent for the supported Work IQ application
- Supported delegated/OBO Work IQ connection
- Authenticated remote endpoint in `WORKIQ_MCP_URL`
- `PROJECT_ENDPOINT`, `CHAT_MODEL`, and optional `WORKIQ_MCP_LABEL`

## Run

```powershell
dotnet run --project .\labs\05b-work-iq -- --check
dotnet run --project .\labs\05b-work-iq
```

Pass a custom workplace question as the first non-option argument:

```powershell
dotnet run --project .\labs\05b-work-iq -- "What launch decisions were made this week?"
```

Source: [`labs/05b-work-iq/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/05b-work-iq/Program.cs)

## Code flow

The lab attaches the configured Work IQ endpoint as an MCP tool, sends the workplace
question, counts MCP discovery/call events, and rejects a response that never used Work
IQ. Returned Microsoft 365 content remains permission-trimmed to the caller.

## Expected output

```text
Work IQ MCP events: <positive count>
<briefing with source references>
The returned content is permission-trimmed ...
```

## Your Turn

Ask for action items with owners and deadlines. Verify each citation opens only content
the signed-in user can already access.

## Cleanup and cost

Remove workshop-only connections or endpoint access material. Work IQ and model usage may
be governed by tenant licensing and service quotas.

## Parity and preview caveats

Work IQ is preview and its supported C# tool/connection path can change. A local stdio
Work IQ server is not reachable from Foundry. The lab fails explicitly on missing config
or absent tool calls; it never reports an ungrounded model answer as Work IQ success.
