# Fusion add-in E2E runbook

Install the add-in on the machine running Fusion 360:

- **macOS:** from the repository root, run `./scripts/install-addon.sh`.
- **Windows:** from PowerShell, run
  `powershell -ExecutionPolicy Bypass -File scripts\install-addon.ps1`.

Both scripts install the checked-in Python add-in. Stop the previous C# add-in
before upgrading, and preserve the MCP server's operation journal.

Open Fusion 360 and run **Shift+S → Add-Ins → Fusion360MCP → Run**. Confirm
the TEXT COMMANDS window shows
`[MCP] Server listening on localhost:9876`, then call `ping` from the MCP
client and expect `{"ok": true, "status": "pong"}`.

For a stateful E2E smoke, use a named, saved Fusion document and save it before
the run. Unsaved designs may be discarded when Fusion restarts, making the
resulting scene state unavailable for follow-up inspection.

## Candidate smoke sequence

Use a new test design, not an existing user design. Record the candidate
commit, server archive hash, add-in zip hash, OS, Fusion version, and MCP
client. Record each request and response in the evidence file below.

1. Call `ping`; require `status: pong`. Call `get_scene_info`; record
   `model_revision` and the starting body/sketch counts.
2. Call `create_sketch` with `plane: "xy"`, the recorded
   `expected_revision`, and a new UUID `operation_id`. Record its
   `revision_after` and returned sketch name.
3. Call `draw_rectangle` with `width: 2`, `height: 1`, the current
   `expected_revision`, and a new operation ID. Call `extrude` with
   `height: 1`, the updated revision, and a third operation ID.
4. Call `get_scene_info` and `get_object_info` for the returned body. Confirm
   the new body exists, counts changed once, and the revision advanced once
   per applied mutation. Inspect `deltas` and `verification`; a
   `geometry_checked` level requires a measured check.
5. Repeat the exact successful `extrude` request, including its original
   operation ID and arguments. Confirm the server replays the result and
   the body count does not change. Call `create_sketch` with a stale
   `expected_revision` and a fresh operation ID; require
   `REVISION_CONFLICT`, `partial_effects: false`, and no scene change.
6. Call `render_view` and verify an image block. Call `export_f3d` to a
   disposable test path and verify the file exists. Save the Fusion design.
7. Restart only the MCP server, preserving its journal. Repeat the earlier
   successful operation ID; require a replay without another model change.
   Restart the add-in/Fusion separately, reopen the saved design, and record
   the documented process-local revision behavior.
8. On a trusted test network, confirm a non-loopback bind is refused without
   `FUSION_MCP_ALLOW_REMOTE=1` and starts with the opt-in. Reset to loopback
   after the check. Use instrumented headless tests for queued/executing
   timeout races if they cannot be induced reliably in Fusion.

Repeat this sequence on Windows and macOS using the same candidate add-in
zip. Record any Fusion preview API exclusions separately; do not count an
unrun supported case as passed.

The stable release's platform evidence is recorded in
[e2e-stable-release.md](status/e2e-stable-release.md); this file is the runbook.
