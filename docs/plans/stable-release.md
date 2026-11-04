# Python Add-in Stable Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Release the .NET MCP server with one verified Python Fusion add-in for Windows and macOS, then retire the C# add-in and leave beta.

**Architecture:** The .NET 10 server retains the typed tool catalog and durable `operation_id` journal. The checked-in Python add-in serves the same TCP contract through Fusion's main thread on both supported hosts. Live platform evidence gates removal of the legacy add-in and publication of a stable tag.

**Tech Stack:** C#/.NET 10, Fusion Python API, Python 3.12 for headless CI, PowerShell, POSIX shell, GitHub Actions.

**Spec:** [python-addin-stable-release.md](../tasks/python-addin-stable-release.md)

## Global Constraints

- Keep the 92-tool names and parameter units defined in `src/Fusion360.Mcp.Core/Catalog/tools_catalog.json`; reject `execute_code`.
- Use the pinned source under `addon/Fusion360MCP/`, including `addon/UPSTREAM.md` and the upstream MIT license. Installation must not fetch source.
- Python bridge timeout: 300 seconds; C# TCP client timeout: 310 seconds; ping: 5 seconds. Do not retry a mutation with an unknown outcome.
- Default listener: loopback port 9876. Non-loopback bind requires `FUSION_MCP_ALLOW_REMOTE=1`; TCP has no authentication.
- Preserve the server operation journal and user designs during install, upgrade, and uninstall.
- Require passing live Windows and macOS Fusion runs before deleting the C# add-in. A Windows Fusion host is not yet available.
- The final E2E record must name the release commit, candidate version, server artifact and add-in SHA-256 values, OS/Fusion versions, commands, responses, and defects.
- Do not publish a stable tag or claim support for a Fusion version on headless tests alone.

## Review Focus

These are the most consequential inputs to check during Task 1 and the platform runs:

1. Malformed JSON, non-object requests, and a bad `params` value yield a structured error while the Python listener remains available. Add `test_socket_rejects_malformed_requests_and_recovers` in `tests/python_addin/test_contract.py`.
2. A repeated `operation_id` with different arguments or a pending intent cannot execute a second mutation. Exercise `IdempotentToolBackendTests` and the live journal rows in the E2E runbook.
3. An installer failure after staging leaves the previous add-in usable. Exercise a staged replacement failure in the Windows and macOS installer jobs.
4. An export that fails after creating an external file is never reported as `partial_effects: false`. Add a Python handler test for an observed external effect and inspect the live export result.
5. A Fusion process restart resets the Python revision counter while a server restart preserves journal decisions. Record both transitions separately in live evidence.

## Baseline already completed

Commit `1750af1` contains the pinned Python add-in, installers, release packaging, CI/release workflow edits, and the [compatibility inventory](../status/python-addin-compatibility.md). Local checks before that commit passed: 12 Python headless tests, 92-tool dispatch/effect parity, docs guards, .NET build and tests, macOS repeat installation in a temporary AddIns directory, and checksums for four server archives plus the add-in zip. These checks are not live Fusion or GitHub runner evidence. The C# add-in remains in the tree. The [E2E record](../status/e2e-stable-release.md) is pending on both platforms.

The executable steps for Tasks 1–4 below are in the
[remaining-tasks plan](stable-release-next-steps.md). It also splits their
protocol, installer, platform, retirement, and publication work into
independently checkable changes.

## Remaining file map

