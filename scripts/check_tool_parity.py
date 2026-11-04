#!/usr/bin/env python3
"""Check the released C# catalog against the checked-in Python add-in."""

import ast
import json
from pathlib import Path

from generate_addin_effects import generate

ROOT = Path(__file__).resolve().parent.parent
CATALOG = ROOT / "src/Fusion360.Mcp.Core/Catalog/tools_catalog.json"
HANDLER = ROOT / "addon/Fusion360MCP/server/command_handler.py"
EFFECTS = ROOT / "addon/Fusion360MCP/server/effects.json"


def dispatch_contracts() -> tuple[dict[str, str], dict[str, ast.FunctionDef]]:
    tree = ast.parse(HANDLER.read_text())
    handler_class = next(node for node in tree.body
                         if isinstance(node, ast.ClassDef) and node.name == "CommandHandler")
    methods = {node.name: node for node in handler_class.body
               if isinstance(node, ast.FunctionDef)}
    for node in ast.walk(tree):
        if not isinstance(node, ast.Assign) or not isinstance(node.value, ast.Dict):
            continue
        mapping = {key.value: value.attr
                   for key, value in zip(node.value.keys, node.value.values)
                   if isinstance(key, ast.Constant) and isinstance(key.value, str)
                   and isinstance(value, ast.Attribute)}
        if "ping" in mapping and "get_scene_info" in mapping:
            return mapping, methods
    raise ValueError("Python command dispatch table not found")


def main() -> None:
    catalog = json.loads(CATALOG.read_text())
    effects = json.loads(EFFECTS.read_text())
    expected_effects = generate()
    names = {entry["name"] for entry in catalog}
    dispatch, methods = dispatch_contracts()
    errors = []

    if len(names) != len(catalog):
        errors.append("duplicate C# catalog tool name")
    if names != set(dispatch):
        errors.append(f"dispatch mismatch: missing={sorted(names - dispatch)}, "
                      f"extra={sorted(set(dispatch) - names)}")
    if effects != expected_effects:
        errors.append("effects.json differs from ToolEffects.cs; run generate_addin_effects.py")

    operations = set(expected_effects["operation_commands"])
    documents = set(expected_effects["document_mutation_commands"])
    for entry in catalog:
        name = entry["name"]
        properties = entry["inputSchema"].get("properties", {})
        method = methods.get(dispatch.get(name, ""))
        if method is None:
            errors.append(f"{name}: Python handler method is missing")
            continue
        accepted = {arg.arg for arg in method.args.args + method.args.kwonlyargs}
        public_properties = set(properties) - {"operation_id", "expected_revision"}
        if method.args.kwarg is None and not public_properties <= accepted:
            errors.append(f"{name}: schema fields missing in Python signature: "
                          f"{sorted(public_properties - accepted)}")
        required_args = {arg.arg for arg in method.args.args[1:
                         len(method.args.args) - len(method.args.defaults)]}
        required_args.update(arg.arg for arg, default in
                             zip(method.args.kwonlyargs, method.args.kw_defaults)
                             if default is None)
        if not required_args <= set(entry["inputSchema"].get("required", [])):
            errors.append(f"{name}: required Python arguments absent from schema: "
                          f"{sorted(required_args - set(entry['inputSchema'].get('required', [])))}")
        read_only = entry.get("annotations", {}).get("readOnlyHint") is True
        if read_only != (name not in operations):
            errors.append(f"{name}: readOnlyHint differs from ToolEffects")
        if ("operation_id" in properties) != (name in operations):
            errors.append(f"{name}: operation_id schema differs from ToolEffects")
        if ("expected_revision" in properties) != (name in documents):
            errors.append(f"{name}: expected_revision schema differs from ToolEffects")

    if errors:
        for error in errors:
            print("PARITY FAILED:", error)
        raise SystemExit(1)
    print(f"PARITY OK — {len(catalog)} catalog tools, Python dispatch and effects in sync")


if __name__ == "__main__":
    main()
