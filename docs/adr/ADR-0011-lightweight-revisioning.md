The C# implementation paths described here were removed on 2026-11-04. The revision contract remains relevant; current Fusion implementation is in the Python add-in.

# ADR-0011 — Lightweight model revisioning with `expected_revision`

Status: accepted (implemented).
Date: 2026-10-30.
Environment: contract in [`TOOL_CONTRACTS.md`](../TOOL_CONTRACTS.md) §§1.2 and 2.

## Decision

Use a lightweight revision counter per document rather than a full reference registry:

- **`model_revision`** is returned by `get_scene_info` and advances after each applied document mutation.
- **`expected_revision`** is an optional guard supplied by the client. If it differs from the current revision, reject the operation with `REVISION_CONFLICT` before changing the model and return the current value as `revision_after`.
- **`STALE_REFERENCE`** reports a previously observed named object that no longer resolves after the scene changed.

## Rationale

The API addresses scene entities by names rather than persistent COM object handles. A document revision detects stale client context, while resolving by name identifies deleted or renamed objects. A full reference registry would add complexity without matching the current API model.

## Evidence

The original implementation used the former C# add-in revision tracker (removed 2026-11-04). The contract remains: `get_scene_info` returns `model_revision`; document mutations check an optional `expected_revision` before running handlers. Revision conflicts and stale named references use the corresponding error kinds.

## Alternatives rejected

- **Maintain a full object-reference registry:** unnecessary for name-based addressing.
- **Track a revision per body or component:** a document-level counter captures changes across the whole design.
- **Require callers to reread the scene without a revision guard:** would not detect intervening edits before a mutation is applied.
