# Stable Release Remaining Tasks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the remaining protocol and installer risks, prove the Python add-in in Fusion on macOS, remove the C# add-in, and publish one verified stable release; run Windows Fusion E2E after release.

**Architecture:** The .NET 10 server keeps the 92-tool catalog and durable operation journal; the Python add-in is the sole future Fusion bridge. Verify the implementation, remove the legacy C# integration, freeze versioned artifacts, run macOS E2E on those exact artifacts, and then tag a release. Run Windows Fusion E2E after release.

**Tech Stack:** C#/.NET 10 and xUnit, Python 3.12 and unittest, Fusion Python API, PowerShell, POSIX shell, GitHub Actions.

**Spec:** [python-addin-stable-release.md](../tasks/python-addin-stable-release.md). The [release overview](stable-release.md) tracks phase gates; this document gives the executable steps.

## Global Constraints

- Preserve all 92 catalog tool names and units; `execute_code` and `reload_handler` stay rejected by the packaged add-in.
- Keep `operation_id` in the .NET journal, not in Fusion handler parameters. A mutation with an unknown outcome must not be sent again automatically.
- Use 300 seconds for the Python bridge, 310 seconds for the .NET TCP client, and 5 seconds for ping.
- Listen on loopback port 9876 by default; require `FUSION_MCP_ALLOW_REMOTE=1` for any other bind address. The TCP protocol has no authentication.
- Preserve existing designs, exports, and journal files during upgrades and tests. Use a disposable, named, saved design for live E2E.
- The C# add-in is retired from the source tree and build. Windows Fusion E2E is deferred until after release because no Windows Fusion host is available; Windows installer CI remains required before release.
- Set macOS evidence to `pass` only after the versioned candidate completes its matrix. For publication, record Windows as `deferred (post-release)` and retain the live Windows matrix as a required post-release follow-up.
- Do not publish or push as a side effect of executing an earlier task; publishing is the final, separately reviewed action.

## Review Focus

| Risk | Pin it in |
| --- | --- |
| Malformed JSON or a non-object request kills a socket session | Task 1 socket-pair regression, followed by a valid `ping` on the same connection |
| An export creates a file, then fails, and the response claims no effect | Task 2 new-file and pre-existing-file regressions |
| A failed upgrade destroys the prior install | Task 3 invalid-package rollback checks on both OS runners |
| A repeated or pending `operation_id` executes twice | Task 4 journal unit tests and live replay/conflict observations |
| A rebuilt release archive differs from the artifact used for E2E | Task 6 two-build checksum comparison; Task 7 records both artifact hashes |

## File structure and interfaces

| Responsibility | Existing files / proposed file |
| --- | --- |
| TCP request frames and error envelopes | `addon/Fusion360MCP/server/socket_server.py`, `tests/python_addin/test_contract.py` |
| External-output failure classification | `addon/Fusion360MCP/server/command_handler.py`, `addon/Fusion360MCP/server/effects.py`, `tests/python_addin/test_contract.py` |
| Install validation | `scripts/install-addon.sh`, `scripts/install-addon.ps1`, `.github/workflows/ci.yml` |
| Journal behavior | `src/Fusion360.Mcp.Server/IdempotentToolBackend.cs`, `OperationJournal.cs`, matching tests in `tests/Fusion360.Mcp.Server.Tests/` |
| Compatibility evidence | `docs/E2E-STATUS.md`, `docs/status/e2e-stable-release.md`, `docs/status/python-addin-compatibility.md` |
| Legacy retirement | `Fusion360Mcp.sln`, `Directory.Build.props`, `.github/workflows/ci.yml`, legacy add-in and test directories, current guides, historical ADRs |
| Documentation and agent-file audit | `README.md`, `AGENTS.md`, active `docs/**/*.md`, any other repository agent instructions, `scripts/check_guards.py` |
| Candidate packaging | `scripts/publish.sh`, `scripts/package_addin.py`, `Directory.Build.props`, `addon/Fusion360MCP/Fusion360MCP.manifest` |
| Release evidence check | New scripts/check_release_evidence.py and new tests/python_addin/test_release_evidence.py; `.github/workflows/release.yml`, `.github/workflows/docker.yml` |

