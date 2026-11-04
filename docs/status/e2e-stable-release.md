# Stable release E2E evidence

Status: pending
Windows: pending
macOS: pending
Candidate version: pending
Add-in SHA256: pending

Windows Fusion E2E has not been run: a Windows Fusion host is currently
unavailable. Windows remains pending; installer CI is not a substitute for
live Fusion testing.

Record the candidate commit, server and add-in artifact SHA-256 values, OS and
Fusion versions, named saved design, each command and observed response from
the matrix in [the release task](../tasks/python-addin-stable-release.md),
defects, and affected-case reruns. The add-in hash must match the reproducible
release zip built from the tagged source. Change a platform line to `pass` only after
its run is complete. Set `Status: pass` only when both platform runs and all
release-blocking cases pass on the candidate artifacts.

## Preliminary artifacts (not E2E evidence)

Built locally from source commit `7a8436fb923602115b1b7ba2876a6a75e73c47bd`;
all entries in `SHA256SUMS` verified locally. These archives have not been
installed or tested in Fusion on either host and are not final release evidence.

| Artifact | SHA-256 |
| --- | --- |
| Python add-in | `76a29dd19aadc0622a7b1d601fb883b01deb9356ebdf1f86bc0ed1cd8fbdb001` |
| Server win-x64 | `601527885033ca89986130b461f0301d34092b5b82f70af9f917b1b6cb1e6a3c` |
| Server osx-arm64 | `2431a8e37a5cec3da2f81fe43e64a5c2a29e7bb8670f49fdc575b10f55638065` |
| Server osx-x64 | `6aa73f6a116c2b244ccef6b6621ad3a7959952a5d82d9c663a00db527c4a25c1` |
| Server linux-x64 | `a6f4046b6e39ef8c35bc4fa0652feef46f29e5ec6818b5c5744880e08156f748` |

Preliminary macOS Fusion: pending (no active connection at preparation time).
Preliminary Windows Fusion: pending (host unavailable).

Local journal gate: 28 filtered `IdempotentToolBackendTests` and
`OperationJournalTests` passed on 2026-11-04. This does not replace either
live Fusion platform run.

## Preliminary macOS run — 2026-11-04 (incomplete)

macOS 15.7.4, Fusion 2706.1.17, saved design `Codex MCP E2E candidate
2026-11-04`; add-in zip SHA-256 `76a29dd19aadc0622a7b1d601fb883b01deb9356ebdf1f86bc0ed1cd8fbdb001` from the preliminary source above. The connected MCP server binary has not been identified as a versioned candidate, so this is preliminary evidence only.

- `ping` returned `pong`; initial `get_scene_info` reported revision 0, zero bodies and sketches. `get_design_type {}` returned parametric.
- `create_sketch {"plane":"xy","expected_revision":0,"operation_id":"64fb8b63-0f33-4a23-9899-5c859ed153ab"}` returned Sketch1, revision 1. `draw_rectangle {"width":2,"height":1,"expected_revision":1,"operation_id":"bd5dd5af-05bd-4078-8c5f-88ec685fba44"}` created one profile, revision 2. `extrude {"height":1,"profile_index":0,"operation":"new_body","expected_revision":2,"operation_id":"0f9f34b0-406c-40eb-b86a-d7d320ee7ffb"}` returned Extrude1, revision 3; Body1 measured 2 cm³ and 10 cm².
- Repeating that exact extrude returned the original result with one body and revision 3; changing height to 2 with the same ID returned `OPERATION_ID_CONFLICT`, leaving the scene unchanged. `create_sketch` with revision 0 and ID `406f40d1-178e-41ee-9c47-70101af5ae70` returned `REVISION_CONFLICT`, `partial_effects: false`; scene and revision remained unchanged.
- `move_body {"body_name":"Body1","x":1,"expected_revision":3,"operation_id":"12cdb86f-5de4-4655-bc4d-421a699fb646"}` advanced revision to 4; bounding box became [1,0,0]–[3,1,1]. `create_box {"length":2,"width":1,"height":1,"center_x":6,"expected_revision":4,"operation_id":"ab527542-855c-4f6a-9546-40af67b9f180"}` created Body2, revision 5. `create_construction_plane {"method":"offset","plane":"xy","offset":2,"expected_revision":5,"operation_id":"007f9985-aec2-4bb0-992b-8775d6670a8c"}` returned Plane1, revision 6.
- `create_component {"name":"E2EComponent","expected_revision":6,"operation_id":"bb3fb6b7-44ad-4f1b-a441-c64bc991b7fe"}` failed: Fusion Part Design documents allow only one component. Scene remained at revision 6; the response marked the outcome unknown. A valid Assembly precondition remains to be tested.
- `set_color {"body_name":"Body1","red":255,"green":0,"blue":0,"expected_revision":6,"operation_id":"9020e89d-28d4-4c27-b324-46b5e5d54e16"}` returned red, revision 7; image showed a red body. `create_parameter {"name":"E2E_Length","value":10,"unit":"mm","expected_revision":7,"operation_id":"b0243737-8357-411d-a663-63e293bf90ec"}` returned `10 mm`, revision 8; `get_parameters` confirmed the expression.
- A separate Sketch2 and rectangle (IDs `b9901d09-39f3-47cc-a297-1fbc3d9c9927`, `fa261497-fee0-4efb-93e8-8816f0413ae4`, revisions 9–10) provided a closed profile. `patch_surface {"sketch_name":"Sketch2","profile_index":0,"continuity":"connected","expected_revision":10,"operation_id":"f2f86835-41dd-459e-b416-453a3e355c61"}` produced SurfacePatch1 and Body3, revision 11.
- `get_physical_properties {"body_name":"Body1","accuracy":"medium"}` returned volume 2, mass 0.0157 and center [2,0.5,0.5]; `cam_list_setups {}` returned an empty list. `export_f3d {"file_path":"/private/tmp/fusion-mcp-e2e-candidate.f3d","operation_id":"e635119b-7eb4-4ac3-b9a3-cc3c3cf1e545"}` produced a 91 KB F3D archive. `render_view {"view":"current","width":640,"height":480,"fit":false}` returned a PNG.
- **Defect:** after `cam_list_setups`, `render_view {"view":"iso","width":640,"height":480,"fit":true}` failed with `'CAM' object has no attribute 'rootComponent'`. This candidate did not pass the matrix. A regression and fix require a rebuilt candidate and live retest.
- Invalid `move_body` for `E2E_Missing` with revision 11 and ID `ee1565cf-f011-4522-b374-ef4be847e66d` returned `BODY_NOT_FOUND`, `outcome_unknown: true`; the scene and revision remained unchanged.

