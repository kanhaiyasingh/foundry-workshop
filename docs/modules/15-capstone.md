# M15 - Capstone

## Objective

Combine grounding, function tools, deterministic evaluation, and optional Azure Monitor
tracing in one Contoso Support response.

## Prerequisites

- `PROJECT_ENDPOINT` and `CHAT_MODEL`
- Optional `APP_INSIGHTS_CONN_STRING`
- Completion of M3, M4, M9, and M10 concepts

## Run

```powershell
dotnet run --project .\labs\15-capstone -- --check
dotnet run --project .\labs\15-capstone
```

Source: [`labs/15-capstone/Program.cs`](https://github.com/malaika2820/foundry-workshop-c-/blob/main/labs/15-capstone/Program.cs)

## Code flow

1. Optionally configure an Azure Monitor trace provider.
2. Ask the model about a damaged order with two tools available.
3. Execute order lookup and approved policy retrieval in trusted C#.
4. Submit tool outputs for a final, cited answer.
5. Evaluate delivered status, policy citation, and carrier-case guidance.
6. Add tool-count and pass-rate tags to the trace.

## Expected output

```text
<answer describing delivered status, carrier case, and [support-policy]>
PASS mentions delivered state
PASS cites policy
PASS mentions carrier case
Capstone score: 3/3. ...
```

The lab throws if no tool is called rather than treating an ungrounded answer as success.

## Your Turn

Replace the in-memory policy tool with the M4 knowledge base, route high-value refunds to
the M13 approval pattern, and add successful M12 attacks to the M9 test set.

## Cleanup and cost

No persistent agent is created. Responses and optional telemetry incur normal service
cost. Remove local artifacts and disable any online evaluation rule created in M10.

## Parity and preview caveats

The capstone favors a transparent Responses tool loop over hiding orchestration behind a
single abstraction. Production systems should add durable approval/audit state, retries,
rate controls, content rendering defenses, and evaluation gates.
