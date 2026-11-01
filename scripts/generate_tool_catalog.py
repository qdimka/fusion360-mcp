#!/usr/bin/env python3
"""Generate the C# ToolCatalog data file from the reference Python catalog.

Reads ``reference/fusion360-mcp-server/src/fusion360_mcp/tools.py`` (the
93-tool declarative catalog on the Python side; ``execute_code`` is an
intentional exclusion from the C# product surface)
and writes ``src/Fusion360.Mcp.Core/Catalog/tools_catalog.json`` — the
single source of truth on the C# side (PLAN.md D6). The Core project embeds
the JSON as a resource; the MCP server, mock responses, and the parity
checker all consume the same data.

Re-run this script whenever the reference catalog changes, then run
``scripts/check_tool_parity.py`` to prove the two sides still agree.
"""

from __future__ import annotations

import ast
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "src" / "Fusion360.Mcp.Core" / "Catalog" / "tools_catalog.json"

# Deliberate product-surface exclusions from the upstream reference catalog.
# execute_code runs agent-authored code inside Fusion and is not exposed by
# this port; keep parity checks against the remaining tools.
EXCLUDED_REFERENCE_TOOLS = {"execute_code"}


def tools_path() -> pathlib.Path:
    """Reference tools.py location.

    ``reference/`` is a local, untracked clone (kept out of CI), so an
    explicit reference repo may be passed as argv[1] when the local clone
    is missing (e.g. inside a kanban worktree).
    """
    if len(sys.argv) > 1:
        return pathlib.Path(sys.argv[1]) / "src" / "fusion360_mcp" / "tools.py"
    return ROOT / "reference" / "fusion360-mcp-server" / "src" / "fusion360_mcp" / "tools.py"


def load_tools() -> list[dict]:
    """Parse reference TOOLS and apply the reviewed port exclusions."""
    ref = tools_path()
    src = ref.read_text()
    tree = ast.parse(src)
    tools = None
    for node in tree.body:
        if isinstance(node, ast.AnnAssign) and getattr(node.target, "id", "") == "TOOLS":
            if node.value is not None:
                tools = ast.literal_eval(node.value)
        elif isinstance(node, ast.Assign):
            for target in node.targets:
                if getattr(target, "id", "") == "TOOLS":
                    tools = ast.literal_eval(node.value)
    if tools is None:
        sys.exit(f"TOOLS list not found in {tools_path()}")
    return [tool for tool in tools if tool["name"] not in EXCLUDED_REFERENCE_TOOLS]


def load_annotation_sets() -> tuple[set[str], set[str], set[str]]:
    """Extract the _READ_ONLY / _DESTRUCTIVE / _IDEMPOTENT name sets."""
    ref = tools_path()
    src = ref.read_text()
    tree = ast.parse(src)
    wanted = {"_READ_ONLY": set(), "_DESTRUCTIVE": set(), "_IDEMPOTENT": set()}
    for node in tree.body:
        if isinstance(node, ast.Assign):
            for target in node.targets:
                name = getattr(target, "id", "")
                if name in wanted and isinstance(node.value, ast.Set):
                    wanted[name] = ast.literal_eval(node.value)
    missing = [k for k, v in wanted.items() if not v]
    if missing:
        sys.exit(f"annotation sets missing or empty in {ref}: {missing}")
    return wanted["_READ_ONLY"], wanted["_DESTRUCTIVE"], wanted["_IDEMPOTENT"]


# ------------------------------------------------------------------------
# C#-side extensions: `operation_id` covers document and external side
# effects; `expected_revision` covers document mutations only.
#
# OPERATION_TOOLS / REVISION_TOOLS stay in lockstep with ToolEffects.cs.
# ------------------------------------------------------------------------

MUTATION_TOOLS = frozenset({
    # feature ops
    "extrude", "revolve", "sweep", "loft", "fillet", "chamfer", "shell",
    "mirror", "create_hole", "rectangular_pattern", "circular_pattern",
    "create_thread", "draft_faces", "split_body", "split_face",
    "offset_faces", "scale_body", "suppress_feature", "unsuppress_feature",
    # body ops
    "move_body", "boolean_operation",
    # primitives
    "create_box", "create_cylinder", "create_sphere", "create_torus",
    # surface / sheet metal (can produce / thicken bodies)
    "thicken_surface", "patch_surface", "stitch_surfaces", "ruled_surface",
    "trim_surface", "create_flange", "create_bend", "flat_pattern",
    "unfold",
    # scene-wide
    "delete_all", "undo",
    # parametric & agent-authored changes
    "set_parameter",
    # sketch constraint mutation
    "auto_constrain",
    # construction geometry / appearance
    "create_ucs", "set_color",
})

