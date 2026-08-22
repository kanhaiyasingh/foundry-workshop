# M13 - Human in the loop and REST

## Objective

Intercept a sensitive transfer tool call for human approval, submit the decision, and
exercise raw Responses REST patterns for single-shot, multi-turn, and SSE output.

## Prerequisites

- `PROJECT_ENDPOINT` and tool-capable `CHAT_MODEL`
- Foundry User access

## Run

```powershell
dotnet run --project .\labs\13-human-in-loop-rest -- --check
dotnet run --project .\labs\13-human-in-loop-rest
```

Source: [`labs/13-human-in-loop-rest/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/13-human-in-loop-rest/Program.cs)

## Code flow

1. Advertise `transfer_funds`, but never execute it inside the model.
2. Parse the request in trusted C# and demonstrate approved and rejected callbacks.
3. Submit the decision as `function_call_output`.
4. Send a direct JSON response, continue it by `previous_response_id`, and parse SSE
   deltas for a streamed story.

## Expected output

```text
Approval path:
[APPROVED] $500.00
...
Rejection path:
[REJECTED] $9000.00
...
REST single-shot: ...
REST multi-turn: ...
REST SSE stream: ...
```

No real funds move; the tool implementation is a deterministic workshop mock.

## Your Turn

Replace the Boolean approval with a console prompt and an amount policy. Record approver,
decision, request hash, and tool arguments in an audit event without recording credentials.

## Cleanup and cost

No persistent resources are created. Model calls and streamed output consume tokens.

## Parity and preview caveats

The lab deliberately uses raw authenticated REST to teach wire behavior. SDK convenience
properties such as `OutputText` are reconstructed by `JsonHelpers` from response items.
