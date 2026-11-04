# Specification: stable release with a Python Fusion add-in

Status: approved direction, implementation in progress. Related plan:
[stable-release.md](../plans/stable-release.md).

## Intent and decisions

The stable release gives Windows and macOS Fusion users one installable add-in
with the same 92 typed MCP tools. Windows installer CI runs before release; live
Windows Fusion E2E follows after release. The .NET server and its
durable operation journal remain. This removes the platform split in which
the former C# add-in served Windows and a separately sourced Python add-in
served macOS.

The approaches considered were (1) keep two add-ins and maintain two behavior
contracts, (2) ship the pinned Python add-in on both hosts while retaining the
C# MCP server, and (3) replace both the add-in and server with upstream Python.
The chosen approach is (2): it preserves the server's typed catalog and journal
while making the Fusion component cross-platform. The C# add-in has been
removed from the repository. Windows live Fusion E2E is scheduled after release
because no Windows Fusion host is available.

Non-goals for this release are Linux Fusion support, adding `execute_code`,
changing the 92 tool names or units, and TCP authentication. A release that silently drops Windows support or loses mutation journal data
fails this specification even if local tests pass. The project owner has
explicitly deferred live Windows Fusion E2E until after release; this known
validation gap must be disclosed in release notes and completed against the
published candidate.

## Goal

Release the C# MCP server with one packaged Python Fusion add-in for Windows
and macOS, with the advertised 92-tool behavior verified on macOS before
release and Windows after release.

## Acceptance criteria

- A clean checkout builds and packages the server and pinned Python add-in
  without fetching source code during installation.
- Every advertised tool dispatches; revision, mutation, timeout, journal,
  verification, and network contracts below have automated coverage.
- Fresh-install and saved-design E2E on macOS passes on the candidate
  artifacts, with exact versions and results recorded. Windows installer CI
  passes before publication; live Windows Fusion E2E is recorded after release.
- CI, release assets, checksums, installation guides, and known limitations
  match the same release commit. The retired C# add-in is absent from active
  build, install, and user guidance.

## Current state

Commit `1750af1` contains the pinned Python add-in, headless contract tests,
both installer scripts, the release zip builder, and CI/release workflow
changes. The C# add-in has been removed. macOS live validation remains a release gate.
Windows live validation is deferred until after release because a Windows Fusion
host is unavailable. macOS evidence remains pending; Windows is marked deferred.
The changed workflows have not yet been exercised on GitHub runners. The commit also includes timeout edits that
predated the release work; their provenance matters in review.

## Required behavior

### 1. Supported product and distribution

The product consists of the .NET 10 MCP server, shared Core library, and one
Python add-in package installed inside Fusion on Windows or macOS. The MCP
server connects over newline-delimited JSON/TCP to port 9876 by default and
serves 92 typed tools over stdio. The supported tool list is the generated
catalog; `execute_code` is rejected by both this server's catalog and the
packaged add-in's raw TCP dispatch.

The repository contains the exact Python add-in source under
`addon/Fusion360MCP/` for each release. Its package includes a Fusion
manifest with `supportedOS: windows|mac`, source,
upstream commit attribution, and the upstream MIT license. A release identifies
the server and add-in versions together and provides a checksum for each
download. The add-in must not download code during installation or startup.

### 2. Wire and tool contract

Requests use `{"type":"<tool>","params":{...}}` followed by `\n`.
Responses use the shapes in [TOOL_CONTRACTS.md](../TOOL_CONTRACTS.md): a
`status: success` frame with a result containing `ok`, or a structured
`status: error` frame. Unknown commands and malformed input must yield
structured errors without killing the listener. Each advertised tool name
must route to a handler; the add-in must not silently ignore a public
parameter. Input and output units remain as stated in the catalog.

