# Release readiness plan

Status: in progress. Target: first stable release of the C# MCP server with
one Python Fusion add-in for Windows and macOS. This plan does not itself
publish a release.

## Goal and scope

Ship a reproducible server and add-in pair whose documented 92-tool MCP catalog
matches actual add-in behavior on both supported Fusion hosts. Keep the C# MCP
server and shared Core library. Retire the C# add-in, its test project, the
compile-time adsk stub, and the Windows-only installer after the replacement
passes the gates below. `execute_code` remains outside this server's catalog.

The behavior to deliver is defined in the
[release task](../tasks/python-addin-stable-release.md), the existing
[tool contracts](../TOOL_CONTRACTS.md), and the generated
[tool catalog](../TOOLS.md). If they disagree, resolve the discrepancy before
marking a gate complete.
The working [compatibility inventory](../status/python-addin-compatibility.md)
records what has been checked and what still requires live evidence.

## Work sequence

| Phase | Work | Evidence required to finish |
| --- | --- | --- |
| 0. Baseline | Record the current commit and the pre-existing uncommitted timeout changes separately. Inventory the 92 tools, current C# add-in behaviors, Python add-in behaviors, installers, and release artifacts. Choose and record the Python upstream commit and its MIT license attribution. | A reviewed compatibility matrix and an explicit list of differences; no existing work overwritten. |
| 1. Python add-in source | Put a pinned, reviewable copy of the Python add-in under this repository's version control. Apply project-specific changes there, with an update procedure that records the upstream commit and local patches. | A clean checkout contains the exact add-in source needed to install; no network clone is required at install time. |
| 2. Contract parity | Implement or deliberately revise every advertised server/add-in contract: revisions and stale references, mutation diagnostics, verification, structured timeouts, operation IDs, and tool dispatch. Keep wire compatibility with the C# server. | Automated contract tests for the catalog and response shapes; regression tests for conflicts, timeout races, and partial effects. |
| 3. Installation and security | Provide idempotent Windows PowerShell and macOS shell installers for the same Python package. Require explicit opt-in for non-loopback listening. Document upgrade and uninstall without deleting user designs or journal data. | Installer checks on both OSs, loopback/remote-bind checks, and a fresh-install walkthrough. |
| 4. Retire C# add-in | Remove the C# add-in project and tests from the solution and CI only after phases 1–3 pass. Remove its source, installer, stub assemblies, and packaging references. Keep decision history, updating historical paths with a migration note where needed. | The solution builds from a clean checkout without `net48` or adsk assemblies; no active guide points to the removed add-in. |
| 5. Release pipeline and docs | Make CI run Python unit/contract tests, C# tests, parity and guard checks, installer/package validation. Package the Python add-in zip and server binaries; attach artifacts to a release and publish the image from the same commit. Rewrite README, INSTALL, ARCHITECTURE, E2E runbook, tool contracts, and release notes. | A candidate release produces downloadable, installable artifacts with checksums; docs match those artifacts and commands. |
| 6. Live validation | Run the E2E matrix in the release task against the candidate artifacts on Windows Fusion and macOS Fusion. Test normal operations and adverse cases using saved, named designs. | Recorded OS/Fusion versions, artifact hashes, commands, observed results, failures, and issue links. |
| 7. Stable release | Fix release-blocking defects, repeat affected checks, freeze the candidate, approve notes, then publish the stable version (proposed `1.0.0`). | All release gates below pass on the release commit and tag; no untriaged blocker remains. |

## File change map

Paths below are the intended implementation locations. New paths are proposed
names; existing paths are current repository files. This map does not itself
authorize removing the C# add-in before its replacement passes phases 1–3.

