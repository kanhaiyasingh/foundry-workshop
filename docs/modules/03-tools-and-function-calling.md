# M3 - Tools & Function Calling

> **Goal:** give an agent tools - first Foundry's hosted **Code Interpreter**, then a
> **custom function** - and watch the model decide when to call them.
>
> **You'll use:** `ResponseTool.CreateCodeInterpreterTool`,
> `ResponseTool.CreateFunctionTool`, `DeclarativeAgentDefinition.Tools`, and the
> `function_call` -> `function_call_output` loop.

The M2 agent could only *talk*. Tools let it **act**: run code, look things up, or call
your APIs. Foundry supports two flavours:

- **Hosted tools**, such as Code Interpreter, run inside Foundry. Attach them and the
  service executes them.
- **Custom function tools** run in your code. The model emits a structured call; your
  host executes it and feeds the result back.

![Anatomy of a Foundry agent](../assets/agent-anatomy.png)

> [!NOTE]
> Tool APIs are evolving. The Python notebook uses `PromptAgentDefinition`,
> `CodeInterpreterTool`, `AutoCodeInterpreterToolParam`, and `FunctionTool`. With this
> repository's current C# packages, their equivalents are
> `DeclarativeAgentDefinition`, `ResponseTool.CreateCodeInterpreterTool` with an
> automatic `CodeInterpreterToolContainer`, and `ResponseTool.CreateFunctionTool`.
> The tool and response wire shapes remain the same.

## Prerequisites

- `PROJECT_ENDPOINT` in the repository `.env`
- A tool-capable `CHAT_MODEL`; it defaults to `gpt-4.1-mini`
- `az login`, or another identity accepted by `DefaultAzureCredential`
- The Azure AI Developer role on the Foundry project
- Code Interpreter support for the selected model and project region

Check configuration without making Azure calls:

```powershell
dotnet run --project .\labs\03-tools-and-function-calling -- --check
```

Run the complete notebook-equivalent flow:

```powershell
dotnet run --project .\labs\03-tools-and-function-calling
```

The default is intentionally **not** an optional Code Interpreter substitution: it
uploads the notebook CSV, creates the Code Interpreter agent, runs the data question,
then creates and runs the custom-function agent. For isolated smoke tests only:

```powershell
dotnet run --project .\labs\03-tools-and-function-calling -- --code-interpreter-only
dotnet run --project .\labs\03-tools-and-function-calling -- --function-only
```

