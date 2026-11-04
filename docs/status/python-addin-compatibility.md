# Python add-in compatibility inventory

Status: working inventory, 2026-11-04. Candidate source: upstream commit
`19b9f1e9519235ae4a0f5e8dc6b029c08b87ff5f`, copied into
`addon/Fusion360MCP/` and committed with local changes in `1750af1`. This is static and
headless evidence, not a live Fusion pass.

| Contract | C# add-in before migration | Packaged Python add-in now | Remaining evidence |
| --- | --- | --- | --- |
| Tool dispatch | 92 typed commands | All 92 names route to Python handlers and every public schema field is accepted by its Python signature. The packaged add-in rejects `execute_code` on raw TCP as `UNKNOWN_COMMAND`. | Live representative tools on both hosts. |
| Server metadata | `operation_id` journaled by the C# server; `expected_revision` handled in the add-in | Python dispatch removes server-only `operation_id` and checks `expected_revision` before invoking a Fusion handler. | Live revision conflict and same-ID retry. |
| Scene revision | Process-local per-document counter and reported-name registry | Python uses `parentDocument.creationId` when available, with process-local identity fallback; returns `model_revision` and `revision_after`, and classifies a previously reported missing body/sketch as `STALE_REFERENCE`. Headless tests cover separate documents and Stop/Run handler recreation. A Fusion process restart resets the counter. | Test document switch and Fusion restart semantics live. |
| Failure effects | Changed snapshots report `partial_effects: true`; uncertain outcomes stay unknown. | Python compares before/after body snapshots on handlers that collect deltas; otherwise failure is marked unknown. | Controlled partial failure in Fusion; consider wider snapshot coverage. |
| Verification | C# handlers have read-back checks on some mutations. | Python emits `existence_checked` when a returned body/sketch name resolves in the scene; otherwise it emits `call_returned` and marks geometry unverified. | Live read-back of smoke mutations; do not claim geometry verification until measured. |
| Bridge timeout | Legacy C# path still passes 30 seconds; the shared Core has 300/310-second edits that began before this release task. | Python bridge is 300 seconds; C# client is 310 seconds and journal barrier is 315 seconds in `1750af1`. Queued cancellation, executing unknown outcomes, and Stop/Run rejection have headless tests. | Validate a real long-running operation and an instrumented timeout race. |
| Remote listener | C# add-in requires opt-in. | Python listener rejects non-loopback without `FUSION_MCP_ALLOW_REMOTE=1`; headless guard test passes. | Windows/macOS listener checks through packaged install. |
| Installer | Windows script built the C# add-in; macOS script linked a local upstream clone. | Both scripts install checked-in Python source or the release zip with a staged replacement. macOS repeated install and extracted-zip install passed locally. | Windows CI job and fresh Fusion install on both hosts. |
| Distribution | Main-branch workflow could create a release without assets. | New tag-only workflow checks E2E status and attaches server archives, Python zip, and SHA-256 file. | Run workflow on a candidate tag after live gates. |

The old C# add-in remains available in the source tree during validation. Its
source and tests are removed only after the Python package passes the
[release task](../tasks/python-addin-stable-release.md) on both supported hosts.
