# M3 - Tools and function calling

## Objective

Implement the complete host-side function-call loop and optionally run hosted Code
Interpreter.

## Prerequisites

- `PROJECT_ENDPOINT` and a tool-capable `CHAT_MODEL`
- Code Interpreter enabled for the project when using the optional flag

## Run

```powershell
dotnet run --project .\labs\03-tools-and-function-calling
dotnet run --project .\labs\03-tools-and-function-calling -- --code-interpreter
```

Source: [`labs/03-tools-and-function-calling/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/03-tools-and-function-calling/Program.cs)

## Code flow

1. Advertise a strict JSON schema for `get_weather`.
2. Ask the model to call it.
3. Parse and validate `function_call` arguments in trusted C# host code.
4. Execute a deterministic mock service and submit `function_call_output` with
   `previous_response_id`.
5. Optionally ask the hosted Code Interpreter to calculate statistics.

## Expected output

```text
Executing get_weather(Seattle) -> {...}
Seattle is experiencing light rain ...
Add --code-interpreter ...
```

With the optional flag, a second model-generated statistics answer appears.

## Your Turn

1. **Add a second function tool.** Declare `convert_currency(amount, from, to)`, attach it
   alongside `get_weather`, and ask a question that forces both calls in one turn. The
   existing `calls` loop already handles multiple `function_call` items.
2. **Make Code Interpreter draw.** Run with `--code-interpreter` after changing its prompt
   to request a bar-chart PNG. Inspect `codeResult.RootElement` for the
   `container_file_citation`, then retrieve the cited container file through the
   authenticated Responses/container REST endpoint.
3. **Starve the model.** Remove `get_weather` from `tools` but keep the weather question.
   Watch the model refuse or hedge, proving that the tool—not the model—supplied the facts.

## Cleanup and cost

The mock function creates no resources. Responses and Code Interpreter consume model and
tool capacity; Code Interpreter can create ephemeral containers managed by the service.

## Parity and preview caveats

The Responses tool payload is sent through REST to keep the function loop visible and
portable. Tool schema support depends on the selected model.
