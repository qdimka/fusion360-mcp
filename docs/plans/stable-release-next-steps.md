# Stable Release Remaining Tasks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the remaining protocol and installer risks, prove the Python add-in in Fusion on both hosts, remove the C# add-in, and publish one verified stable release.

**Architecture:** The .NET 10 server keeps the 92-tool catalog and durable operation journal; the Python add-in is the sole future Fusion bridge. First verify the committed implementation and both host integrations, then remove the legacy implementation, freeze versioned artifacts, repeat E2E on those exact artifacts, and only then tag a release.

**Tech Stack:** C#/.NET 10 and xUnit, Python 3.12 and unittest, Fusion Python API, PowerShell, POSIX shell, GitHub Actions.

**Spec:** [python-addin-stable-release.md](../tasks/python-addin-stable-release.md). The [release overview](stable-release.md) tracks phase gates; this document gives the executable steps.

## Global Constraints

- Preserve all 92 catalog tool names and units; `execute_code` and `reload_handler` stay rejected by the packaged add-in.
- Keep `operation_id` in the .NET journal, not in Fusion handler parameters. A mutation with an unknown outcome must not be sent again automatically.
- Use 300 seconds for the Python bridge, 310 seconds for the .NET TCP client, and 5 seconds for ping.
- Listen on loopback port 9876 by default; require `FUSION_MCP_ALLOW_REMOTE=1` for any other bind address. The TCP protocol has no authentication.
- Preserve existing designs, exports, and journal files during upgrades and tests. Use a disposable, named, saved design for live E2E.
- Do not remove the C# add-in until the packaged Python add-in passes preliminary live Windows and macOS checks. A Windows Fusion host is not currently available.
- Do not change `Status: pending` to `pass` in the final evidence file until the versioned candidate passes the entire matrix on both hosts. The tested source commit is recorded there; the later evidence commit may change only documentation.
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

- [ ] **Step 1: Write the socket regression.** In `test_socket_rejects_malformed_requests_and_recovers`, use `socket.socketpair()` and a fake bridge whose `submit` delegates non-ping commands to `CommandHandler.execute_command` and returns the EventBridge ping frame for `ping`. Send invalid JSON and `[]` as separate lines; assert `status: error`, `error_kind: INVALID_INPUT`, and a nonempty `message` for each. Send `{"type":"create_box","params":[]}`; assert `status: success`, `result.ok: false`, `result.error_kind: INVALID_INPUT`. Then send `{"type":"ping","params":{}}` on the same socket and assert `status: success`, `result.status: pong`. Close both sockets and join the handler thread in `finally`.
- [ ] **Step 2: Confirm red.** Run `python3.12 -m unittest tests.python_addin.test_contract.ContractTests.test_socket_rejects_malformed_requests_and_recovers -v`. Expected: the new test fails on the first missing error field or broken follow-up, rather than hanging.
- [ ] **Step 3: Fix only the failing path.** Validate the top-level decoded value before `EventBridge.submit`; keep malformed frames from terminating `_handle_client`. Use an error envelope accepted by `ProtocolCodec.ParseResponse` in `src/Fusion360.Mcp.Core/Protocol/ProtocolCodec.cs`; keep the existing success envelope for handler-level input errors.
- [ ] **Step 4: Confirm green and commit.** Run the new test and `python3.12 -m unittest discover -s tests/python_addin -v`; require zero failures. Commit the socket/test change separately.

### Task 2: Classify failed external outputs safely

**Files:** Modify `tests/python_addin/test_contract.py` and `addon/Fusion360MCP/server/command_handler.py`; use `EXTERNAL_OUTPUT_COMMANDS` from `addon/Fusion360MCP/server/effects.py`.

**Interfaces:** `CommandHandler.execute_command(command: dict) -> dict` returns a `status: success` frame with `result.ok: false` on handler failure. The six external-output commands are `cam_post_process`, `export`, `export_f3d`, `export_step`, `export_stl`, and `export_view_sheet`.

