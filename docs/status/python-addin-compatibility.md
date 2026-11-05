# Python add-in compatibility inventory

Status: working inventory, 2026-11-04. Candidate source: upstream commit
`19b9f1e9519235ae4a0f5e8dc6b029c08b87ff5f`, copied into
`addon/Fusion360MCP/` and committed with local changes in `1750af1`. This is static and
headless evidence, not a complete release-candidate Fusion pass. Windows Fusion
E2E has not been run because a Windows Fusion host is unavailable; per project
decision it is deferred until after release. Windows installer CI does not
constitute live Fusion E2E evidence.

| Contract | C# add-in before migration | Packaged Python add-in now | Remaining evidence |
| --- | --- | --- | --- |
| Tool dispatch | 92 typed commands | All 92 names route to Python handlers and every public schema field is accepted by its Python signature. The packaged add-in rejects `execute_code` on raw TCP as `UNKNOWN_COMMAND`. | Partial live matrix through the exact macOS candidate is recorded in `docs/status/e2e-stable-release.md`; remaining rows before release; Windows matrix after release. |
| Server metadata | `operation_id` journaled by the C# server; `expected_revision` handled in the former add-in | Python dispatch removes server-only `operation_id` and checks `expected_revision` before invoking a Fusion handler. | Exact candidate revision conflict, replay, changed-argument conflict, and restart replay now pass on macOS; remaining candidate matrix before release, Windows after release. |
| Scene revision | Process-local per-document counter and reported-name registry | Python uses `parentDocument.creationId` when available, with process-local identity fallback; returns `model_revision` and `revision_after`, and classifies a previously reported missing body/sketch as `STALE_REFERENCE`. Headless tests cover separate documents and Stop/Run handler recreation. A Fusion process restart resets the counter. | Test document switch and Fusion restart semantics live. |
| Failure effects | Changed snapshots report `partial_effects: true`; uncertain outcomes stay unknown. | Python compares before/after body snapshots on handlers that collect deltas; otherwise failure is marked unknown. | Controlled partial failure in Fusion; consider wider snapshot coverage. |
| Verification | C# handlers have read-back checks on some mutations. | Python emits `existence_checked` when a returned body/sketch name resolves in the scene; otherwise it emits `call_returned` and marks geometry unverified. | Scene readbacks now verify candidate body/sketch counts and revisions; measured geometry checks and remaining candidate matrix before release. |
| Bridge timeout | The former C# add-in bridge used a 30-second limit. | Python bridge is 300 seconds; server TCP client is 310 seconds and journal barrier is 315 seconds in `1750af1`. Queued cancellation, executing unknown outcomes, and Stop/Run rejection have headless tests. | Validate a real long-running operation and an instrumented timeout race. |
| Remote listener | C# add-in requires opt-in. | Python listener rejects non-loopback without `FUSION_MCP_ALLOW_REMOTE=1`; headless guard test passes. | Project owner deferred the trusted-network opt-in E2E to the next release; Windows live listener check after release. |
| Installer | The former Windows script built the C# add-in; the former macOS script linked a local upstream clone. | Both scripts install checked-in Python source or the release zip with a staged replacement. macOS repeated install and extracted-zip install passed locally. | Windows installer CI before release; live Fusion install on Windows after release. |
| Distribution | Main-branch workflow could create a release without assets. | New tag-only workflow checks E2E status and attaches server archives, Python zip, and SHA-256 file. | Run workflow on a candidate tag after live gates. |

The former C# add-in has been removed from the source tree, solution, CI build,
and distribution. Windows Fusion E2E is deferred until after release because a
Windows Fusion host is unavailable; Windows installer CI remains a pre-release
gate.

## Installer runner verification — 2026-11-05

The `main` CI run at commit `fe3f5c82` passed `build-test`,
`python-windows-install` (Windows Server 2025), and `python-macos-install`:
[GitHub Actions run 41974231569](https://github.com/qdimka/fusion360-mcp/actions/runs/41974231569).
The Windows job covered install, rejection of an invalid manifest, rollback
after an injected promotion failure, and reinstall. This is installer CI
evidence only; Windows Fusion E2E remains deferred until after release.

The later `main` run at candidate source `bca69cbb` also passed `build-test`,
`python-windows-install` and `python-macos-install`:
[GitHub Actions run 41974563658](https://github.com/qdimka/fusion360-mcp/actions/runs/41974563658).
The macOS installer was additionally exercised locally with the extracted
candidate ZIP and a temporary `FUSION_MCP_ADDINS_DIR`; the installed package
tree matched the ZIP contents. This does not count as Windows Fusion E2E.
