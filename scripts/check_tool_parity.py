#!/usr/bin/env python3
"""Parity gate (PLAN.md D6): C# ToolCatalog data vs. the reference catalog.

Compares ``src/Fusion360.Mcp.Core/Catalog/tools_catalog.json`` (the data
embedded in Fusion360.Mcp.Core) against the reference Python catalog, after
the explicit upstream-tool exclusions in ``generate_tool_catalog.py``
``reference/fusion360-mcp-server/src/fusion360_mcp/tools.py``:

* tool names (set equality, order preserved)
* title + description
* inputSchema (deep equality, including "required" fields — with documented
  C# extensions: optional "operation_id" on effectful tools and
  "expected_revision" on document mutations)
* readOnlyHint / destructiveHint / idempotentHint annotations

Exit 0 when both sides agree; exit 1 with a diff report otherwise.
Reference repo path: argv[1] when the local ``reference/`` clone is
missing (e.g. inside a kanban worktree).
"""

from __future__ import annotations

import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from generate_tool_catalog import (  # noqa: E402
    EXPECTED_REVISION_PROPERTY,
    EXCLUDED_REFERENCE_TOOLS,
    OPERATION_ID_PROPERTY,
    OPERATION_TOOLS,
    REVISION_TOOLS,
    load_annotation_sets,
    load_tools,
    tools_path,
)

ROOT = pathlib.Path(__file__).resolve().parent.parent
CATALOG = ROOT / "src" / "Fusion360.Mcp.Core" / "Catalog" / "tools_catalog.json"

# The C#-side catalog extensions (ADR-0011 / ADR-0010): optional input
# properties that exist ONLY in the C# catalog, on mutation tools only.
CSHARP_EXTENSION_PROPERTIES = {
    "expected_revision": (EXPECTED_REVISION_PROPERTY, REVISION_TOOLS),
    "operation_id": (OPERATION_ID_PROPERTY, OPERATION_TOOLS),
}


def load_catalog() -> list[dict]:
    if not CATALOG.exists():
        sys.exit(
            f"{CATALOG} not found — run scripts/generate_tool_catalog.py first"
        )
    return json.loads(CATALOG.read_text())


def schemas_agree(
    name: str, catalog_schema: dict | None, reference_schema: dict | None
) -> bool:
    """inputSchema deep equality, tolerating the C#-side extensions.

    The C# catalog carries optional `operation_id` on every effectful tool
    and `expected_revision` on document mutations. Everything else must
    equal the reference schema.
    """
    if catalog_schema is None or reference_schema is None:
        return catalog_schema is None and reference_schema is None
    reference_props = reference_schema.get("properties", {})
    catalog_props = dict(catalog_schema.get("properties") or {})
    required = catalog_schema.get("required", [])
    for key, (expected, tools) in CSHARP_EXTENSION_PROPERTIES.items():
        if key in reference_props or key in required:
            return False
        if name in tools:
            if catalog_props.get(key) != expected:
                return False
            catalog_props.pop(key, None)
        elif key in catalog_props:
            return False

    reduced = dict(catalog_schema)
    reduced["properties"] = catalog_props
    return reduced == reference_schema


def main() -> None:
    ref = load_tools()
    read_only, destructive, idempotent = load_annotation_sets()
    catalog = load_catalog()

    errors: list[str] = []

    expected_operations = {tool["name"] for tool in ref} - read_only
    if OPERATION_TOOLS != expected_operations:
        errors.append(
            "OPERATION_TOOLS must match the reference's non-read-only set "
            f"(missing={sorted(expected_operations - OPERATION_TOOLS)}, "
            f"extra={sorted(OPERATION_TOOLS - expected_operations)})"
        )

    ref_by_name = {t["name"]: t for t in ref}
    cat_by_name = {t["name"]: t for t in catalog}

    unexpected_exclusions = EXCLUDED_REFERENCE_TOOLS - {"execute_code"}
    if unexpected_exclusions:
        errors.append(f"review new upstream exclusions explicitly: {sorted(unexpected_exclusions)}")
    for name in EXCLUDED_REFERENCE_TOOLS:
        if name in cat_by_name:
            errors.append(f"intentionally excluded tool exposed in C# catalog: {name}")

    for name in ref_by_name:
        if name not in cat_by_name:
            errors.append(f"missing in C# catalog: {name}")

    for name in cat_by_name:
        if name not in ref_by_name:
            errors.append(f"unexpected in C# catalog: {name}")

    if [t["name"] for t in ref] != [t["name"] for t in catalog]:
        errors.append("tool order differs from the reference catalog")

    for t in ref:
        name = t["name"]
        entry = cat_by_name.get(name)
        if entry is None:
            continue

        if entry.get("title") != t.get("title"):
            errors.append(f"{name}: title mismatch")
        expected_description = t.get("description")
        if entry.get("description") != expected_description:
            errors.append(f"{name}: description mismatch")
        if not schemas_agree(name, entry.get("inputSchema"), t.get("inputSchema")):
            errors.append(f"{name}: inputSchema mismatch")

        ann = entry.get("annotations", {})
        if ann.get("readOnlyHint") is not (name in read_only):
            errors.append(f"{name}: readOnlyHint mismatch")
        if ann.get("destructiveHint") is not (name in destructive):
            errors.append(f"{name}: destructiveHint mismatch")
        if ann.get("idempotentHint") is not (name in idempotent):
            errors.append(f"{name}: idempotentHint mismatch")

    if errors:
        print(f"PARITY FAILED — {len(errors)} mismatch(es) "
              f"({CATALOG} vs {tools_path()}):")
        for e in errors:
            print(f"  - {e}")
        sys.exit(1)

    print(f"PARITY OK — {len(catalog)} tools, catalog in sync with "
          f"{tools_path()}")


if __name__ == "__main__":
    main()
