# Stable release E2E evidence

Status: pass
Windows: deferred (post-release)
macOS: pass
Candidate version: 1.0.0
Candidate source commit: bca69cbb3a16f4eaee09272d4e39e233ac53e8ab
Add-in SHA256: 933f91f46add9c21dfa14f0c89044be6f7302d120504a940b0651a4e06d100e7
Server win-x64 SHA256: f2b73742f4e078e8c11280d322867732133add439d612ab10df8a7bfd825ec4d
Server osx-arm64 SHA256: 93ed14b637df69ecf5eef77a62e1c9e655a7c84325d9d001ed5264ca6c50e4a3

Windows Fusion E2E has not been run because a Windows Fusion host is currently
unavailable. Per the release decision, this live matrix is deferred until after
release; Windows installer CI remains required before publication. The
post-release Windows run must use the published candidate and record its exact
Fusion version and artifact checksums here.

Record the candidate commit, server and add-in artifact SHA-256 values, OS and
Fusion versions, named saved design, each command and observed response from
the matrix in [the release task](../tasks/python-addin-stable-release.md),
defects, and affected-case reruns. The add-in hash must match the reproducible
release zip built from the tagged source. Change a platform line to `pass` only after
its required run is complete or the project owner records an explicit scoped
deferral. Set `macOS: pass` after its release scope is complete. Keep Windows as `deferred (post-release)` for publication; change it to
`pass` only after the live post-release run. Project owner approved deferring
the trusted-network opt-in check and remaining catalog/runbook rows until the
next release. Therefore `Status: pass` and `macOS: pass` below describe the
approved 1.0.0 release scope and do not claim those deferred checks were run.
The deferred checks remain explicit follow-up work.

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
At that point the `create_flange` defect was unresolved; the later retest
below records the preview implementation and its outcome.

### Edge-flange preview implementation

The project owner accepted the preview API risk. `create_flange` now uses
`createEdgeFlangeInput` and applies the optional bend-radius override; it
rejects non-sheet-metal bodies and reports when the API is unavailable.
Autodesk introduced the preview API in October 2026 and warns it can change.
Headless tests cover inputs, the override and its failure. A successful live
run on a genuine sheet-metal body was still required at that point; the later
retest below records it. Windows remains untested.

Source commit `4609ee8` was packaged as add-in zip SHA-256
`80a6ea929f01dc10bf1f2c165a9a06463c9ea7bd28d17753e382ccae7708094a` and
installed in Fusion 2706.1.17; its installed handler matched the archive.
On saved test design `Codex MCP E2E flange preview 2026-11-04`, a regular
`create_box` succeeded at revision 1; `create_flange` on that body (operation
ID `c6b8e08e-a700-4d1a-9aef-b25505cb78b7`) rejected it as non-sheet-metal,
and the scene stayed at revision 1. This is a negative-input check, not a
successful flange E2E. The 25-test headless suite passed; a real sheet-metal
body was still required to validate the preview API success path; see the
later retest below.


### Live edge-flange and flat-pattern retest

On 2026-11-04, macOS 15.7.4 with Fusion 2706.1.17 ran the installed add-in
from source `4609ee8`, zip SHA-256
`80a6ea929f01dc10bf1f2c165a9a06463c9ea7bd28d17753e382ccae7708094a`.
A temporary local test add-in created and saved the disposable design `Codex
MCP flange fixture 2026-11-04`, with a 4 × 2 cm base flange, `Steel (mm)`
rule, and a genuine sheet-metal Body1. The fixture add-in and its registration
were removed after the test; the saved design remains.