| Responsibility | Files |
| --- | --- |
| Protocol and effect gap fixes, only if tests expose them | `addon/Fusion360MCP/server/socket_server.py`, `command_handler.py`, `event_bridge.py`, `tests/python_addin/test_contract.py`, `tests/Fusion360.Mcp.Server.Tests/IdempotentToolBackendTests.cs` |
| Installer rollback and runner checks | `scripts/install-addon.sh`, `scripts/install-addon.ps1`, `.github/workflows/ci.yml` |
| Candidate version and packaging | `Directory.Build.props`, `addon/Fusion360MCP/Fusion360MCP.manifest`, `scripts/package_addin.py`, `scripts/publish.sh`, planned scripts/check_release_evidence.py, `.github/workflows/release.yml`, `.github/workflows/docker.yml` |
| Live runbook and evidence | `docs/E2E-STATUS.md`, `docs/status/e2e-stable-release.md`, `docs/status/python-addin-compatibility.md` |
| Legacy removal after preliminary platform pass | `src/Fusion360.Mcp.AddIn/`, `tests/Fusion360.Mcp.AddIn.Tests/`, `thirdparty/adsk/`, `Fusion360Mcp.sln`, `Directory.Build.props`, `.github/workflows/ci.yml`, `scripts/check_guards_baseline.json` |
| Documentation and agent-file audit | `README.md`, `AGENTS.md`, all active `docs/**/*.md`, any other repository agent instruction files, and `scripts/check_guards.py` |
| Final user documentation and release | `README.md`, `docs/INSTALL.md`, `docs/ARCHITECTURE.md`, `docs/DOCKER.md`, `docs/TOOLS.md`, `docs/STATUS.md`, `docs/GOVERNANCE.md`, `docs/adr/*.md`, `docs/decisions/*.md`, `.github/workflows/release.yml`, `.github/workflows/docker.yml` |

### Task 1: Close static contract and installer gaps

**Files:** `tests/python_addin/test_contract.py`, relevant Python server file only if a test fails, both installers and `.github/workflows/ci.yml` only if rollback checks fail.

**Interfaces:** `Fusion360MCPServer._dispatch(client, command)` emits one JSON frame; `CommandHandler.execute_command(command: dict) -> dict`; `EventBridge.submit(command: dict, timeout: float = 300.0) -> dict`. The C# server consumes these frames through `Fusion360Connection` and `IdempotentToolBackend`.

- [ ] **Step 1:** Add headless tests named `test_socket_rejects_malformed_requests_and_recovers` and `test_export_failure_with_observed_file_does_not_claim_no_effects`. Assert the response frame and successful follow-up `ping` for the first; assert `partial_effects` is not `false` for the second.
- [ ] **Step 2:** Run `python3.12 -m unittest discover -s tests/python_addin -v`; verify each new test fails for a real contract gap, or record that the existing behavior already passes.
- [ ] **Step 3:** Make only the code changes required by a failing test. Keep the 92-tool catalog, server-only `operation_id`, and wire shape unchanged.
- [ ] **Step 4:** Add an installer replacement-failure check to the Windows and macOS CI jobs: install once, provide an invalid package or failed stage, assert the prior `Fusion360MCP.py` still exists, then install again successfully.
- [ ] **Step 5:** Run `dotnet build Fusion360Mcp.sln -c Release`, both Core/Server test projects, `python3.12 scripts/check_tool_parity.py`, `python3.12 scripts/check_guards.py`, and `git diff --check`. Require all commands to exit zero.
- [ ] **Step 6:** On a branch with CI enabled, run `.github/workflows/ci.yml` and record the Windows/macOS job URLs and results in `docs/status/python-addin-compatibility.md`. Commit any fixes and evidence separately from unrelated changes.

### Task 2: Preliminary live compatibility on both Fusion hosts

**Files:** `docs/E2E-STATUS.md`, `docs/status/python-addin-compatibility.md`; Python source or installer only for a reproduced defect.

**Interfaces:** Release zip from `python3 scripts/package_addin.py`; `ping`, `get_scene_info`, `create_sketch`, `draw_rectangle`, `extrude`, `render_view`, and `export_f3d` use the 92-tool catalog and the runbook request shapes.

