#!/usr/bin/env python3
"""Mirror the C# tool effect sets into the packaged Python add-in."""

import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE = ROOT / "src/Fusion360.Mcp.Core/Catalog/ToolEffects.cs"
OUTPUT = ROOT / "addon/Fusion360MCP/server/effects.json"


def names(source: str, marker: str) -> list[str]:
    start = source.index(marker)
    opening = source.index("{", start)
    closing = source.index("};", opening)
    return re.findall(r'"([a-z0-9_]+)"', source[opening:closing])


def generate() -> dict:
    source = SOURCE.read_text()
    operations = names(source, "OperationCommands =")
    external = names(source, "ExternalOutputCommands =")
    if len(operations) != len(set(operations)) or not set(external) <= set(operations):
        raise ValueError("Invalid ToolEffects.cs sets")
    return {
        "operation_commands": sorted(operations),
        "external_output_commands": sorted(external),
        "document_mutation_commands": sorted(set(operations) - set(external)),
    }


if __name__ == "__main__":
    OUTPUT.write_text(json.dumps(generate(), indent=2) + "\n")
    print(OUTPUT)