`create_flange {"body_name":"Body1","edge_index":8,"height":1,"angle":90,
"expected_revision":0,"operation_id":"3a23db3e-4f6a-4949-a7f9-54c18122bb3c"}`
returned `EdgeFlange2`, revision 1. Body volume increased from 2.0 to
approximately 3.089 cm³ and the bounding box grew; a rendered image showed
the flange. `flat_pattern {"body_name":"Body1","expected_revision":1,
"operation_id":"499ff79b-f6de-455f-84dd-45a7f4755cc2"}` returned
`FlatPattern1`, revision 2. The model was saved. These results confirm the
calls completed on one macOS build, not measured dimensional verification or
Windows compatibility. The full candidate matrix and Windows E2E remain pending.

## Candidate smoke — 2026-11-05 (incomplete; not release evidence)

Fusion on macOS responded to `ping` with `pong`. The installed add-in handler
SHA-256 is `0c6a3d072dd395fb519b358a77f4b9717bf17413d69054570629725284a3797d`,
matching the handler extracted from the candidate add-in zip. The candidate
source is `af7f8639aee59842a996b44203170bb75ce7c270`; the local add-in zip SHA-256
is `1f20880ad5e368a9a0fe8c865f62ddc8b52c991830b8a32bce969f6632e38ed3`.
The live Fusion MCP server process could not be identified from this environment,
so the running server has not been proven to be the candidate macOS archive.
The candidate archives are local builds; Task 6's successful Actions artifact
and cross-build hash comparison are still outstanding.

On the named saved design `Codex MCP Release E2E af7f863 2026-11-05`, the
connected endpoint began at revision 0. `create_sketch` (xy),
`draw_rectangle` (4 × 2 cm), and `extrude` (1 cm, new body) succeeded at
revisions 1–3. Exact replay of the extrude returned the recorded result at
revision 3 without a second body. Reusing its operation ID with height 2
returned `OPERATION_ID_CONFLICT`. A `create_sketch` request with revision 0
returned `REVISION_CONFLICT` and `partial_effects: false`.

`move_body` moved Body1 by +1 cm in X at revision 4; `create_box` added Body2
at revision 5; `create_construction_plane` added Plane1 at revision 6;
`set_color` set Body1 red at revision 7; `create_parameter` added
`E2E_Length = 10 mm` at revision 8. A second sketch and rectangle were made
at revisions 9–10; `patch_surface` created SurfacePatch1 at revision 11.
`get_scene_info` confirmed three bodies, two sketches, four features and
revision 11. `get_physical_properties` returned Body1 volume 8 cm³, area
28 cm² and mass 0.0628 kg; `get_parameters` returned the 10 mm expression;
`cam_list_setups` returned zero setups. `export_f3d` wrote
`/private/tmp/fusion360mcp-e2e-af7f863.f3d`; `render_view` returned a 640 × 480
PNG. The saved model is disposable and named as above.

This run is incomplete. The active document was not an Assembly, so
`create_component` with the required Assembly precondition has not been tested.
Body1 is an ordinary extruded solid, so `flat_pattern` rejected it as not sheet
metal; this is not a success-path test. The invalid-body `move_body` request
returned `BODY_NOT_FOUND` with `outcome_unknown: true`; a subsequent scene read
still showed revision 11 and the same three bodies. CAM toolpath creation,
network opt-in, candidate server identity, journal restart on the candidate,
the complete matrix, and Windows Fusion E2E remain outstanding for this older
smoke. Keep `macOS: pending` and overall `Status: pending` until those release
gates pass.

### Actions candidate validation — 2026-11-05 (partial; not release evidence)

