# Fusion add-in E2E runbook

Install the add-in on the machine running Fusion 360:

- **macOS:** from the repository root, run `./scripts/install-addon.sh`.
- **Windows:** from PowerShell, run
  `powershell -ExecutionPolicy Bypass -File scripts\install-addon.ps1`.

Both scripts install the checked-in Python add-in. If an older C# add-in is
installed, stop or remove it in Fusion before testing to avoid two listeners
on port 9876. Preserve the MCP server's operation journal.

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

## Catalog group matrix

Run each row against the named, saved disposable design and record its exact
request and response. Values in angle brackets are replaced with names returned
by earlier operations or with an absolute disposable path. For each mutation,
add the latest `expected_revision` and a fresh UUID `operation_id`; retain the
exact original request for replay checks. Dimensions are in the catalog units.

| Tool and example arguments | Precondition and observation |
| --- | --- |
| `get_scene_info` `{}` | Active saved design; record revision, bodies and sketches. |
| `get_design_type` `{}` | Active design; record the reported type. |
| `create_sketch` `{"plane":"xy"}` | Active design; record sketch name and revision. |
| `extrude` `{"height":1,"profile_index":0,"operation":"new_body"}` | Draw a closed rectangle in the current sketch; record the resulting body. |
| `move_body` `{"body_name":"<body>","x":1,"y":0,"z":0}` | Use the existing solid; verify its position changed. |
| `create_box` `{"length":2,"width":1,"height":1}` | Active design; record the new solid's name and dimensions. |
| `create_construction_plane` `{"method":"offset","plane":"xy","offset":2}` | Active design; verify the construction plane exists. |
| `create_component` `{"name":"E2EComponent"}` | Use an Assembly Design (not Part Design); name is unused. Verify the new component. |
| `get_physical_properties` `{"body_name":"<body>","accuracy":"medium"}` | Existing solid; record measured properties and units. |
| `set_color` `{"body_name":"<body>","red":255,"green":0,"blue":0}` | Existing solid; inspect its appearance. |
| `create_parameter` `{"name":"E2E_Length","value":10,"unit":"mm"}` | Parameter name is unused; inspect its value and unit. |
| `patch_surface` `{"sketch_name":"<closed-sketch>","profile_index":0,"continuity":"connected"}` | Create a separate sketch with a closed profile; verify a surface is created. |
| `flat_pattern` `{"body_name":"<sheet-metal-body>"}` | Create a valid sheet-metal body in the disposable design; verify the flat pattern. |
| `cam_list_setups` `{}` | A CAM-capable design is active; record the setup list, including an empty list if applicable. |
| `export_f3d` `{"file_path":"<disposable-absolute-path>/e2e.f3d"}` | Destination directory exists and is writable; inspect the resulting file. |
| `render_view` `{"view":"iso","width":640,"height":480,"fit":true}` | Active design contains visible geometry; verify the returned image. |

Before a candidate run, record any preview-only API exclusion, the Fusion
version, and its reason in the evidence record. Do not infer an exclusion
from an untested row. For journal conflict testing, repeat a successful
mutation's `operation_id` with changed arguments and require
`OPERATION_ID_CONFLICT` without another scene change. Also record a deliberately
invalid mutation and verify that the scene remains unchanged.

Run this sequence on macOS before release using the candidate add-in zip.
Windows installer CI is a pre-release gate. Run the same live Fusion sequence
on Windows after release using the published candidate, and record preview API
exclusions separately; do not count an unrun supported case as passed.

The stable release's platform evidence is recorded in
[e2e-stable-release.md](status/e2e-stable-release.md); this file is the runbook.
