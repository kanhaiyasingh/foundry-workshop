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

Add a required date parameter, reject dates in the past, and return a structured tool
error. Confirm the model explains the failure instead of inventing weather.

## Cleanup and cost

The mock function creates no resources. Responses and Code Interpreter consume model and
tool capacity; Code Interpreter can create ephemeral containers managed by the service.

## Parity and preview caveats

The Responses tool payload is sent through REST to keep the function loop visible and
portable. Tool schema support depends on the selected model.