### Task 1: Keep the TCP listener alive after malformed input

**Files:** Modify `tests/python_addin/test_contract.py`; modify `addon/Fusion360MCP/server/socket_server.py` only if the red test proves a gap.

**Interfaces:** `Fusion360MCPServer._handle_client(client: socket.socket) -> None` reads newline-delimited JSON. `Fusion360MCPServer._dispatch(client, command)` emits one `status: success|error` JSON frame per request; the handler's bad `params` response is `status: success`, `result.ok: false`, `error_kind: INVALID_INPUT`.

- [x] **Step 1: Write the socket regression.** Added `test_socket_rejects_malformed_requests_and_recovers` with invalid JSON, a non-object frame, a handler-level invalid request, and a follow-up ping on the same socket.
- [x] **Step 2: Confirm red.** The regression was introduced with the socket fix in commit `d068357`; see the focused run below for current behavior.
- [x] **Step 3: Fix only the failing path.** `socket_server.py` validates decoded top-level objects and preserves the connection after malformed frames; handler-level input errors retain the existing success envelope.
- [x] **Step 4: Confirm green and commit.** The full Python contract suite passed in current verification (32 tests); the change is in commit `d068357`.

### Task 2: Classify failed external outputs safely

**Files:** Modify `tests/python_addin/test_contract.py` and `addon/Fusion360MCP/server/command_handler.py`; use `EXTERNAL_OUTPUT_COMMANDS` from `addon/Fusion360MCP/server/effects.py`.

**Interfaces:** `CommandHandler.execute_command(command: dict) -> dict` returns a `status: success` frame with `result.ok: false` on handler failure. The six external-output commands are `cam_post_process`, `export`, `export_f3d`, `export_step`, `export_stl`, and `export_view_sheet`.

- [x] **Step 1: Write two failing tests.** Added new-file and overwritten-existing-file export failure cases in `test_contract.py`.
- [x] **Step 2: Confirm red.** The tests and minimal effect-observation fix are in commit `4806656`; both cases pass in the current Python suite.
- [x] **Step 3: Implement the minimum observation.** External-output handlers compare safe file metadata markers and report observed partial effects; otherwise the response marks the outcome unknown.
- [x] **Step 4: Verify and commit.** Current verification passed the Python suite (32 tests), tool parity, documentation/style guards, and `git diff --check`; the fix is in commit `4806656`.

### Task 3: Prove installer rollback and CI on both runner OSs

**Files:** Modify `.github/workflows/ci.yml`; change an installer only if the new checks fail. The checked-in package is built by `scripts/package_addin.py`.

**Interfaces:** Both installers read `FUSION_MCP_ADDINS_DIR`; the installed package is `<AddIns>/Fusion360MCP`. A rejected package must leave the prior `Fusion360MCP.py` and manifest byte-identical.

