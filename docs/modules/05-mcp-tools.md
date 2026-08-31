# M5 - MCP tools

## Objective

Connect a Foundry response to a remote Model Context Protocol server and inspect whether
the model discovered and invoked MCP tools.

## Prerequisites

- `PROJECT_ENDPOINT` and `CHAT_MODEL`
- A Foundry-reachable `MCP_SERVER_URL`
- Optional `MCP_SERVER_LABEL`

The Microsoft Learn server is a public, authless starting point:

```ini
MCP_SERVER_URL=https://learn.microsoft.com/api/mcp
MCP_SERVER_LABEL=microsoft_learn
```

## Run

```powershell
dotnet run --project .\labs\05-mcp-tools -- --check
dotnet run --project .\labs\05-mcp-tools
```

Source: [`labs/05-mcp-tools/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/05-mcp-tools/Program.cs)

## Code flow

The program declares an `mcp` Responses tool, passes the server label and URL, asks a
tool-requiring question, counts `mcp_list_tools`/`mcp_call` output items, and prints the
model's final synthesis.

## Expected output

```text
MCP events: <positive count>
<answer based on the server's tools>
```

Tool names and wording depend on the configured server. If the prompt does not match the
server, the event count can be zero; use a server-specific question.

## Your Turn

1. **Ask a multi-step question.** Try: “Find overdue tasks on the Aurora project and flag
   a risk for the latest one.” Inspect `response.RootElement["output"]` and confirm that
   more than one `mcp_call` item appears.
2. **Tighten the instructions.** Edit `input` to require the task id for every item and
   rerun. Notice the format change.
3. **Flip approval on.** Run with `--require-approval` and inspect `response.output` for
   an `mcp_approval_request` instead of an immediate tool call.

## Cleanup and cost

The lab creates no Foundry objects. Stop or delete a custom MCP host when finished and
remove any associated project connection. Model and external service usage can incur cost.

## Parity and preview caveats

MCP support is sent over the current Responses REST shape. Authentication differs by
server; never embed long-lived credentials in source or documentation.
