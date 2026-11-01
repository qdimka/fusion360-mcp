# ADR-0010 — `operation_id` and a durable server-side journal

Status: accepted (implemented).
Date: 2026-10-30.
Environment: contract in [`TOOL_CONTRACTS.md`](../TOOL_CONTRACTS.md) §§1.2, 2, and 3.

## Decision

Every mutation is journaled by the MCP server. An optional UUID `operation_id` can be supplied by the client; otherwise the server generates one and returns it. Repeating the same command and arguments under the same ID replays the recorded successful result without executing the mutation again.

The append-only journal records an intent before sending the mutation and a terminal result afterward. If a restart leaves an intent without a terminal result, reconciliation is required. For a repeated ID:

- A known clean failure permits another attempt using the same ID and arguments.
- A successful operation returns the recorded result.
- Different command or arguments, partial effects, or an unknown outcome are refused with `OPERATION_ID_CONFLICT`. Reconcile the scene and use a fresh ID when the prior outcome cannot safely be replayed.

The journal is implemented in `Fusion360.Mcp.Server`. By default it lives at `~/.fusion360-mcp/operation_journal.jsonl`; `FUSION_MCP_JOURNAL_DIR` overrides the directory.

## Rationale

Mutations are not inherently idempotent. A client retry after a timeout or disconnect could otherwise apply a mutation twice. The server is the appropriate owner because it can outlive Fusion and the add-in process.

## Evidence

Implemented in `OperationJournal.cs` and `IdempotentToolBackend.cs`. Journal records are append-only JSONL and protected by an exclusive cross-process file lock. Pending intents after restart are surfaced for reconciliation.

## Alternatives rejected

- **In-memory deduplication:** lost on process restart.
- **Journal inside the add-in:** lost with Fusion and entangles journal work with Fusion's main-thread model.
- **SQLite:** more machinery than needed for this local append-only journal.
- **Deduplicate only by an argument hash:** does not give the caller control over retry identity.