- [ ] **Step 1:** Build the add-in zip and server artifacts from one identified commit with `bash scripts/publish.sh`; verify `cd dist && shasum -a 256 -c SHA256SUMS`.
- [ ] **Step 2:** On macOS Fusion, install the zip into a disposable test environment and run every row of the [E2E runbook](../E2E-STATUS.md) against a saved, named design. Record OS/Fusion versions, hashes, requests, responses, and defects.
- [ ] **Step 3:** Repeat Step 2 on Windows Fusion with the same add-in zip. This step awaits a Windows host and cannot be inferred from PowerShell runner checks.
- [ ] **Step 4:** If either platform fails, add a focused regression test, fix the defect, rebuild artifacts, and rerun the affected rows on both platforms where shared behavior changed.
- [ ] **Step 5:** Mark preliminary compatibility passed in `docs/status/python-addin-compatibility.md` only after both runs pass. Keep `docs/status/e2e-stable-release.md` pending until the final release candidate is tested.

### Task 3: Retire the C# add-in and build the versioned candidate

**Prerequisite:** Task 2 passed on both hosts. This task must not begin based only on headless CI.

**Files:** Legacy directories, `Fusion360Mcp.sln`, `Directory.Build.props`, Python manifest, CI, guard baseline, current guides and historical decision notes listed above.

- [ ] **Step 1:** Compare `tests/Fusion360.Mcp.AddIn.Tests/` with `tests/python_addin/test_contract.py`. Add Python tests for any missing revision conflict, stale reference, timeout, partial-effect, verification, or remote-bind assertion, and run the Python suite before deleting the C# tests.
- [ ] **Step 2:** Remove `src/Fusion360.Mcp.AddIn/`, `tests/Fusion360.Mcp.AddIn.Tests/`, and `thirdparty/adsk/`; remove their solution, `net48`, and CI references. Keep the server/Core and historical ADRs.
- [ ] **Step 3:** Update current guides to the sole Python add-in, annotate historical decisions that describe retired paths, and complete the documentation and agent-file audit in Task 5a of the [remaining-tasks plan](stable-release-next-steps.md). Regenerate `docs/TOOLS.md` if its generator or catalog changes.
- [ ] **Step 4:** Choose the stable candidate version (proposed `1.0.0`), set the same value in `Directory.Build.props` and `Fusion360MCP.manifest`, then run `bash scripts/publish.sh`. Record the resulting artifact hashes; any later source or package change requires rebuilding and rechecking affected E2E rows.
- [ ] **Step 5:** Run a clean checkout build, Core/Server tests, Python tests, parity, guards, installer jobs, and `git diff --check`. Require no `net48` or adsk dependency in the active solution. Commit the candidate before final E2E.

### Task 4: Validate the final candidate and publish

**Files:** `docs/status/e2e-stable-release.md`, `docs/STATUS.md`, release notes/current guides, `.github/workflows/release.yml` or Docker workflow only for a demonstrated gate defect.

- [ ] **Step 1:** Run the full candidate matrix in [E2E-STATUS.md](../E2E-STATUS.md) on Windows and macOS using the artifacts built from Task 3's commit. Record both server and add-in hashes, OS/Fusion versions, saved design, commands, responses, defects, and reruns.
- [ ] **Step 2:** Review each specification acceptance criterion against the test record. Set `Windows: pass`, `macOS: pass`, `Status: pass`, `Candidate version`, and `Add-in SHA256` only when all required rows pass; make no support claim beyond the tested Fusion versions.
- [ ] **Step 3:** Add scripts/check_release_evidence.py with `check_release_evidence(path: Path, version: str, addin_zip: Path) -> None`; it must reject pending platform lines, version mismatch, and add-in SHA-256 mismatch. Add tests for each rejection and success, call it from both versioned workflows, then run CI on the final candidate commit before creating a tag.
- [ ] **Step 4:** Review release notes and current docs for install, upgrade, uninstall, journal persistence, network exposure, preview API limits, and known issues. Commit the evidence and docs; if that changes packaged source, rebuild and repeat affected platform checks.
- [ ] **Step 5:** After all gates pass, create the matching `v<version>` tag and verify the release has four server archives, the Python zip, SHA-256 file, notes, and a Docker image from the same commit. Do not tag or publish while any gate is pending.

## Completion rule

The task is complete only when Task 4 has recorded final passing Windows and macOS runs and the tagged release assets are verified. Local tests, an install on one host, or a passing CI run alone do not close it.
