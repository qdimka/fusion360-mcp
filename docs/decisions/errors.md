# Errors — decisions and implementation notes

Module: the `error_kind` catalog, classifier, and retry behavior in `src/Fusion360.Mcp.Core/Errors/` (`ErrorKind.cs`, `ErrorClassifier.cs`). The current contract is in [`TOOL_CONTRACTS.md`](../TOOL_CONTRACTS.md) §§2–3. This file records design history; active invariants remain in code comments marked `INVARIANT`, `MEASURED`, `DOC`, or `LIMIT`.

## <a id="catalog"></a>Error-kind catalog (2026-10-30, P0)

The reference defined 11 named error kinds plus the `UNKNOWN` catch-all. The port retained their stable `SCREAMING_SNAKE_CASE` wire strings and added infrastructure and safety errors. The current catalog also includes `TIMEOUT`, `OPERATION_ID_CONFLICT`, `REVISION_CONFLICT`, and `STALE_REFERENCE`; these are implemented, not planned.

The wire strings are a stable public contract. Renaming one requires a documented migration. See `ErrorKind.cs` and §2 of `TOOL_CONTRACTS.md`.

## <a id="retry-policy"></a>Retry behavior (initial design, 2026-10-30, P0)

An early design considered four policy labels: never retry; retry with the same `operation_id`; reacquire the current scene context; or inspect possible partial effects before deciding. These labels were a policy model, not a wire field. The current implementation makes retry decisions from the structured result and the operation-journal state; see §3 of `TOOL_CONTRACTS.md` and `IdempotentToolBackend`.

Read-only socket requests may be retried by the connection layer. Mutations are not automatically retried. A known clean mutation failure can be attempted again with the same ID and arguments. A partial effect or unknown outcome requires reconciliation; the journal rejects a same-ID replay in those cases.

## <a id="timeout-semantics"></a>`TIMEOUT`: known or unknown outcome

The KOMPAS reference highlighted a risk where timed-out work could execute later and a retry could apply a mutation twice. This add-in marks queued work cancelled when the bridge deadline expires and skips it before execution. A structured timeout before the handler starts carries `partial_effects: false`. If the handler has already started and the response is lost, or the socket disconnects, the outcome is unknown; inspect the scene before another mutation. See [`protocol.md#timeout-cancel`](protocol.md#timeout-cancel).

## <a id="classifier"></a>Classified errors rather than plain messages (T2)

The add-in classifies handler exceptions into a stable `error_kind`, contextual `hints`, and an optional `traceback`. Application failures are returned as data in a result object rather than thrown across the MCP boundary. Classification rules live in `ErrorClassifier.cs`; the public catalog is in `ErrorKind.cs` and §2 of `TOOL_CONTRACTS.md`.
