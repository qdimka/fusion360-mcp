# Idempotency — decisions and implementation notes

Module: `operation_id` and the durable journal in the MCP server (P2, [ADR-0010](../adr/ADR-0010-operation-id-journal.md)). Current contract: [`TOOL_CONTRACTS.md`](../TOOL_CONTRACTS.md) §§1.2, 2, and 3. Active invariants remain in code comments marked `INVARIANT`, `MEASURED`, `DOC`, or `LIMIT`.

The current implementation returns `OPERATION_ID_CONFLICT` for an unsafe replay. `retry_policy` is not a separate wire field. If the journal has an unknown outcome or records partial effects, reconcile the scene before proceeding with a new operation ID.

## <a id="operation-id"></a>Use an explicit UUID, not an argument hash (2026-10-30, P2)

Mutations are not inherently idempotent: repeating a successful call can apply the same change again. Deduplicating only by a hash of `command + params` was rejected because of collisions and because callers need control over retry identity. Each mutation therefore uses an explicit UUID `operation_id`. Repeating the same ID, command, and arguments after success returns the first result without executing the mutation again.

## <a id="durable-journal"></a>Durable intent and recorded outcome (2026-10-30, P2)

The design adopted lessons from the KOMPAS journal: persist intent before doing the work, treat an already-running operation as a barrier rather than starting a second one, repair a torn final line only while holding the lock, and serialize writes with a cross-process lock.

The server journal is append-only JSONL:

- An intent containing the ID, command, and argument hash is flushed before the add-in call.
- A terminal outcome is appended afterward. An intent without a terminal record after restart requires reconciliation.
- A successful retry returns the recorded result; a known clean failure may be attempted again with the same ID and arguments.
- A retry after partial effects or an unknown outcome is refused with `OPERATION_ID_CONFLICT` rather than replayed.

The implementation uses a local journal and an exclusive file lock. The record lives under the user's home directory by default; `FUSION_MCP_JOURNAL_DIR` changes its directory. See `OperationJournal.cs`.

Rejected alternatives included an in-memory table (lost on restart), a journal inside Fusion (lost with the add-in process), SQLite (more machinery than this append-only journal needs), and argument-hash-only deduplication (no caller-controlled operation identity).

## <a id="timeout-retry"></a>Do not blindly retry a timeout (2026-10-30, P0)

The 300-second bridge timeout has two outcomes. If the queued handler has not started, the bridge cancels it and reports a known no-effect failure; the same operation may be attempted again with the same ID and arguments. If the response is lost after execution may have started, the outcome is unknown and the journal refuses a same-ID retry. Reconcile with scene queries and use a fresh ID for subsequent work. See [`protocol.md#timeout-cancel`](protocol.md#timeout-cancel).
