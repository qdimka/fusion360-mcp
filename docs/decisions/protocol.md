# Protocol — decisions and implementation notes

Modules: protocol models and codec in `src/Fusion360.Mcp.Core/Protocol/`,
with the Python bridge in `addon/Fusion360MCP/server/`. The former C# bridge
was retired from the repository; historical implementation decisions are
superseded by the Python add-in. This file records design history; current wire behavior is described
in [`TOOL_CONTRACTS.md`](../TOOL_CONTRACTS.md). Active invariants remain in
code comments marked `INVARIANT`, `MEASURED`, `DOC`, or `LIMIT`.

## <a id="envelope"></a>Baseline result and additive mutation fields (2026-10-30, P0)

The baseline result used `ok: true` plus tool-specific fields and optional mutation `deltas`; failures used `ok: false`, `error_kind`, `error_message`, and optional `hints`/`traceback`. That shape remains stable. Optional mutation fields were added over time: `verification` (ADR-0009), `partial_effects`, `revision_after` (ADR-0011), and `operation_id` (ADR-0010). Older clients can ignore fields they do not understand. Optional fields may be absent for some commands or when talking to an older add-in. See `CommandResult.cs` and §1 of `TOOL_CONTRACTS.md`.

## <a id="timeouts"></a>Timeout values (T2)

Timeout values are centralized in `ProtocolConstants.cs`: bridge command timeout 300 seconds, client timeout 310 seconds, ping timeout 5 seconds, retry delay 1 second, bridge backup timer 200 ms, server restart delay 2 seconds, at most 10 accept-loop restarts, and a 64 KiB receive buffer. The client timeout exceeds the bridge timeout so a structured timeout can normally arrive before the client gives up. The checked-in Python add-in uses the same 300-second bridge timeout.

## <a id="timeout-cancel"></a>Cancel expired queued work rather than executing it later (T3)

If a queued command reaches the 300-second bridge deadline before its handler starts, the waiting socket worker marks it cancelled. `DrainQueue` skips cancelled items, preventing late execution after the caller has received a timeout. The bridge cannot interrupt a handler that has already started.

The result distinguishes these cases: a command cancelled before execution is a known no-effect failure with `partial_effects: false`; if an in-progress result is lost, the operation outcome is unknown. In the latter case, inspect the scene before issuing another mutation.

## <a id="retries"></a>Automatic retries (T2)

`Fusion360Connection` allows up to two retries with a one-second delay for read-only requests. Mutations are not retried automatically. Any retry of a mutation is explicit and governed by the operation ID journal; see [idempotency](idempotency.md#operation-id) and §3 of `TOOL_CONTRACTS.md`.

## <a id="units"></a>Wire units (T2)

Linear dimensions use Fusion's internal unit, centimetres; angles use radians. Mutation mass deltas use grams. Tool-specific units are listed in [`TOOLS.md`](../TOOLS.md). Verification comparisons use the shared table in [`verification.md#tolerances`](verification.md#tolerances) and §4 of `TOOL_CONTRACTS.md`.