- [ ] **Step 1: Write two failing tests.** In `test_failed_export_with_new_file_reports_partial_effects`, replace `handler.export_f3d` with a function that writes `file_path` in a temporary directory and raises; assert `result.partial_effects is True` and `result.ok is False`. In `test_failed_export_over_existing_file_never_claims_no_effect`, pre-create the file, overwrite it and raise; assert `result.partial_effects is not False` and `result.outcome_unknown is True` unless a changed file is positively observed.
- [ ] **Step 2: Confirm red.** Run each named unittest with Python 3.12. Expected: the current failure path lacks external-output effect classification.
- [ ] **Step 3: Implement the minimum observation.** In `execute_command`, take a before marker for explicit `file_path`, `output_dir`, or `output_folder` before an external-output handler; compare after failure. Mark `partial_effects: true` only for an observed new or changed output. Otherwise set `outcome_unknown: true` and omit `partial_effects`. Avoid reading arbitrary output contents or assuming a missing file proves no side effect.
- [ ] **Step 4: Verify and commit.** Run both named tests, the full Python suite, `python3.12 scripts/check_tool_parity.py`, and `git diff --check`; require zero failures. Commit this effect-contract change separately.

### Task 3: Prove installer rollback and CI on both runner OSs

**Files:** Modify `.github/workflows/ci.yml`; change an installer only if the new checks fail. The checked-in package is built by `scripts/package_addin.py`.

**Interfaces:** Both installers read `FUSION_MCP_ADDINS_DIR`; the installed package is `<AddIns>/Fusion360MCP`. A rejected package must leave the prior `Fusion360MCP.py` and manifest byte-identical.

- [ ] **Step 1: Add the Windows check.** In `python-windows-install`, after the first install, record SHA-256 of the installed `Fusion360MCP.py` and manifest. First run an invalid-manifest package and assert failure with identical hashes. Then inject a one-time failure in the PowerShell `Move-Item` call that promotes the staged package after the old target moved to backup; assert the old target is restored with the same hashes. Rerun the valid installer and check the installed handler file.
- [ ] **Step 2: Add the macOS check.** In `python-macos-install`, after the first install, record both hashes. First run a package whose `supportedOS` is invalid and assert failure with identical hashes. Then put a temporary `mv` shim first in `PATH` that fails only when promoting `.Fusion360MCP-stage.*` to the target, after the old target has moved to backup; assert the rollback restores the old target and hashes. Rerun the valid installer.
- [ ] **Step 3: Verify locally where available.** Run the macOS check using a temporary `FUSION_MCP_ADDINS_DIR` and extracted release zip. Run `python3.12 -m unittest discover -s tests/python_addin -v` and `python3.12 scripts/check_guards.py`; require zero failures.
- [ ] **Step 4: Verify on runners and commit.** A branch/PR run of `.github/workflows/ci.yml` must show `build-test`, `python-windows-install`, and `python-macos-install` passing. Record run URLs in `docs/status/python-addin-compatibility.md`. Commit the workflow and evidence; do not treat runner installation as live Fusion validation.

### Task 4: Preliminary Fusion compatibility on macOS and Windows

**Files:** Update `docs/status/e2e-stable-release.md` with clearly labeled preliminary sections while keeping top-level status pending; update `docs/status/python-addin-compatibility.md`. Change source only for a reproduced defect.

**Interfaces:** Use the tool schemas in `src/Fusion360.Mcp.Core/Catalog/tools_catalog.json` and the [E2E runbook](../E2E-STATUS.md). Use one add-in zip checksum on both hosts and record the server binary checksum used on each host.

- [ ] **Step 1: Fix the live matrix before testing.** Extend `docs/E2E-STATUS.md` with one concrete request and precondition for each current catalog group: `get_scene_info`, `get_design_type`, `create_sketch`, `extrude`, `move_body`, `create_box`, `create_construction_plane`, `create_component`, `get_physical_properties`, `set_color`, `create_parameter`, `patch_surface`, `flat_pattern`, `cam_list_setups`, `export_f3d`, and `render_view`. Record any preview-only exclusion with the Fusion version and reason before the candidate run.
- [ ] **Step 2: Freeze the preliminary input.** Record `git rev-parse HEAD`, build with `bash scripts/publish.sh`, and run `(cd dist && shasum -a 256 -c SHA256SUMS)`. Save the zip and server hashes before installing.
- [ ] **Step 3: Run macOS Fusion.** Install from the extracted zip in a disposable AddIns location. Use a named, saved design. Execute every [runbook](../E2E-STATUS.md) row, including same-ID replay, same-ID changed-arguments conflict, a stale revision, and a deliberately invalid mutation with no scene change. Record arguments, `operation_id`, revisions, observed scene/file state, OS/Fusion version, and defects.
- [ ] **Step 4: Run Windows Fusion.** Repeat Step 3 with the same zip. This step remains pending until a Windows Fusion host is available; a Windows CI runner does not replace it.
- [ ] **Step 5: Reproduce and correct any failure.** Add a focused headless or C# regression where possible, confirm it fails, make the minimal fix, rerun relevant tests, rebuild packages, and repeat affected live rows on both hosts when shared behavior changed.
- [ ] **Step 6: Gate legacy retirement and commit evidence.** Before declaring preliminary compatibility passed, run `dotnet test tests/Fusion360.Mcp.Server.Tests/Fusion360.Mcp.Server.Tests.csproj -c Release --filter 'FullyQualifiedName~IdempotentToolBackendTests|FullyQualifiedName~OperationJournalTests'`. Require replay, conflict, unknown-outcome, and restart tests to pass. Record both live passes and the test result in the compatibility inventory; keep final E2E `Status: pending`. Commit the preliminary evidence.