Sheet-metal flat pattern, component creation in an Assembly, network, restart and full candidate-server checks remain pending. No Windows Fusion E2E has run.

### Render defect retest

A new add-in zip built from commit `591495b` has SHA-256
`828a60be489771ecac6df0cc3de43038325a730b266515e5d09372877e11186b`.
On macOS Fusion 2706.1.17, the installed handler matched the extracted
package. After restarting Fusion, `cam_list_setups {}` returned zero setups
and `render_view {"view":"iso","width":640,"height":480,"fit":true}`
returned a 640×480 PNG. The first `ping` after restart returned `The node
already has a parent`; a second read-only `ping` returned `pong`. The source
and reproducibility of that reconnect error remain unconfirmed and require
investigation. The fixed zip has not passed the full matrix or Windows tests.

The reconnect error was reproduced headlessly: encoding a read-only request
again with the same parameters threw `The node already has a parent` because
the first encoding attached them to its JSON object. The codec now copies the
parameters before encoding; the regression passed along with 121 Core and 84
Server tests. A rebuilt server and live reconnect retest are still required.

### Further preliminary macOS checks

On 2026-11-04, a separate saved Assembly Design `Codex MCP E2E real assembly
2026-11-04` was created using Fusion's document-type selector. With revision 0,
`create_component {"name":"E2EComponent","expected_revision":0,
"operation_id":"de1e615c-1809-4e64-acbb-0ad37b0c264d"}` succeeded at
revision 1, and `list_components` showed the root and E2EComponent. The earlier
failure in the similarly named Part Design did not exercise the required
Assembly precondition.

The source at `7a10607eebbddc29dd824bca70316724e4a3ee9d` was published locally;
all five entries in `SHA256SUMS` verified. The Python zip hash remained
`828a60be489771ecac6df0cc3de43038325a730b266515e5d09372877e11186b`;
the macOS arm64 server archive hash was
`06f418af514fab0f3e420e039e19d8dd9ac4ce85c06f6c062479290e6a6e7505`
and the extracted server executable hash was
`70b656b7a532a773564c7b78315afd76e92d3422a4b9bdd7aef5bca88feb92d7`.
A separate server process with an isolated temporary journal connected to the
live add-in through a localhost TCP proxy. After a successful `ping`, the proxy
closed the connection; the next `ping` returned `pong` through the reconnected
socket. This verifies the read-only reconnect path on that binary, not the full
matrix or server restart and journal persistence.

A saved test Part Design named `Codex MCP E2E sheet metal 2026-11-04` contains
a closed 4×2 cm sketch. A genuine sheet-metal base flange and its flat pattern
remain pending; UI automation could not operate Fusion's main toolbar. Network
opt-in, full candidate reruns, and Windows Fusion E2E also remain pending.

