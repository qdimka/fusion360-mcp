The C# handler paths described here were removed on 2026-11-04. Treat implementation details below as historical; current behavior is in the Python add-in and tool contract.

# ADR-0009 — Verification levels and named checks

Status: accepted (implemented).
Date: 2026-10-30.
Environment: contract in [`TOOL_CONTRACTS.md`](../TOOL_CONTRACTS.md) §§1.2 and 4; baseline mutation deltas predate this ADR.

## Decision

Mutation results may include an additive `verification` object describing the level of confirmation:

- **`level`** is a monotone ladder: `call_returned` → `existence_checked` → `geometry_checked` → `roundtrip_checked`. Report only the strongest level actually confirmed. `roundtrip_checked` requires an export/import round trip and a subsequent read.
- **`checks[]`** contains named observations: `{name, observed, expected, passed}`. Numeric expectations are compared using the shared tolerances in §4, not by directly comparing the original tool inputs.
- **`unverified_aspects[]`** identifies each aspect that could not be checked, with `{name, reason}`. Unverified aspects should be explicit.

The verification level supplements `ok` and mutation `deltas`; it does not replace them. A check outside tolerance yields `passed: false`, not a new error kind. Unreadable measurements belong in `unverified_aspects`.

## Rationale

Mutation deltas show whether a feature changed the scene. Verification describes what the add-in subsequently checked. A level ladder avoids overstating confidence, while named checks make the evidence inspectable.

## Evidence

Implemented in the shared Core and the former C# add-in verification handler (removed 2026-11-04). Tolerances are centralized in `VerificationTolerances.cs`. The optional summary is rendered by the MCP server. `roundtrip_checked` is defined but is not currently emitted by a handler.

## Alternatives rejected

- **A single `verified: bool`:** does not distinguish a returned call from a geometry measurement.
- **Recompute expected geometry on the server:** geometry calculations from tool inputs belong to the caller or a separate query.
- **Duplicate tolerances in each tool:** risks inconsistent comparisons; use one shared table.