Candidate source `bca69cbb3a16f4eaee09272d4e39e233ac53e8ab`, version `1.0.0`,
was packaged by successful `main` CI run
[41974563658](https://github.com/qdimka/fusion360-mcp/actions/runs/41974563658).
The Actions artifact internal hashes were independently reproduced by a second
Ubuntu build of the same source commit (artifact IDs `13391261132` and
`13391074210`). The same five ZIP hashes matched across both runs:

| Candidate artifact | SHA-256 |
| --- | --- |
| `Fusion360MCP-python.zip` | `933f91f46add9c21dfa14f0c89044be6f7302d120504a940b0651a4e06d100e7` |
| `Fusion360.Mcp.Server-linux-x64.zip` | `85af1ee1d0de281ae2d0f01a15cfb0d131774535d7b48dbaad1c10afbf99ca6a` |
| `Fusion360.Mcp.Server-osx-arm64.zip` | `93ed14b637df69ecf5eef77a62e1c9e655a7c84325d9d001ed5264ca6c50e4a3` |
| `Fusion360.Mcp.Server-osx-x64.zip` | `ad868f17b73bd5ea4405c4615ab7e825b9f58a91073627ebdd9f20921122fae5` |
| `Fusion360.Mcp.Server-win-x64.zip` | `f2b73742f4e078e8c11280d322867732133add439d612ab10df8a7bfd825ec4d` |

Two local macOS arm64 publishes from this same source also matched each other:
server archive `87305ad6563396190d13986b6ee5864c6b0db69f1317b97dbaa1b2cf71145b61`
and add-in archive `933f91f46add9c21dfa14f0c89044be6f7302d120504a940b0651a4e06d100e7`.
The macOS-host server archive differs from the Ubuntu-host archive; use the
tested Ubuntu Actions archive for the candidate record.

On macOS 15.7.4 with Fusion 2706.1.17, the Python add-in was installed from the
Actions ZIP and its handler SHA-256 (`bd7ea3306af7282db76ca86d6587f536c697c974b810364b12caaad04dae976a`)
matched the extracted archive. The extracted Actions macOS arm64 server
executable SHA-256 is `48b91da62d30400420e28988227a7657721a9ac3ab726a68d2c22e9d1ade3297`.
That exact server initialized as MCP `1.0.0` and connected to the live add-in
over loopback using isolated journal `/private/tmp/fusion360mcp-journal-bca69`.

On saved disposable design `Codex MCP Release E2E af7f863 2026-11-05`, starting
at revision 11, candidate-server `ping`, `get_scene_info`, `get_design_type`,
`get_physical_properties` for Body1, `cam_list_setups` (empty list),
`render_view` (640 × 480 PNG), and `export_f3d` all returned successfully; the
export exists at `/private/tmp/fusion360mcp-e2e-bca69.f3d` (92,688 bytes).
An invalid-body `move_body` returned `BODY_NOT_FOUND`; `get_scene_info` still
showed revision 11 and the same bodies. A stale revision returned
`REVISION_CONFLICT` with `partial_effects: false`. A new `create_box` advanced
revision 11 to 12 and increased body count once. Repeating that exact request
returned the journaled result; changing its dimensions with the same operation
ID returned `OPERATION_ID_CONFLICT`. After stopping and starting the exact
candidate server twice with the same journal directory, the original request
still replayed at revision 12 without adding another body.

In the saved Assembly Design `Codex MCP E2E real assembly 2026-11-04`, the exact
candidate returned `get_design_type: parametric`; the document had been created
with Fusion's Assembly Design selector. `create_component` created
`E2EComponent_bca69` at revision 1, and the scene timeline count increased.
On this same disposable document the exact candidate then created a sketch and
2 × 1 cm rectangle, extruded Body1 (1 cm), moved Body1 (+1 cm X), created a
2 × 1 × 1 cm box, created Plane1 (XY offset 2 cm), set Body1 red, created
`E2E_bca69_Length = 10 mm`, created a second sketch/profile, and patched it as
SurfacePatch1. These mutations advanced revisions 1 through 12; scene reads
confirmed Body1, Body2, and the zero-volume surface body. The Assembly was
saved after the run.

The exact candidate's first `flat_pattern` call on the genuine sheet-metal
Body1 in `Codex MCP flange fixture 2026-11-04` returned `The component already
has a flat pattern`; revision stayed 0. The disposable Sheet Metal Design
`Codex MCP Release E2E sheet metal candidate bca69 2026-11-05` was created
with a 4 × 2 cm profile and a base flange in Fusion UI. Its existing Flat
Pattern was removed in the Fusion browser to reset the fixture, then the exact
candidate's successful creation is recorded below.
An exploratory `create_flange` request used unsupported argument `length`
instead of the catalog's `height`; the handler returned `TypeError` with
`outcome_unknown: true`. A subsequent scene read confirmed the saved design
still had zero bodies at revision 2. This was a malformed E2E client request,
not a candidate API result; no retry was made because the operation outcome was
unknown.

The extracted Actions add-in ZIP installer also passed locally using a
temporary AddIns directory; installed and extracted package trees matched.
`python3.12 -m unittest discover -s tests/python_addin -v` passed 32 tests and
`python3.12 scripts/check_guards.py` passed. Remaining release evidence includes
a new-body `flat_pattern` success, the trusted network opt-in check, remaining
catalog/runbook rows and exact requests/responses, and the complete candidate
rerun. Windows Fusion E2E remains deferred until after release. This evidence
is partial; overall status stays pending.

### Exact MCP JSON-RPC rerun — 2026-11-05 (partial)

Launched the extracted Actions `osx-arm64` executable (`1.0.0`) with
`FUSION_MCP_JOURNAL_DIR=/private/tmp/fusion360mcp-journal-exactrun`, initialized
MCP protocol `2025-06-18`, and verified `ping: pong`. The running add-in handler
SHA-256 matched the Actions Python ZIP. All calls below went through that
candidate server to the live Fusion add-in. The active saved disposable
Assembly was `Codex MCP E2E real assembly 2026-11-04`, revision 24, 6 bodies,
4 sketches. After each mutation, `get_scene_info` confirmed the revision and
counts recorded below.

| Exact `tools/call` arguments | Result |
| --- | --- |
| `create_component` `{name: E2EExactCandidateComponent_20261105, expected_revision: 24, operation_id: d8aec10f-7e8e-4baf-90e8-b1b71af27200}` | OK, revision 25 |
| `create_sketch` `{plane: xy, expected_revision: 25, operation_id: d6518bce-4a59-4868-9755-213417864b22}` | Sketch5, revision 26 |
| `draw_rectangle` `{width: 2, height: 1, origin_x: 40, origin_y: 40, expected_revision: 26, operation_id: 8f7e412b-f6d3-4ec6-9a4a-599c5b7fc21f}` | Sketch5 profile, revision 27 |
| `extrude` `{height: 1, profile_index: 0, operation: new_body, expected_revision: 27, operation_id: 5c5ba2ba-d476-44cc-9eb4-e0420021f1cc}` | Extrude3, Body7, revision 28 |
| `move_body` `{body_name: Body7, x: 1, y: 0, z: 0, expected_revision: 28, operation_id: 1d82ce33-7f65-4c3e-b38a-f8b64afc0347}` | Move3, revision 29 |
| `create_box` `{length: 2, width: 1, height: 1, center_x: 60, center_y: 0, center_z: 0, expected_revision: 29, operation_id: 235e0036-50e2-43c8-9ad5-f14b88358b18}` | Body8, body count 7→8, revision 30 |
| `create_construction_plane` `{method: offset, plane: xy, offset: 2, expected_revision: 30, operation_id: e6fc8252-a901-41ef-86d2-4a4744798d35}` | Plane3, revision 31 |
| `set_color` `{body_name: Body7, red: 255, green: 0, blue: 0, expected_revision: 31, operation_id: 7a02b31b-9c8d-4654-b660-346050c4a35b}` | Red appearance, revision 32 |
| `create_parameter` `{name: E2EExact_Length_20261105, value: 10, unit: mm, expected_revision: 32, operation_id: 773b670f-7732-495a-8e3b-b0bceac88fd6}` | Fusion rejected the name (`3 : param name is not valid`), unknown outcome flagged; readback confirmed revision 32 and unchanged counts. Retried with new ID `d92c61de-6802-425c-ab1c-b0b190242c79`, name `LengthE2E20261105`, value 10 mm, expected revision 35: OK, revision 36. |
| `create_sketch` `{plane: xy, expected_revision: 32, operation_id: 653eea65-98b6-4e9a-8dd9-9fe3522383f8}` | Sketch6, revision 33 |
| `draw_rectangle` `{width: 2, height: 1, origin_x: 80, origin_y: 80, expected_revision: 33, operation_id: 04d57beb-5379-43d4-8cc0-c4b8cd55e799}` | Sketch6 profile, revision 34 |
| `patch_surface` `{sketch_name: Sketch6, profile_index: 0, continuity: connected, expected_revision: 34, operation_id: 8f9ce8c6-e6bf-49e0-aa46-13162d7d5d67}` | SurfacePatch3, Body9, revision 35 |
| `get_design_type` `{}` | `parametric`, ID 1 |
| `get_physical_properties` `{body_name: Body1}` | mass 0.0157, volume 2 cm³, area 10 cm², density 0.00785, center [2, 0.5, 0.5] |
| `cam_list_setups` `{}` | Empty list, count 0 |
| `export_f3d` `{file_path: /private/tmp/fusion360mcp-exactcandidate-20261105.f3d, operation_id: a0401735-4b5e-4cd4-b1e1-dd5fc199c1aa}` | OK, 134,101 bytes; SHA-256 `862a5cc91a2e958cf2fde1af8f6c72040ab598236c5b4e287f68a55bc4125b07` |
| `render_view` `{view: iso, width: 640, height: 480, fit: true}` | OK, PNG 79,416 bytes; image content block returned |
| Replay `create_box` with original arguments and operation ID, expected revision 29 | Journal returned the original success at revision 30; no second body |
| `create_box` changed `length` to 3 with same operation ID | `OPERATION_ID_CONFLICT` |
| `create_sketch` `{plane: xy, expected_revision: 34, operation_id: af2cdd32-742a-441e-822c-b085a4d3a940}` | `REVISION_CONFLICT`, current revision 35, `partial_effects: false` |
| `move_body` `{body_name: NoSuchBody, x: 1, y: 0, z: 0, expected_revision: 35, operation_id: 0c88ec27-1543-4bea-9ab0-e59f5b22b1e4}` | `BODY_NOT_FOUND`, `outcome_unknown: true`; follow-up read confirmed revision 35, 9 bodies, 6 sketches |

The Assembly document was saved. The missing-body failure and invalid parameter
name are retained as error evidence. The stale revision was rejected without
effects. The exact candidate server's `flat_pattern` success path is verified
below. Trusted network opt-in and remaining catalog rows also remain pending;
Windows Fusion E2E stays deferred until after release. Overall status remains
pending.

After stopping and restarting the exact Actions server with the same isolated
journal directory, MCP initialized again as `1.0.0`; replaying the original
`create_box` request above returned its journaled revision-30 success and did
not add another body.

### Sheet metal flat-pattern success — 2026-11-05

The exact Actions server used the isolated journal and local TCP access to
Fusion's running add-in on `127.0.0.1:9876`; `ping` returned `pong`. Before this
call, `get_scene_info` read the saved sheet-metal fixture
`Codex MCP Release E2E sheet metal candidate bca69 2026-11-05`: Body1, one
sketch/profile, revision 3. The existing flat-pattern feature was cleared
from the disposable fixture in the Fusion UI. The exact candidate request
`flat_pattern {body_name: Body1, expected_revision: 3, operation_id: e3882324-b2f2-4ec0-9bf0-dcc10f2d30ca}`
returned `OK`, `feature_name: FlatPattern1`, `revision_after: 4`, body count
1→1, and mass 15.700 g→15.700 g. Follow-up `get_scene_info` returned revision
4 and the same one body; the Fusion browser visibly showed the `Flat Pattern`
entry again. The document was saved. This closes the candidate flat-pattern
success-path check; the full macOS runbook and trusted-network opt-in gate
remain open.
