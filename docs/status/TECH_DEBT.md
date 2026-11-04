# TECH_DEBT — Open items

Policy ([GOVERNANCE.md](../GOVERNANCE.md)): after each release, resolve an item, close it with a reason, or retain it with a return condition. The number of open items should not grow during a cycle.

## Open

| # | Item | Source | Triage (P6, 2026-10-31) |
|---|---|---|---|
| 1 | `Application.saveAs` is unavailable in the current adsk API (Fusion 2706.x), so MCP cannot save a document under a specified name; the user must save it in Fusion. | T8, commits `8d2e5eb`/`e6b191f`; E2E notes | Retain. Revisit if a supported `saveAs` API becomes available; then consider adding a save tool. |
| 2 | Stateful E2E smoke runs must use a saved, named design; otherwise Fusion may discard changes on restart. | T8 post-mortem | The saved-document check is now in [E2E-STATUS.md](../E2E-STATUS.md). Close after the next real E2E run. |
| 3 | The packaged Python add-in has not had a recorded live E2E run on Windows. | [E2E-STATUS.md](../E2E-STATUS.md) | Deferred until after release because no Windows Fusion host is available. Run the complete candidate matrix on the first available Windows Fusion host and record the result in [e2e-stable-release.md](e2e-stable-release.md). Windows installer CI remains part of release CI. |
| 5 | Style debt in `src/`: 162 lines exceed 120 characters and 24 comment blocks exceed 8 lines; tracked by the ratchet baseline. | P6 guard | Retain. The ratchet baseline may only shrink. Close when `scripts/check_guards.py --regen` produces an empty baseline. |
| 6 | `roundtrip_checked` has no current producer in the verification block. | P1 residual ([ADR-0009](../adr/ADR-0009-verification-block.md)) | Retain. Revisit when an export/import round-trip verification tool is added to the catalog. |

## Closed

| # | Item | Closed | Reason |
|---|---|---|---|
| 4 | The server version was hard-coded as `1.0.0`. | 2026-10-31 | `Fusion360McpServer.CreateOptions` now reads the version from assembly metadata, which is tied to `<Version>` in `Directory.Build.props`. |