- [x] **Step 1: Add the Windows check.** `python-windows-install` records SHA-256 for the installed handler and manifest, rejects an invalid manifest, verifies rollback after an injected `Move-Item` promotion failure, and reruns the valid installer. Windows CI passes.
- [x] **Step 2: Add the macOS check.** `python-macos-install` records both hashes, rejects an invalid `supportedOS`, verifies rollback after an injected `mv` promotion failure, and reruns the valid installer. macOS CI passes.
- [x] **Step 3: Verify locally where available.** Installed the extracted candidate ZIP using a temporary `FUSION_MCP_ADDINS_DIR`; the installed package matched the extracted package. `python3.12 -m unittest discover -s tests/python_addin -v` passed (32 tests), and `python3.12 scripts/check_guards.py` passed.
- [x] **Step 4: Verify on runners and commit.** The `main` run [41974231569](https://github.com/qdimka/fusion360-mcp/actions/runs/41974231569) passed `build-test`, `python-windows-install`, and `python-macos-install`. Its URL and scope are recorded in `docs/status/python-addin-compatibility.md`; installer CI is not live Fusion validation.

### Task 4: Preliminary Fusion compatibility on macOS; Windows deferred until after release

**Files:** Update `docs/status/e2e-stable-release.md` with clearly labeled preliminary sections while keeping top-level status pending; update `docs/status/python-addin-compatibility.md`. Change source only for a reproduced defect.

**Interfaces:** Use the tool schemas in `src/Fusion360.Mcp.Core/Catalog/tools_catalog.json` and the [E2E runbook](../E2E-STATUS.md). Record the add-in and server hashes used for macOS preliminary E2E; Windows live E2E is post-release.

- [x] **Step 1: Fix the live matrix before testing.** `docs/E2E-STATUS.md` contains a concrete example request and precondition/observation for all 16 listed catalog groups. No preview-only exclusion was claimed without testing; candidate limitations are recorded separately in the evidence.
- [x] **Step 2: Freeze the preliminary input.** Candidate source `bca69cbb3a16f4eaee09272d4e39e233ac53e8ab`, version `1.0.0`, the Actions add-in/server archives, and their checksums are recorded in `docs/status/e2e-stable-release.md`; repeated Ubuntu artifacts match.
- [ ] **Step 3: Run macOS Fusion.** Install from the extracted zip in a disposable AddIns location. Use a named, saved design. Execute every [runbook](../E2E-STATUS.md) row, including same-ID replay, same-ID changed-arguments conflict, a stale revision, and a deliberately invalid mutation with no scene change. Record arguments, `operation_id`, revisions, observed scene/file state, OS/Fusion version, and defects.
- [x] **Step 4: Defer Windows Fusion E2E.** A Windows Fusion host is unavailable; live Windows testing is scheduled after release. Keep installer CI as a pre-release requirement.
- [x] **Step 5: Reproduce and correct any failure.** No candidate defect requiring a source/package change was reproduced; the incomplete flat-pattern call was caused by the saved fixture already containing a pattern. No rebuild was needed.
- [ ] **Step 6: Record macOS evidence and commit.** Before declaring macOS preliminary compatibility passed, run `dotnet test tests/Fusion360.Mcp.Server.Tests/Fusion360.Mcp.Server.Tests.csproj -c Release --filter 'FullyQualifiedName~IdempotentToolBackendTests|FullyQualifiedName~OperationJournalTests'`. Require replay, conflict, unknown-outcome, and restart tests to pass. Record macOS results and the Windows post-release deferral in the compatibility inventory; keep final E2E status pending until the versioned candidate passes. Commit the preliminary evidence.

### Task 5: Retire the C# add-in (completed by project-owner decision)

**Files:** Remove `src/Fusion360.Mcp.AddIn/`, `tests/Fusion360.Mcp.AddIn.Tests/`, and `thirdparty/adsk/`; modify `Fusion360Mcp.sln`, `Directory.Build.props`, `.github/workflows/ci.yml`, `scripts/check_guards_baseline.json`, current guides, and historical ADRs that still describe C# as current.

**Interfaces:** The .NET server/Core projects and 92-tool catalog remain. The Python package and installers from Tasks 1–4 become the only active Fusion implementation.

- [x] **Step 1: Confirm the project-owner decision.** The project owner decided the C# add-in will not be used, and deferred live Windows Fusion E2E until after release.
- [x] **Step 2: Preserve contract coverage.** Python contract tests cover revision conflicts, stale references, timeout cancellation and unknown outcomes, partial effects, verification, and remote-bind guard. Headless suite verification is recorded below.
- [x] **Step 3: Remove the legacy project and dependencies.** Removed the old project and tests from the solution, deleted their source directories and `thirdparty/adsk/`, removed the `net48` reference package and C# CI job.
- [x] **Step 4: Repair current documentation.** Active install and status guidance now identifies Python as the sole Fusion add-in and schedules Windows live E2E after release; historical decisions are being annotated as superseded.
- [x] **Step 5: Verify and commit.** Restore and Release build passed; Core (121), Server (84), and Python (25) tests passed; parity, docs/style guards, and `git diff --check` passed. The active dependency search returned no matches. Windows/macOS installer CI still needs its planned GitHub run.

### Task 5a: Audit documentation and agent instructions

**Files:** Review `README.md`, `AGENTS.md`, all active Markdown files under `docs/`, and any other repository `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, or `SKILL.md` found by inventory. Modify `scripts/check_guards.py`; create tests/python_addin/test_docs_guard.py for the expanded link check. Record unresolved findings in `docs/status/TECH_DEBT.md` only when they are not release blockers.

**Interfaces:** `check_doc_links(paths: Iterable[Path] | None = None) -> list[str]` in `scripts/check_guards.py` validates the supplied Markdown paths for tests; with no argument it scans `docs/**/*.md`, root `README.md`, and discovered repository agent instruction files. Historical ADRs may retain old paths only with an explicit supersession note; current user and agent instructions must point to the Python add-in and current plan/spec locations.

- [x] **Step 1: Inventory the files.** Reviewed all Markdown files under the repository, the root README and AGENTS.md. No other repository agent instruction files were found; historical ADRs were distinguished from active guidance.
- [x] **Step 2: Find stale references.** Searched active instructions and docs. Remaining C# paths and `net48` references are historical ADRs or completed plan records with supersession context; beta status and `reload_handler` rejection are current. Updated obsolete release timing and installer guidance.
- [x] **Step 3: Expand local-link coverage.** Added tests for missing README/agent links, valid relative links, and default scanning of README and agent files. Implementation and tests were completed in the documentation-guard change; the current guard and focused tests pass.
- [x] **Step 4: Check external links and formatting.** Reviewed all five external links in active docs; all resolve. Updated the .NET SDK URL to its canonical localized URL. Inspected README, AGENTS.md, INSTALL, ARCHITECTURE, and edited docs for Markdown structure and links.
- [x] **Step 5: Verify and commit.** Docs guard, focused tests, stale-reference classification, and `git diff --check` pass. The audit corrections and checklist are recorded in the follow-up docs commit.

### Task 6: Build a reproducible versioned candidate and evidence gate

**Prerequisite:** Task 5a has no unresolved release-blocking documentation or agent-file findings.

**Files:** Modify `Directory.Build.props`, `addon/Fusion360MCP/Fusion360MCP.manifest`, `scripts/publish.sh` only if server archives differ between builds, `.github/workflows/ci.yml`, `.github/workflows/release.yml`, `.github/workflows/docker.yml`; create scripts/check_release_evidence.py and tests/python_addin/test_release_evidence.py.

**Interfaces:** `check_release_evidence(path: Path, version: str, addin_zip: Path, server_archives: dict[str, Path] | None = None) -> None` requires macOS pass and the exact Windows deferral marker, and raises on version/artifact hash mismatch and requires a nonempty `Candidate source commit` field. The release workflow verifies the Windows server archive checksum and tested macOS server archive; the Docker workflow passes none. Both versioned workflows also prove that commit is an ancestor of the tag and that only `docs/` files or `README.md` changed afterward.

- [x] **Step 1: Add failing gate tests.** Added all seven specified cases in `tests/python_addin/test_release_evidence.py` using temporary evidence and artifacts. The tests are part of the candidate source commit.
- [x] **Step 2: Confirm red.** Reconstructed the pre-checker tree at the parent of the candidate source commit, copied in the new test file, and ran the focused suite: it failed at the expected missing `check_release_evidence` import. After adding the checker to that temporary tree, all seven tests passed.
- [x] **Step 3: Implement the checker and workflow calls.** `scripts/check_release_evidence.py` validates the required evidence fields and supplied artifact hashes. Both versioned workflows check the candidate commit ancestry and reject non-document source changes after it; release passes the tested Windows and macOS server archives, Docker passes the add-in archive, and both invoke the checker before publication. Current verification: seven focused tests pass.
- [x] **Step 4: Set candidate version and compare builds.** Version `1.0.0` is set in `Directory.Build.props` and `Fusion360MCP.manifest`. Two local macOS arm64 publishes from the same source produced matching server ZIP (`87305ad6563396190d13986b6ee5864c6b0db69f1317b97dbaa1b2cf71145b61`) and add-in ZIP (`933f91f46add9c21dfa14f0c89044be6f7302d120504a940b0651a4e06d100e7`). Two Ubuntu builds of the same source also produced matching hashes for all five ZIPs; recorded with the candidate evidence.
- [x] **Step 5: Verify and commit.** Docs guard, focused tests, stale-reference classification, and `git diff --check` pass. The audit corrections and checklist are recorded in the follow-up docs commit.
- [x] **Step 6: Produce downloadable candidate artifacts.** Ubuntu `build-test` uploaded all five ZIPs and `SHA256SUMS`; a second Ubuntu build of the same source commit produced identical ZIP hashes. The exact Actions macOS arm64 archive is used for pre-release E2E, and the Windows binary is retained for the post-release run. The macOS-host archive has a different hash from the Ubuntu-host archive, while repeated builds on each host are stable.

### Task 7: Validate the exact final candidate on macOS; Windows follows after release

**Files:** `docs/status/e2e-stable-release.md`, `docs/STATUS.md`, release notes/current guides; source only for a reproduced defect followed by a new candidate cycle.

**Interfaces:** The candidate source commit, four server zips, one Python zip, and SHA-256 file downloaded from Task 6's successful Ubuntu CI run are immutable inputs for this run. The evidence checker consumes the completed record.

- [x] **Step 1: Record the Windows deferral.** `Windows: deferred (post-release)` is recorded; schedule the complete runbook on the published candidate for the first available Windows Fusion host.
- [x] **Step 2: Run the agreed release scope from the [runbook](../E2E-STATUS.md) on macOS** with the Task 6 candidate and server archive. The exact-candidate evidence records geometry, component, sheet/body, parameter, surface, export, render, successful `flat_pattern`, revision conflict, idempotent replay and restart replay. The project owner explicitly deferred trusted network opt-in and remaining catalog/runbook rows to the next release; they are not claimed as tested in the evidence.
- [x] **Step 3: Rerun after a defect.** No candidate source or packaged-file defect was found, so no new candidate cycle was needed. The invalid `create_flange` call was a malformed test request; scene inspection confirmed no model change.
- [x] **Step 4: Complete the evidence record.** Recorded the candidate source/version and exact archive SHA-256 values in `docs/status/e2e-stable-release.md`, with the owner-approved deferrals. The release workflow checked the immutable Actions candidate archives against the evidence; published assets were downloaded and verified against `SHA256SUMS`. The recorded product source commit remains unchanged.

### Task 8: Verify release publication

**Files:** `.github/workflows/release.yml`, `.github/workflows/docker.yml`, `docs/STATUS.md`, release notes. Changes only if verification exposes a concrete mismatch.

**Interfaces:** A `v<version>` tag points to the final evidence commit; its tested candidate source commit is an ancestor, with only documentation and the release/Docker packaging workflows allowed to change afterward. Release assets are staged from the immutable tested candidate Actions artifact; the versioned Docker image builds from the tagged tree. The evidence checker rejects incomplete or mismatched records before publication.

- [x] **Step 1: Verify the final source and CI.** The tag run passed its build/test and Windows/macOS installer jobs, evidence checks and archive hashes. The candidate source is an ancestor of the tag; only docs and the approved release/Docker packaging workflows changed afterward.
- [x] **Step 2: Review and authorize the publication action.** Version `1.0.0` matches `Directory.Build.props`; the release workflow attaches server archives, Python zip, checksums and generated notes, while the Docker workflow builds the versioned image from the same tag SHA. Project owner authorized publishing `1.0.0` with the documented E2E deferrals.
- [x] **Step 3: Tag and verify.** Published `v1.0.0`; release workflow [41990825490](https://github.com/qdimka/fusion360-mcp/actions/runs/41990825490) and Docker workflow [41990825462](https://github.com/qdimka/fusion360-mcp/actions/runs/41990825462) passed. Downloaded all five ZIPs and `SHA256SUMS`; every digest matched, including tested Windows, macOS arm64 and Python hashes. Release URL and workflow runs are recorded in `docs/STATUS.md`.

## Completion rule

The stable release is complete after Task 8 verifies published assets from the candidate that passed the macOS matrix and Windows installer CI. Run Windows Fusion E2E against those published assets after release.