OPERATION_TOOLS = frozenset({
    "create_sketch", "draw_rectangle", "draw_circle", "draw_line", "extrude", "revolve",
    "fillet", "chamfer", "shell", "mirror", "rename_body", "move_body", "export_stl",
    "boolean_operation", "delete_all", "undo", "sweep", "loft",
    "create_polygon", "draw_arc", "create_hole", "rectangular_pattern", "circular_pattern",
    "create_component", "add_joint", "export_step", "export_f3d", "export_view_sheet", "export",
    "import_mesh", "create_parameter", "set_parameter", "delete_parameter", "add_constraint",
    "auto_constrain", "add_dimension", "create_construction_plane", "create_construction_axis",
    "create_ucs", "draw_spline", "offset_curve", "trim_curve", "extend_curve", "create_thread",
    "draft_faces", "split_body", "split_face", "offset_faces", "scale_body", "create_box",
    "create_box_parametric", "create_cylinder", "create_sphere", "create_torus",
    "create_as_built_joint", "create_rigid_group", "create_section_analysis", "set_appearance",
    "set_color", "project_geometry", "suppress_feature", "unsuppress_feature", "patch_surface",
    "stitch_surfaces", "thicken_surface", "ruled_surface", "trim_surface", "create_flange",
    "create_bend", "flat_pattern", "unfold", "cam_create_setup", "cam_create_operation",
    "cam_generate_toolpath", "cam_post_process", "set_design_type",
})
REVISION_TOOLS = OPERATION_TOOLS - {
    "export_stl", "export_step", "export_f3d", "export_view_sheet", "export", "cam_post_process",
}

EXPECTED_REVISION_PROPERTY = {
    "type": "integer",
    "minimum": 0,
    "description": (
        "Optional. The model_revision you last observed for this design "
        "(from get_scene_info 'model_revision' or a previous result's "
        "'revision_after'). If it does not match the current model "
        "revision, the command is rejected with REVISION_CONFLICT before "
        "touching the model — re-read get_scene_info and retry with the "
        "current revision. After a scene change, names that no longer "
        "resolve report STALE_REFERENCE instead of BODY_NOT_FOUND / "
        "SKETCH_NOT_FOUND."
    ),
}

OPERATION_ID_PROPERTY = {
    "type": "string",
    "description": (
        "Optional. Idempotency key (UUID, e.g. a uuid4). If you pass it, a "
        "retry of this operation with the same arguments is deduplicated by "
        "the server's durable operation journal: the recorded result of the "
        "first execution is returned without re-applying the operation. "
        "Omit it and the server generates a UUID v4 and returns it in the "
        "result as 'operation_id'. The same operation_id with different "
        "arguments (or for a different command) is rejected with "
        "OPERATION_ID_CONFLICT; after a clean failure you may retry with "
        "the same operation_id, after an unknown outcome (e.g. TIMEOUT) or "
        "partial effects you must verify the scene and use a new one."
    ),
}

def add_csharp_extension(name: str, schema: dict) -> dict:
    """Return a copy of *schema* with the C#-side extension properties added."""
    extended = json.loads(json.dumps(schema))
    properties = dict(extended.get("properties") or {})
    if name in REVISION_TOOLS:
        properties["expected_revision"] = EXPECTED_REVISION_PROPERTY
    if name in OPERATION_TOOLS:
        properties["operation_id"] = OPERATION_ID_PROPERTY
    extended["properties"] = properties
    return extended


def main() -> None:
    tools = load_tools()
    read_only, destructive, idempotent = load_annotation_sets()

    known = {tool["name"] for tool in tools}
    unknown_mutation = sorted(MUTATION_TOOLS - known)
    if unknown_mutation:
        sys.exit(f"MUTATION_TOOLS entries missing from the reference catalog: {unknown_mutation}")
    unknown_operations = sorted(OPERATION_TOOLS - known)
    if unknown_operations:
        sys.exit(f"OPERATION_TOOLS entries missing from the reference catalog: {unknown_operations}")
    if not REVISION_TOOLS <= OPERATION_TOOLS:
        sys.exit("REVISION_TOOLS must be a subset of OPERATION_TOOLS")

    entries = []
    for tool in tools:
        name = tool["name"]
        # Mirror the reference's module-level annotation loop exactly
        # (tools.py applies the sets after the list literal is defined).
        entries.append(
            {
                "name": name,
                "title": tool["title"],
                "description": tool["description"],
                "inputSchema": add_csharp_extension(name, tool["inputSchema"])
                if name in OPERATION_TOOLS
                else tool["inputSchema"],
                "annotations": {
                    "readOnlyHint": name in read_only,
                    "destructiveHint": name in destructive,
                    "idempotentHint": name in idempotent,
                },
            }
        )

    names = [e["name"] for e in entries]
    if len(names) != len(set(names)):
        dupes = sorted({n for n in names if names.count(n) > 1})
        sys.exit(f"duplicate tool names in reference catalog: {dupes}")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(entries, indent=1) + "\n")
    print(f"wrote {OUT} ({len(entries)} tools)")


if __name__ == "__main__":
    main()