### Task 5: Remove the C# add-in after both preliminary passes

**Files:** Remove `src/Fusion360.Mcp.AddIn/`, `tests/Fusion360.Mcp.AddIn.Tests/`, and `thirdparty/adsk/`; modify `Fusion360Mcp.sln`, `Directory.Build.props`, `.github/workflows/ci.yml`, `scripts/check_guards_baseline.json`, current guides, and historical ADRs that still describe C# as current.

**Interfaces:** The .NET server/Core projects and 92-tool catalog remain. The Python package and installers from Tasks 1–4 become the only active Fusion implementation.

- [ ] **Step 1: Confirm the prerequisite.** Read both preliminary records and verify they used the same add-in zip and passed every required row. If either is absent, stop this task without deleting files.
- [ ] **Step 2: Preserve contract coverage.** Compare legacy tests against the Python suite for revision conflicts, stale references, timeout cancellation, partial effects, verification, and network guard. For any missing assertion, add a named Python test, confirm red if behavior is missing, and require the Python suite to pass before deletion.
- [ ] **Step 3: Remove the legacy project and dependencies.** Use `dotnet sln Fusion360Mcp.sln remove` for the old project/test entries, remove their directories and the `net48` reference-only package block, and remove the old `addin` CI job. Regenerate the style baseline only if deleted files leave stale entries.
- [ ] **Step 4: Repair current documentation.** Update `README.md`, `docs/INSTALL.md`, `docs/ARCHITECTURE.md`, `docs/STATUS.md`, and active scripts for Python-only install, upgrade, uninstall, troubleshooting, journal persistence, and network exposure. Keep historical ADRs but add a concise supersession note where needed.
- [ ] **Step 5: Verify and commit.** Run `dotnet restore Fusion360Mcp.sln`, `dotnet build Fusion360Mcp.sln -c Release`, Core/Server tests, Python tests, parity, guards, and `rg -n 'Fusion360.Mcp.AddIn|net48|thirdparty/adsk' Fusion360Mcp.sln Directory.Build.props .github/workflows README.md docs/INSTALL.md docs/ARCHITECTURE.md`. The build/tests must pass; the final `rg` must find no active dependency. Commit removal separately.

### Task 5a: Audit documentation and agent instructions

