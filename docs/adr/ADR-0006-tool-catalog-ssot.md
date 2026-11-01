# ADR-0006 — Single source of truth for the tool catalog (D6)

Status: accepted (implemented).
Date: Recorded 2026-10-30; decided in PLAN.md D6 (2026-10-27), implemented in T2.
Environment: `src/Fusion360.Mcp.Core/Catalog/tools_catalog.json` contains 92 schemas; the reference `tools.py` is used for parity checks.

## Decision

The 92 MCP tool JSON Schemas are maintained once in `tools_catalog.json`. The catalog feeds:

- MCP tool registration (92 schemas).
- Mock responses, keyed by catalog tool name.
- Add-in command dispatch (92 catalog commands); `ping` is a separate internal health command.
- `scripts/check_tool_parity.py`, which deeply compares names, order, titles, descriptions, input schemas, required fields, and annotations against the reference catalog.

The catalog was generated from the reference by `scripts/generate_tool_catalog.py` and remains the runtime source of truth.

## Rationale

Without one catalog and a parity check, the server tool surface, mock layer, and handler dispatch can silently diverge in names, ordering, or schemas. The CI parity gate catches drift.

## Evidence recorded at implementation

- `python3 scripts/check_tool_parity.py` reported all 92 tools in sync with the reference.
- Mock-mode `tools/list` returned 92 tools.
- The recorded 290-test suite included parity and `ToolCatalog` coverage.

## Alternatives rejected

- **Generate schemas separately in each project:** duplicates the port and risks drift.
- **Load `tools.py` as the runtime source:** creates a cross-language runtime dependency; the JSON catalog plus parity check keeps runtime self-contained.
- **Compare only tool names:** order and MCP annotations (`readOnlyHint`, `destructiveHint`, `idempotentHint`) are part of the client-facing contract.