The server owns the durable `operation_id` journal. It writes an intent before
sending a mutation, replays a recorded successful result for a matching ID,
rejects changed arguments or unsafe same-ID retries, and keeps journal data
through restart and upgrades. Read-only calls bypass the journal. The add-in
may receive `operation_id` but must not treat it as a new model parameter.

### 3. Model revision and mutation results

`get_scene_info` returns `model_revision` for the active design. Document
mutations accepting optional `expected_revision` check it before any Fusion
change. A mismatch returns `ok: false`, `REVISION_CONFLICT`, current
`revision_after`, and `partial_effects: false`. A successful applied mutation
advances the revision and returns `revision_after`. An observed partial change
also advances the revision and returns it with the failure; other failures
return the current revision when it can be read. A previously observed name that no
longer resolves after a tool-driven change may produce `STALE_REFERENCE`.

The revision counter is lightweight and process-local. It keys each design by
`parentDocument.creationId` when available and otherwise by the document
object's process-local identity. Stopping and starting the add-in in the same
Fusion process retains this state; restarting Fusion resets it to zero. It
does not track edits made through Fusion's UI. Contract tests must cover
separate documents, matching and stale revisions, failed mutations, and
restart semantics.

Mutation results preserve `deltas` where available. Failed mutations report
`partial_effects: false` only when no model or external effect occurred;
report `true` when an effect is observed; omit it when uncertain. Optional
`verification` contains `level`, `checks`, and `unverified_aspects`, using the
tolerances in [TOOL_CONTRACTS.md](../TOOL_CONTRACTS.md). A verification level
may assert only a check actually performed. These fields must survive the
server's MCP formatting path.

### 4. Timeouts, retries, and shutdown

The intended command budget is 300 seconds in the Python add-in and 310
seconds in the C# TCP client; ping uses 5 seconds. All active constants,
messages, docs, and tests must agree. The journal's in-flight barrier must
cover the command budget or return an explicit still-in-flight conflict
without starting a duplicate operation.

The bridge tracks queued, executing, completed, and cancelled work with an
atomic or locked transition. On expiry before execution, it cancels the item,
guarantees that the main-thread handler cannot later start it, and returns a
`status: success` frame containing `result.ok: false`, `TIMEOUT`, and
`partial_effects: false`. On expiry after execution starts, it returns the
same frame shape with `TIMEOUT` and `outcome_unknown: true`; it never claims
no effects. The server never automatically retries a mutation after sending
it. A lost connection to the add-in also means an unknown mutation outcome.
Read-only requests may retry after reconnect. Stopping the add-in wakes queued
callers and does not leave work to execute after restart.

### 5. Network exposure

The add-in listens on loopback by default. `FUSION_MCP_HOST` and
`FUSION_MCP_PORT` configure its listener; the MCP server uses the same names
for the destination. Before binding any non-loopback address, including
`0.0.0.0`, the add-in requires `FUSION_MCP_ALLOW_REMOTE=1`. Without that
value, startup fails with a clear diagnostic and no listener. The TCP wire
protocol has no authentication. Documentation describes LAN use only on a
trusted network and does not imply that opt-in provides authentication.

### 6. Installation and upgrade

Windows uses a PowerShell installer and macOS uses a shell installer. Both
install the same packaged Python source in Fusion's AddIns directory, validate
the manifest and required files, stage the package, and preserve the previous
working installation if validation or replacement fails. A development
symlink is an explicit option, not the default release path. Neither installer
deletes designs, exported files, or the server journal. They report the
installed version and next steps: start or restart Fusion, run the add-in if
needed, then call `ping` and `get_scene_info`.

Upgrade instructions explain how to stop any installed legacy C# add-in, avoid two
listeners on port 9876, install the Python add-in, preserve the operation
journal, and verify the new installation. Uninstall instructions remove only
the installed add-in package and client configuration unless the user
explicitly chooses to remove journal data.

### 7. CI, artifacts, and release behavior