**Files:** Review `README.md`, `AGENTS.md`, all active Markdown files under `docs/`, and any other repository `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, or `SKILL.md` found by inventory. Modify `scripts/check_guards.py`; create tests/python_addin/test_docs_guard.py for the expanded link check. Record unresolved findings in `docs/status/TECH_DEBT.md` only when they are not release blockers.

**Interfaces:** `check_doc_links(paths: Iterable[Path] | None = None) -> list[str]` in `scripts/check_guards.py` validates the supplied Markdown paths for tests; with no argument it scans `docs/**/*.md`, root `README.md`, and discovered repository agent instruction files. Historical ADRs may retain old paths only with an explicit supersession note; current user and agent instructions must point to the Python add-in and current plan/spec locations.

- [ ] **Step 1: Inventory the files.** Run `rg --files --hidden -g '*.md' -g '!**/.git/**' -g '!reference/**' -g '!dist/**` and separately `rg --files --hidden -g 'AGENTS.md' -g 'CLAUDE.md' -g 'GEMINI.md' -g 'SKILL.md' -g '!**/.git/**' -g '!reference/**'`. Review every returned active guide and agent file; record historical ADRs separately from current instructions.
- [ ] **Step 2: Find stale references.** Run `rg -n -i 'reference/fusion360|Fusion360.Mcp.AddIn|net48|thirdparty/adsk|docs/release|0\.1\.0|beta|reload_handler|windows.only|macOS reference' README.md AGENTS.md docs .github`. Classify each hit as current, intentionally historical, or obsolete. Correct obsolete paths, commands, platform/version claims, and C# install directions; add a supersession note to a historical page when its old path could be mistaken for current guidance.
- [ ] **Step 3: Expand local-link coverage.** In tests/python_addin/test_docs_guard.py, add `test_root_readme_missing_link_is_reported`, `test_agent_file_missing_link_is_reported`, and `test_valid_relative_link_resolves`, using temporary Markdown files and `check_doc_links(paths=...)`. Add `test_default_scan_includes_readme_and_agent_files` with a temporary repository root and no `paths` argument. Run `python3.12 -m unittest discover -s tests/python_addin -p 'test_docs_guard.py' -v` and confirm red, then add the optional `paths` parameter and the default README/agent-file inventory to `scripts/check_guards.py`. Preserve the existing rule that local `.md`, `.py`, and `.cs` targets must exist; rerun the four tests and require green.
- [ ] **Step 4: Check external links and formatting.** Extract HTTP(S) links from current guides and agent files, visit each target, and record any 404, moved page, or authentication-only result; replace obsolete URLs with canonical destinations. Inspect headings, list spacing, fenced code blocks, Markdown tables, and rendered links in `README.md`, `AGENTS.md`, `docs/INSTALL.md`, `docs/ARCHITECTURE.md`, and the pages changed by this audit. Fix rendering errors without rewriting historical content unnecessarily.
- [ ] **Step 5: Verify and commit.** Run `python3 scripts/check_guards.py`, `python3.12 -m unittest discover -s tests/python_addin -p 'test_docs_guard.py' -v`, `git diff --check`, and the stale-reference search again. Require zero broken active links, zero unexplained stale references, and no formatting defects in reviewed pages. Commit the documentation, agent-file, and guard fixes separately before Task 6 packages the candidate.

### Task 6: Build a reproducible versioned candidate and evidence gate

**Prerequisite:** Task 5a has no unresolved release-blocking documentation or agent-file findings.

**Files:** Modify `Directory.Build.props`, `addon/Fusion360MCP/Fusion360MCP.manifest`, `scripts/publish.sh` only if server archives differ between builds, `.github/workflows/ci.yml`, `.github/workflows/release.yml`, `.github/workflows/docker.yml`; create scripts/check_release_evidence.py and tests/python_addin/test_release_evidence.py.

**Interfaces:** `check_release_evidence(path: Path, version: str, addin_zip: Path, server_archives: dict[str, Path] | None = None) -> None` raises on pending platform lines or version/artifact hash mismatch and requires a nonempty `Candidate source commit` field. The release workflow passes tested Windows/macOS server archives; the Docker workflow passes none. Both versioned workflows also prove that commit is an ancestor of the tag and that only `docs/` files or `README.md` changed afterward.

- [ ] **Step 1: Add failing gate tests.** `test_rejects_pending_platform`, `test_rejects_wrong_version`, `test_rejects_wrong_addin_hash`, `test_rejects_wrong_server_hash`, `test_rejects_missing_source_commit`, and `test_accepts_complete_matching_record` use a temporary evidence file and archive files. Each rejection asserts nonzero/raised error; acceptance asserts return without error.
- [ ] **Step 2: Confirm red.** Run `python3.12 -m unittest discover -s tests/python_addin -p 'test_release_evidence.py' -v`; expect import/function failure before implementation.
- [ ] **Step 3: Implement the checker and workflow calls.** Parse the exact top-level fields `Status`, `Windows`, `macOS`, `Candidate version`, `Candidate source commit`, and `Add-in SHA256`, plus `Server win-x64 SHA256` and the tested `Server osx-<rid> SHA256`; compare to CLI inputs and SHA-256 of supplied archives. In both versioned workflows use full Git history, verify `git merge-base --is-ancestor <candidate-source-commit> HEAD`, and reject any path outside `docs/` and `README.md` in `git diff --name-only <candidate-source-commit> HEAD`. Call the checker after packaging and before any publish step; keep ordinary build/test jobs.
- [ ] **Step 4: Set candidate version and compare builds.** Set the same selected stable version (proposed `1.0.0`) in `Directory.Build.props` and `Fusion360MCP.manifest`. Run `bash scripts/publish.sh` twice from identical source content and compare the SHA-256 of every produced zip. If server archives differ, make `scripts/publish.sh` produce deterministic sorted entries and fixed zip timestamps, then repeat; do not promise identical server artifacts until the comparison passes.
- [ ] **Step 5: Verify and commit.** Run the new checker tests, full Python suite, Core/Server tests, `python3.12 scripts/check_tool_parity.py`, `python3.12 scripts/check_guards.py`, and `git diff --check`. Commit the versioned candidate and gate before final E2E; record its exact source commit.
- [ ] **Step 6: Produce downloadable candidate artifacts.** Make the Ubuntu `build-test` job in `.github/workflows/ci.yml` run `bash scripts/publish.sh` and upload `dist/*.zip` plus `dist/SHA256SUMS` as one Actions artifact. Run CI on the Task 6 source commit, download that artifact for both Fusion hosts, and compare its hashes to a second Ubuntu build from the same commit. Fix any environment-dependent binary or zip differences before Task 7.

### Task 7: Validate the exact final candidate on both hosts

**Files:** `docs/status/e2e-stable-release.md`, `docs/STATUS.md`, release notes/current guides; source only for a reproduced defect followed by a new candidate cycle.

**Interfaces:** The candidate source commit, four server zips, one Python zip, and SHA-256 file downloaded from Task 6's successful Ubuntu CI run are immutable inputs for this run. The evidence checker consumes the completed record.

- [ ] **Step 1: Run the complete [runbook](../E2E-STATUS.md) on Windows** with the Task 6 zip and appropriate server archive. Record OS/Fusion version, named saved design, exact commands/responses, server/add-in hashes, journal replay, revision behavior after add-in versus Fusion restart, export file, render image, and any defect.
- [ ] **Step 2: Repeat the same matrix on macOS** with the same add-in zip and macOS server archive. Record the same fields. Mark each platform `pass` only after all rows pass; document unsupported preview APIs as limitations, not as silently skipped supported cases.
- [ ] **Step 3: Rerun after a defect.** If code or packaged files change, rebuild and rehash from a new commit, rerun affected cases on both platforms, and update the evidence commit/hash. Never carry a `pass` from an older candidate onto a new artifact without checking affected behavior.
- [ ] **Step 4: Complete the evidence record.** Add `Candidate source commit`, `Candidate version`, `Add-in SHA256`, `Server win-x64 SHA256`, and the tested `Server osx-<rid> SHA256` to `docs/status/e2e-stable-release.md`; set `Status: pass` only when both platforms and release blockers pass. Run scripts/check_release_evidence.py against the built archives, then commit evidence and documentation. Keep the recorded source commit unchanged when the evidence commit changes only docs.

### Task 8: Verify release publication

**Files:** `.github/workflows/release.yml`, `.github/workflows/docker.yml`, `docs/STATUS.md`, release notes. Changes only if verification exposes a concrete mismatch.

**Interfaces:** A `v<version>` tag points to the final evidence commit; its tested candidate source commit is an ancestor with documentation-only changes afterward. Release assets and the versioned Docker image build from the tagged tree and must match the tested artifact hashes. The evidence checker rejects incomplete or mismatched records before a publish job.

- [ ] **Step 1: Verify the final source and CI.** Run all required CI jobs on the commit intended for the tag. Check that the evidence checker passes, the source commit is an ancestor with only documentation changes after it, and notes list tested Fusion versions, limits, and upgrade steps.
- [ ] **Step 2: Review the publication action.** Confirm the tag name matches `Directory.Build.props`, the release job attaches four server archives, the Python zip, checksums and notes, and Docker uses the same tag SHA. Obtain the project's normal publication approval before pushing the tag.
- [ ] **Step 3: Tag and verify.** Push the approved `v<version>` tag; require both versioned workflows to pass. Download each release asset, verify `SHA256SUMS`, compare the add-in/server hashes with the final E2E record, and inspect the Docker image source revision. Record release URL and workflow runs in `docs/STATUS.md`.

## Completion rule

The stable release is complete only after Task 8 verifies published assets from the candidate that passed Task 7. A green headless suite or a run on one Fusion host does not satisfy the release gate.
