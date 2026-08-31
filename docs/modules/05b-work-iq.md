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

Source: [`labs/05b-work-iq/Program.cs`](https://github.com/kanhaiyasingh/foundry-workshop/blob/main/labs/05b-work-iq/Program.cs)

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

1. **Cross-signal question.** Ask: “Summarize the SharePoint doc Dana shared in Teams
   yesterday.” Inspect `response.RootElement["output"]` and confirm that more than one
   `mcp_call` appears when the answer combines files and Teams.
2. **Tighten governance.** Keep `require_approval = "never"` for the read-only request. If
   the server exposes write tools separately, send those through an MCP tool/request with
   `require_approval = "always"` and confirm that approvals appear only for actions.
3. **Capstone tie-in.** Sketch a work-grounded specialist for Module 15. Decide which
   questions the router should send to Work IQ and which should go to the Module 4
   Foundry IQ knowledge base.

## Cleanup and cost

Remove workshop-only connections or endpoint access material. Work IQ and model usage may
be governed by tenant licensing and service quotas.

## Parity and preview caveats

Work IQ is preview and its supported C# tool/connection path can change. A local stdio
Work IQ server is not reachable from Foundry. The lab fails explicitly on missing config
or absent tool calls; it never reports an ungrounded model answer as Work IQ success.
