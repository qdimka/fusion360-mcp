# Architecture

## Components

```text
MCP client
  │ stdio: MCP / JSON-RPC
  ▼
Fusion360.Mcp.Server (.NET 10)
  │ TCP localhost:9876, newline-delimited JSON
  ▼
Fusion360MCP add-in (checked-in Python package on Windows/macOS)
  │ Fusion CustomEvent
  ▼
Fusion main thread and API
```

The server exposes the MCP tools, resources, prompts, and templates. It
converts tool calls to add-in commands and formats add-in results as MCP
content. The add-in is responsible for Fusion API access; Fusion API work is
dispatched onto Fusion's main thread. The current generated tool catalog is
documented in [TOOLS.md](TOOLS.md).

## Request path

1. The MCP client calls a named tool over stdio.
2. The server validates and dispatches it through the socket backend. Mutating
   calls pass through the operation journal first.
3. The server sends a JSON line containing `type` and `params` to the add-in.
4. The add-in queues the command and posts a Fusion `CustomEvent`. Its event
   handler runs the command on the main thread and signals the waiting socket
   worker.
5. The add-in returns a JSON-line result. The server renders it as MCP text
   content and, for `render_view`, an image content block.

The current socket defaults are `localhost:9876`. `FUSION_MCP_HOST` and
`FUSION_MCP_PORT` configure the address. Binding outside loopback requires
`FUSION_MCP_ALLOW_REMOTE=1` on the Fusion host; the socket has no
authentication, so only use a trusted network.

## Threading and timeouts

Socket workers never call the Fusion API directly. They submit work to a
queue, wait for the main-thread event, and receive that item's result. A backup
timer checks overdue queued items every 200 ms in case Fusion did not deliver
the event. The bridge timeout is 300 seconds; the server's client timeout is
310 seconds, leaving time for a structured timeout response.

A command that has not started when the bridge deadline expires is cancelled
and reported as a known no-effect timeout. A handler already running cannot
be interrupted. If its response is lost, the outcome is unknown; inspect the
document before attempting another mutation.

## Mutation safety

The server records each mutation's intent in `operation_journal.jsonl` before
sending it. Retries with the same `operation_id` and arguments replay the
recorded successful result instead of executing twice. Reusing an ID with
changed arguments is rejected. Partial effects or an unknown outcome require
reconciliation and a fresh ID.
See [TOOL_CONTRACTS.md](TOOL_CONTRACTS.md) and
[ADR-0010](adr/ADR-0010-operation-id-journal.md).

Document mutations can also supply `expected_revision`. The add-in rejects a
stale revision before changing the model and returns the current revision.
See [ADR-0011](adr/ADR-0011-lightweight-revisioning.md).

The add-in reports mutation deltas where available and an optional
verification block. The Python add-in emits `existence_checked` for returned
body/sketch names it can resolve, or `call_returned` otherwise. Geometry
read-back checks remain release work. The
verification level states only a check actually completed.