Source:
[`labs/03-tools-and-function-calling/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/03-tools-and-function-calling/Program.cs)

## 1. Configure and build the clients

The familiar bootstrap from M1 loads `.env`, reads `PROJECT_ENDPOINT` and `CHAT_MODEL`,
creates the project client, and keeps handles to both the project OpenAI client and its
file client:

```csharp
var chatModel = context.Config.ChatModel;
var projectClient = context.CreateProjectClient();
var openAiClient = projectClient.ProjectOpenAIClient;
var fileClient = openAiClient.GetOpenAIFileClient();
```

Expected output:

```text
Chat model : gpt-4.1-mini
clients    : ready
```

The C# lab uses the shared `WorkshopConfig` and `WorkshopContext` rather than calling
`DotNetEnv` and `DefaultAzureCredential` directly in every module. This is the C#
equivalent of the notebook bootstrap and honors the same environment values.

## 2. Upload data for Code Interpreter

Code Interpreter runs Python in a sandboxed container. To analyse a file, upload it
with the `assistants` purpose; the returned file id is what the agent's automatic
container receives. The program synthesizes the notebook's exact CSV in memory:

```csv
sector,quarter,operating_profit
TRANSPORTATION,Q1,120
TRANSPORTATION,Q2,135
TRANSPORTATION,Q3,128
TRANSPORTATION,Q4,150
```

It uploads those bytes under the exact filename `quarterly_results.csv`:

```csharp
using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
var uploadResult = await fileClient.UploadFileAsync(
    csvStream,
    "quarterly_results.csv",
    FileUploadPurpose.Assistants);
OpenAIFile uploadedFile = uploadResult.Value;
```

Expected output:

```text
Uploaded file id: assistant-...
```

The prefix can vary with the service API version (`assistant-...` in the notebook and
validated run, or `file-...` on some current routes). This identifier-format
difference does not change the lifecycle: the uploaded file is a persistent project
file that can be attached to agents.

## 3. Create the Code Interpreter agent

The C# SDK's automatic Code Interpreter container is the direct equivalent of
`AutoCodeInterpreterToolParam(file_ids=[uploaded_file.id])`:

```csharp
ProjectsAgentDefinition analystDefinition =
    new DeclarativeAgentDefinition(chatModel)
    {
        Instructions =
            "You are a helpful data analyst. Use Python to answer questions about uploaded files."
    };

analystDefinition.Tools.Add(
    ResponseTool.CreateCodeInterpreterTool(
        new CodeInterpreterToolContainer(
            CodeInterpreterToolContainerConfiguration
                .CreateAutomaticContainerConfiguration([uploadedFile.Id]))));
```

The definition is stored under the notebook's exact agent name and description:

```text
Agent   : data-analyst-agent
Version : 1
```

This is the same `create_version` lifecycle as M2. The model, instructions, and tool
configuration are versioned together. An unchanged rerun can return the existing
version rather than incrementing it.

## 4. Let the agent run code

The request uses an `agent_reference` for `data-analyst-agent` and the notebook's exact
prompt:

```text
From the uploaded CSV, which quarter had the highest operating profit for the
TRANSPORTATION sector, and what was the full-year total?
```

The agent writes and executes Python against the uploaded CSV. The C# program polls
the Responses resource to a terminal state because agent-reference responses can be
asynchronous.

Expected answer:

```text
Q4 had the highest operating profit for TRANSPORTATION at 150. The full-year
total across Q1-Q4 was 533.
```

Wording can vary, but `Q4`, `150`, and `533` are determined by the uploaded data.

> [!TIP]
> **Hosted means you do not run it.** Code Interpreter executes inside Foundry. Each
> conversation gets a sandbox session with an idle timeout of about 30 minutes.
> Charts and files it produces appear as `container_file_citation` annotations and
> can be downloaded through the C# `ContainerClient`.

## 5. Define the custom function tool

For your own logic, declare a tool name, description, and JSON Schema. This is only a
declaration: the model decides when and with which arguments to call it. The actual
implementation remains in your C# process.

M3 preserves the notebook schema exactly:

```json
{
  "type": "object",
  "properties": {
    "city": {
      "type": "string",
      "description": "City name, e.g. 'Zurich'"
    },
    "unit": {
      "type": "string",
      "enum": ["celsius", "fahrenheit"],
      "description": "Temperature unit"
    }
  },
  "required": ["city"]
}
```

The declaration uses the exact function description:

```text
Get the current weather for a city. Call this whenever a user asks about weather.
```

The local implementation also preserves the notebook behavior:

| City | Celsius |
|---|---:|
| Zurich | 18 |
| Cairo | 34 |
| Oslo | 7 |
| Any other city | 21 |

`unit` defaults to `celsius`; `fahrenheit` applies the same
`round(celsius * 9 / 5 + 32)` conversion. Every result ends in `partly cloudy`.

Expected output:

```text
Declared tool: get_weather
```

The schema is the contract the model reads. A crisp description for each tool and
parameter is the biggest lever on whether the model calls it correctly.

## 6. Wire the function-calling loop

The program creates `weather-agent` with the notebook's exact instructions:

```text
You are a travel assistant. Use the get_weather tool to answer weather questions;
don't guess.
```

It then sends the exact user message:

```text
Should I pack a coat for Oslo? What's it like there now?
```

The loop is the full host-side round trip:

1. Create an agent response with the user input and `agent_reference`.
2. Read every `function_call` item.
3. Require the function name to be `get_weather`.
4. Parse and validate the JSON object, required `city`, optional enum `unit`, and
   reject unknown properties.
5. Execute the deterministic C# mock.
6. Submit all `function_call_output` items with their original `call_id`,
   `previous_response_id`, and the same `agent_reference`.
7. Repeat until no function calls remain, then print `output_text`.

Expected shape:

```text
[tool] get_weather({'city': 'Oslo'}) -> Oslo: 7°C, partly cloudy.

Yes - pack a coat. It's about 7°C and partly cloudy in Oslo right now, so a warm
layer will be welcome.
```

The model's prose and whether it explicitly supplies the default `unit` can vary. The
important behavior is that the model chose `get_weather`, the C# host executed the
mock, and the final answer used `Oslo: 7°C, partly cloudy.`

> [!WARNING]
> The model proposes tool arguments. Treat them as untrusted input and validate and
> authorize them before doing anything irreversible. The same loop extends to a
> human-in-the-loop gate: intercept sensitive calls, get approval, then run them.

## Your turn

1. **Add a second function tool.** Declare `convert_currency(amount, from, to)`, attach
   it alongside `get_weather`, and ask a question that forces both calls in one turn.
   The loop already handles multiple `function_call` items per response.
2. **Make Code Interpreter draw.** Ask section 4 for a bar-chart PNG, find the
   `ContainerFileCitationMessageAnnotation` in the response, and download the bytes
   with `openAiClient.GetContainerClient().DownloadContainerFile(...)`.
3. **Starve the model.** Remove `get_weather` from `Tools` but keep the weather
   question. Watch it refuse or hedge, proving the tool, not the model, supplied the
   facts.

## Lifecycle, cleanup, and cost

The normal run intentionally matches the notebook lifecycle and leaves these
persistent project resources for inspection and reruns:

- assistant-purpose file `quarterly_results.csv` (its id is printed)
- `data-analyst-agent` and its returned version
- `weather-agent` and its returned version
- Responses records retained according to the project policy

Delete the two agent versions from **Build > Agents** and delete the printed uploaded
file from the project's file store when finished. Use the exact names and versions
printed by the run; do not delete unrelated versions. The C# lab does not
automatically delete them because unchanged `create_version` calls may resolve to an
existing notebook/workshop version.

The Code Interpreter container is ephemeral and service-managed. Each conversation
creates a separate billable session, active for up to one hour with an idle timeout of
about 30 minutes. Code Interpreter has charges beyond model tokens. File upload,
agent Responses calls, and function-agent synthesis use project storage or model/tool
capacity; the local mock itself creates no Azure resource and has no external API
cost.

## C# parity notes

- `WorkshopContext` centralizes `.env` and credential setup instead of repeating the
  notebook imports.
- The current C# names differ from the Python names, but the agent definitions,
  automatic container with `file_ids`, function schema, and Responses payloads are
  equivalent.
- The C# program explicitly polls agent-reference responses because this service path
  can return an asynchronous response before Code Interpreter or the function agent
  has finished.
- `--code-interpreter-only` and `--function-only` isolate smoke tests; they do not
  change the default full notebook sequence.

Your agent can now run hosted code and call your own functions. Next, ground it in
your knowledge so answers are backed by real sources:
[M4 - Grounding / RAG](04-grounding-rag.md).