A pull request runs .NET Core/Server tests, Python add-in unit and protocol
tests without Fusion, 92-tool catalog/dispatch parity, docs/link guards,
installer/package validation, and a clean build. CI must run from checked-in
sources without a local `reference/` clone. A release candidate attaches
four server binaries (`win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`), one
Python add-in zip, checksums, and release notes; the Docker image is built
from the same commit. A failed required check prevents publication. A push to
`main` alone must not create a stable release before the live gates below are
recorded and approved.

### 8. Live E2E acceptance matrix

Run this matrix on macOS Fusion using the candidate add-in package and server
artifact before release. Record OS, Fusion version, artifact
checksums, design name, commands, responses, and defects. Use a named, saved
design for stateful cases and save again before restarting Fusion.

| Area | Required observation |
| --- | --- |
| Fresh install | Add-in appears in Fusion, starts once, `ping` returns `pong`, `get_scene_info` returns active-design data. |
| Geometry | Create a sketch and a solid, query the resulting body, edit it, and verify counts/dimensions and documented units. |
| Revisions | Matching `expected_revision` succeeds; stale value rejects before change; separate designs and restart follow documented semantics. |
| Journal | Same `operation_id` replays success without a second model change; changed arguments conflict; journal survives server restart. |
| Failure effects | An invalid mutation leaves the scene unchanged and reports the appropriate error; a controlled partial/unknown case does not permit unsafe retry. |
| Timeout race | A queued expiry cannot execute later; an executing expiry is marked unknown. This may be demonstrated by an instrumented bridge test if reliably inducing it in Fusion is impractical. |
| Export and perception | Export a file, inspect it, call `render_view`, and verify image output through the MCP server. |
| Network | Loopback works by default; non-loopback bind fails without opt-in and succeeds with opt-in on a trusted test network. |
| Recovery | Stop/restart the add-in and server; reconnect, inspect the saved design, and verify journal handling. |

All 92 tools require automated catalog-to-handler dispatch coverage. A smaller
live smoke set is acceptable only when it covers every major tool group plus
the high-risk behaviors above; the exact commands and excluded preview-only
APIs are recorded before the candidate run. A failing macOS candidate case blocks the stable release until fixed and rerun.
Run the same matrix on Windows Fusion after release and record any corrections
as a follow-up candidate.

### 9. Documentation and release notes

Current README, INSTALL, ARCHITECTURE, E2E runbook, tool catalog, Docker
instructions, and troubleshooting must all describe the Python add-in on both
hosts. They must include supported Fusion versions established by E2E, install
and upgrade commands, port and remote-bind rules, journal persistence,
timeouts and retry semantics, preview API limitations, and known issues.
Historical ADRs may retain the C# implementation history but must identify
which paths were retired. Empty status/governance placeholders are removed or
filled with accurate content rather than linked as release guidance.

## Verification record

Local verification on 2026-11-04: twelve Python headless tests passed; 92-tool
dispatch/effect parity and the documentation guard passed; the macOS installer
passed a repeated-install check in a temporary AddIns directory, including
installation from the release zip. The solution built with zero warnings.
After the formatter fix, the C# server suite passed 84 tests; the Core suite
passed 120 and the legacy add-in suite passed 186 before that formatter-only
fix. The full publish script produced all four server archives, the Python
add-in zip, and valid SHA-256 checksums. These checks were run before commit
`1750af1`; that commit has not yet been exercised on a GitHub runner or in
live Fusion.

Before closing the release task, record the release commit and candidate hashes,
CI run, macOS Fusion version and E2E commands/results, and defects and reruns.
Schedule the Windows Fusion matrix against the published artifacts after release.
Headless and unit checks do not satisfy the macOS live E2E criterion.

## Next step

Follow the remaining tasks in the [implementation plan](../plans/stable-release.md):
review the candidate, run the installer jobs on GitHub runners, and execute the
complete candidate E2E matrix in macOS Fusion. The C# add-in has been removed.
Run the live Windows Fusion matrix against the published candidate after release.
