#!/usr/bin/env python3
"""Generate docs/TOOLS.md (the tool listing) from the C# tool catalog.

Reads ``src/Fusion360.Mcp.Core/Catalog/tools_catalog.json`` — the very file
``scripts/generate_tool_catalog.py`` writes, the single source of truth on
the C# side — so docs/TOOLS.md and the catalog cannot drift apart. Tool
grouping is declared in ``GROUPS`` below (mirrors the “Available tools”
section of AGENTS.md); a catalog tool missing from GROUPS —
or a GROUPS entry absent from the catalog — fails the script, so new
tools must be assigned to a group explicitly.

Output is deterministic (groups in declared order, catalog order within
each group), so a second run over an unchanged catalog produces no diff.
Drift is gated: ``scripts/check_guards.py`` rebuilds the document in
memory and exits 1 when docs/TOOLS.md differs.

Usage:
  python3 scripts/generate_tool_listing.py
"""

from __future__ import annotations

import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
CATALOG = ROOT / "src" / "Fusion360.Mcp.Core" / "Catalog" / "tools_catalog.json"
OUT = ROOT / "docs" / "TOOLS.md"

# Group order = order of the “Available tools” section in AGENTS.md.
# Within a group the listing keeps catalog order. Every catalog tool must
# appear in exactly one group; every entry here must exist in the catalog.
GROUPS: dict[str, list[str]] = {
    "Scene & Query": [
        "ping", "get_scene_info", "get_object_info",
        "get_bounding_box", "list_components",
    ],
    "Design Type": [
        "get_design_type", "set_design_type",
    ],
    "Sketching": [
        "create_sketch", "draw_rectangle", "draw_circle", "draw_line",
        "draw_arc", "draw_spline", "create_polygon", "add_constraint",
        "auto_constrain", "add_dimension", "offset_curve", "trim_curve",
        "extend_curve", "project_geometry",
    ],
    "Features": [
        "extrude", "revolve", "sweep", "loft", "fillet", "chamfer",
        "shell", "mirror", "create_hole", "rectangular_pattern",
        "circular_pattern", "create_thread", "draft_faces", "split_body",
        "split_face", "offset_faces", "scale_body", "suppress_feature",
        "unsuppress_feature",
    ],
    "Body Operations": [
        "move_body", "rename_body", "boolean_operation", "delete_all",
        "undo",
    ],
    "Direct Primitives": [
        "create_box", "create_box_parametric", "create_cylinder",
        "create_sphere", "create_torus",
    ],
    "Surface Operations": [
        "patch_surface", "stitch_surfaces", "thicken_surface",
        "ruled_surface", "trim_surface",
    ],
    "Sheet Metal": [
        "create_flange", "create_bend", "flat_pattern", "unfold",
    ],
    "Construction Geometry": [
        "create_construction_plane", "create_construction_axis",
        "create_ucs",
    ],
    "Assembly": [
        "create_component", "add_joint", "create_as_built_joint",
        "create_rigid_group",
    ],
    "Inspection & Analysis": [
        "measure_distance", "measure_angle", "get_physical_properties",
        "create_section_analysis", "check_interference", "compare_meshes",
    ],
    "Appearance & Parameters": [
        "set_appearance", "set_color", "get_parameters", "create_parameter",
        "set_parameter", "delete_parameter",
    ],
    "Import / Export": [
        "import_mesh", "export_stl", "export_step", "export_f3d",
        "export_view_sheet", "export",
    ],
    "CAM / Manufacturing": [
        "cam_create_setup", "cam_create_operation", "cam_generate_toolpath",
        "cam_post_process", "cam_list_setups", "cam_list_operations",
        "cam_get_operation_info",
    ],
    "Perception": [
        "render_view",
    ],
}


def _cell(text: str) -> str:
    """Flatten and escape a value for a markdown table cell."""
    return " ".join(text.split()).replace("|", "\\|")


def build_markdown(catalog: list[dict]) -> str:
    """Deterministic docs/TOOLS.md content for *catalog* entries.

    Raises ``ValueError`` when a tool is unassigned to a group or a group
    entry is absent from the catalog — both are drift that must be fixed
    explicitly, never silently.
    """
    by_name = {e["name"]: e for e in catalog}
    dupes = sorted({n for n in by_name if len([e for e in catalog if e["name"] == n]) > 1})
    if dupes:
        raise ValueError(f"duplicate tool names in catalog: {dupes}")
    mapped = {name for names in GROUPS.values() for name in names}
    missing = sorted(set(by_name) - mapped)
    if missing:
        raise ValueError(f"tools missing from GROUPS (assign a group): {missing}")
    stale = sorted(mapped - set(by_name))
    if stale:
        raise ValueError(f"GROUPS entries absent from catalog: {stale}")

    lines = [
        f"# Tool Listing — {len(catalog)} C# tools + reference Python tools",
        "",
        "> **Generated file — do not edit by hand.**",
        ">",
        "> Source of truth: `src/Fusion360.Mcp.Core/Catalog/tools_catalog.json`",
        "> (written by `python3 scripts/generate_tool_catalog.py` from the",
        "> reference `tools.py`). Regenerate this document with",
        "> `python3 scripts/generate_tool_listing.py`; sync is enforced by",
        "> `python3 scripts/check_guards.py` (drift -> exit 1).",
        ">",
        "> Groups mirror the “Available tools” section of `AGENTS.md`;",
        "> the AGENTS.md table is a generated snapshot — do not edit it by",
        "> hand; new tools are added via the catalog + regeneration.",
        "",
        "| Tool | Group | Description | Read-only | Destructive | Idempotent |",
        "|---|---|---|---|---|---|",
    ]
    for group, names in GROUPS.items():
        for name in names:
            entry = by_name[name]
            ann = entry["annotations"]
            lines.append(
                "| `{}` | {} | {} | {} | {} | {} |".format(
                    name,
                    _cell(group),
                    _cell(entry.get("description", "")),
                    "✓" if ann.get("readOnlyHint") else "",
                    "✓" if ann.get("destructiveHint") else "",
                    "✓" if ann.get("idempotentHint") else "",
                )
            )
    lines.extend([
        "",
        "## Reference Python tools (not exposed by the C# server)",
        "",
        "These tools are available in the upstream Python server/add-in and are",
        "not part of the C# server catalog above.",
        "",
        "| Tool | Group | Description |",
        "|---|---|---|",
        "| `execute_code` | Code Execution | Run arbitrary Python in Fusion 360. The last expression's value is returned (REPL-style). Pre-defined names: `app`, `ui`, `design`, `component`, `adsk`, `math`. |",
    ])
    lines.append("")
    return "\n".join(lines)


def main() -> None:
    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    try:
        doc = build_markdown(catalog)
    except ValueError as exc:
        sys.exit(f"generate_tool_listing: {exc}")
    if OUT.is_file() and OUT.read_text(encoding="utf-8") == doc:
        print(f"docs/TOOLS.md up to date ({len(catalog)} tools, no changes)")
        return
    OUT.write_text(doc, encoding="utf-8")
    print(f"wrote {OUT.relative_to(ROOT)} ({len(catalog)} tools)")


if __name__ == "__main__":
    main()