| Path | Change | Phase |
| --- | --- | --- |
| addon/Fusion360MCP/Fusion360MCP.py, Fusion360MCP.manifest, server/*.py (new) | Add the pinned Python add-in source and upstream license/commit record. Extend dispatch, revision tracking, mutation diagnostics, timeout state transitions, and remote-bind guard. The directory is the package installed into Fusion on both OSs. | 1–3 |
| addon/UPSTREAM.md, addon/LICENSE.upstream (new) | Record the upstream commit, license, copied files, and procedure for reviewing future updates. | 1 |
| `src/Fusion360.Mcp.Core/Protocol/ProtocolConstants.cs`, `Fusion360Connection.cs`; `src/Fusion360.Mcp.Core/Errors/ErrorClassifier.cs` | Set the C# client budget and all timeout hints to the agreed Python bridge budget. Keep the wire format and tool names stable. Reconcile the pre-existing uncommitted edits to these files rather than replacing them. | 2 |
| `src/Fusion360.Mcp.Server/OperationJournal.cs`, `IdempotentToolBackend.cs` | Align in-flight waiting and structured Python timeout handling with safe same-ID retry rules; change only where contract tests show a gap. | 2 |
| `src/Fusion360.Mcp.Core/Catalog/tools_catalog.json`, `ToolEffects.cs`; `src/Fusion360.Mcp.Server/ResultFormatter.cs` | Keep the existing 92-tool schema and effect metadata unless parity reveals a false contract. Verify `expected_revision`, `operation_id`, and diagnostic fields through to MCP output. | 2 |
| `scripts/check_tool_parity.py`, `generate_tool_catalog.py`, `generate_tool_listing.py` | Make the checked-in catalog and checked-in Python dispatch the release inputs. Remove the CI requirement for the ignored `reference/` clone. Keep an explicit, reviewed upstream import path for future catalog updates; regenerate `docs/TOOLS.md` only if the catalog changes. | 2, 5 |
| `tests/python_addin/` (new); `tests/Fusion360.Mcp.Server.Tests/SocketModeServerTests.cs`, `IdempotentToolBackendTests.cs`, `OperationJournalTests.cs`; `tests/Fusion360.Mcp.Core.Tests/ProtocolTests.cs` | Add headless Python protocol/bridge/revision/security tests and C# tests for actual Python response frames, timeouts, and journal behavior. | 2–3 |
| `scripts/install-addon.sh`, `scripts/install-addon.ps1` | Replace the reference symlink and C# build installers with release-package installation of the same Python add-in. Preserve the previous install on failure; allow an explicit development symlink mode. | 3 |
| `src/Fusion360.Mcp.AddIn/`, `tests/Fusion360.Mcp.AddIn.Tests/`, `thirdparty/adsk/` | Delete after the Python replacement is verified. Port the contract assertions from the C# add-in tests into Python tests before deleting their source. | 4 |
| `Fusion360Mcp.sln`, `Directory.Build.props`, `scripts/check_guards_baseline.json` | Remove the add-in projects and net48-only references/style debt. Change the shared version only at candidate/stable release time. | 4, 7 |
| `.github/workflows/ci.yml`, `.github/workflows/docker.yml`, `scripts/publish.sh` | Run all checked-in C#/Python gates; package and attach server binaries and Python add-in zip with checksums; tie release/image publication to the validated commit rather than every version bump on `main`. | 5 |
| `README.md`, `docs/INSTALL.md`, `docs/ARCHITECTURE.md`, `docs/TOOL_CONTRACTS.md`, `docs/DOCKER.md`, `docs/E2E-STATUS.md`, `docs/status/TECH_DEBT.md` | Describe one add-in, actual security and timeout behavior, migration, supported versions, test evidence, and remaining debt. Reconcile the already modified architecture and decision documents rather than overwriting them. | 5–6 |
| `docs/decisions/*.md`, `docs/adr/*.md` | Keep historical decisions; add a short supersession/migration note where a document claims the retired C# implementation is current. | 5 |
| `docs/STATUS.md`, `docs/GOVERNANCE.md` | Resolve the currently empty placeholders: either fill with accurate release status/governance and link them, or remove them and repair references. | 5 |
| docs/status/e2e-stable-release.md (new) | Record the Windows and macOS candidate runs, environment, artifact hashes, commands, results, defects, and reruns. | 6 |
| `Dockerfile`, `docker-compose.yml`, `src/Fusion360.Mcp.Server/Program.cs` | Retain unless packaging or integration checks expose a concrete mismatch; Docker carries only the C# server. | 5 |

The main deletion is deliberately late: first the replacement and its tests,
then the old implementation and build references in one reviewable change.

## Release gates

1. **Behavior:** All 92 advertised tools have a dispatch target and schema
   check. The high-risk mutation, export, revision, timeout, and journal cases
   in the release task pass. Unsupported Fusion preview APIs fail with a clear error.
2. **Platforms:** A fresh install and saved-document E2E run pass on Windows
   and macOS with the same packaged Python add-in. The server binary or Docker
   image used in each run is identified by version and checksum.
3. **Safety:** Remote bind is refused without opt-in; mutation retries after an
   unknown outcome are refused; cancelled queued work cannot execute later.
4. **Quality:** CI passes from a clean checkout. The release workflow does not
   publish a stable tag before the full gate is satisfied. The release includes
   server artifacts, add-in package, image tag, checksums, and notes.
5. **Documentation:** The installation, upgrade, uninstall, troubleshooting,
   tool catalog, and limitations have been followed by someone using only the
   release artifacts and docs. No beta or C# add-in instructions remain in
   current user guides.

## Tracking the audit findings

| Finding | Planned resolution | Blocking gate |
| --- | --- | --- |
| `expected_revision` advertised but absent in Python | Phase 2 implements it or changes the public schema and contract before release; preserving the current contract is preferred. | Behavior |
| Python remote bind has no opt-in | Phase 3 enforces `FUSION_MCP_ALLOW_REMOTE=1` before socket bind. | Safety |
| 300/310-second edits conflict with 30/45-second paths | Phase 2 chooses one timeout table, updates every active path and test, and verifies the journal barrier. | Behavior, safety |
| CI lacks add-in E2E evidence | Phases 5–6 add automated checks and recorded live Windows/macOS runs. | Platforms, quality |
| Upstream clone unpinned; release lacks artifacts | Phases 1 and 5 pin source and publish installable, checksummed assets. | Quality, documentation |

## Decisions and remaining choices

- The release will ship a pinned Python add-in copy from this repository. The
  upstream commit and MIT license are recorded in `addon/UPSTREAM.md`.
- LAN access remains available with explicit non-loopback opt-in and trusted
  network instructions. Authentication is a separate future change.
- The stable version number is pending release gates; `1.0.0` is proposed.
- Supported Fusion versions must come from the two live E2E environments;
  the current `2026+` tool annotations alone are not a compatibility claim.

## Out of scope

Adding `execute_code` to the C# MCP catalog, authenticating the TCP protocol,
adding Linux Fusion support, and changing the 92 tool names or parameter units
are separate product decisions. A failed live Windows or macOS gate blocks the
stable release; it does not silently narrow platform support.