The built macOS arm64 server was also restarted against the live add-in with an
isolated journal. `create_parameter {"name":"E2E_Journal_Restart","value":12,
"unit":"mm","expected_revision":3,"operation_id":
"bd9d6a97-e7ac-4970-b2ba-f1619a3b76a2"}` succeeded at revision 4. After
server restart, the identical request returned the identical recorded result;
`get_scene_info` remained at revision 4. This demonstrates persistence and
replay for that operation.

In the sheet-metal test document, `extrude` of the prepared profile with height
0.1 cm (ID `f0f26df8-464c-4d29-b413-ecc34926ef9b`) produced a thin solid at
revision 3. `create_flange {"body_name":"Body1","edge_index":0,"height":1,
"angle":90,"expected_revision":3,"operation_id":
"f1e6ffc7-d140-49e7-b6f3-c929a2bf431d"}` failed with
`'FlangeFeatures' object has no attribute 'createInput'`; the scene remained
unchanged. Fusion 2706.1.17 exposes `createEdgeFlangeInput` and
`createBaseFlangeInput` as preview methods, explicitly marked not for
distributed programs in its installed API documentation. This is a release
blocker for the advertised flange tool; a real sheet-metal base flange and
flat-pattern test remain outstanding. The thin extruded solid is not claimed
to be a sheet-metal base flange.

A diagnostic `flat_pattern` call on the thin solid (not a valid sheet-metal
body), with revision 4 and operation ID `e99f29f9-3351-4f84-9341-3f13578edada`,
failed before examining the body: `'Features' object has no attribute
'flatPatternFeatures'`. The scene remained at revision 4. This identifies an
API compatibility defect but does not constitute a flat-pattern E2E pass. The
installed Fusion API exposes `Component.createFlatPattern(stationaryFace)`;
its availability and behavior for a genuine sheet-metal body must be checked
before implementing and validating a replacement.

An isolated listener test rejected `127.0.0.2` without remote opt-in as
expected. With opt-in, macOS returned `Can't assign requested address` for
that loopback alias; the attempted test therefore did not verify an opt-in
listener. No LAN listener was opened, so the trusted-network opt-in case
remains pending.

### Flat-pattern API fix and remaining flange limitation

Source commit `b970207` replaces the nonexistent `flatPatternFeatures` call
with `Component.createFlatPattern`, checks `isSheetMetal` and an existing flat
pattern, and chooses the largest planar body face as the stationary face.
Headless regressions cover face selection and rejecting non-sheet solids; the
full Python suite passed 22 tests. The rebuilt add-in zip SHA-256 was
`89114ec20b08d6c041f1980ac4b163e6daa9aa54869bc954e2bde351cae0d0af`.
The installed handler matched the extracted archive. In Fusion 2706.1.17, a
saved disposable design containing an ordinary thin solid rejected
`flat_pattern` with `Body 'Body1' is not a sheet metal body`; scene and
revision remained unchanged. The success path still needs a real sheet-metal
body and live validation, including confirming stationary-face selection.

Autodesk documents [`Component.createFlatPattern`](https://help.autodesk.com/cloudhelp/ENU/Fusion-360-API/files/fusion_Component_createFlatPattern.htm)
as a released API. Its
[`createEdgeFlangeInput`](https://help.autodesk.com/cloudhelp/ENU/Fusion-360-API/files/fusion_FlangeFeatures_createEdgeFlangeInput.htm)
API is preview and Autodesk says not to distribute programs using it. The
`create_flange` compatibility defect therefore remains unresolved; release
remains blocked, and a preview-only exclusion must not be represented as a
successful supported-tool test.

### Edge-flange preview implementation

The project owner accepted the preview API risk. `create_flange` now uses
`createEdgeFlangeInput` and applies the optional bend-radius override; it
rejects non-sheet-metal bodies and reports when the API is unavailable.
Autodesk introduced the preview API in October 2026 and warns it can change.
Headless tests cover inputs, the override and its failure. A successful live
run on a genuine sheet-metal body is still required; the earlier solid was not
such a body. Windows remains untested.

Source commit `4609ee8` was packaged as add-in zip SHA-256
`80a6ea929f01dc10bf1f2c165a9a06463c9ea7bd28d17753e382ccae7708094a` and
installed in Fusion 2706.1.17; its installed handler matched the archive.
On saved test design `Codex MCP E2E flange preview 2026-11-04`, a regular
`create_box` succeeded at revision 1; `create_flange` on that body (operation
ID `c6b8e08e-a700-4d1a-9aef-b25505cb78b7`) rejected it as non-sheet-metal,
and the scene stayed at revision 1. This is a negative-input check, not a
successful flange E2E. The 25-test headless suite passed; a real sheet-metal
body is still required to validate the preview API success path.
