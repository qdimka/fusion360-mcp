"""Tool effect sets generated from the C# catalog; see generate_addin_effects.py."""

import json
from pathlib import Path

_effects = json.loads(Path(__file__).with_name("effects.json").read_text())
OPERATION_COMMANDS = frozenset(_effects["operation_commands"])
EXTERNAL_OUTPUT_COMMANDS = frozenset(_effects["external_output_commands"])
DOCUMENT_MUTATION_COMMANDS = frozenset(_effects["document_mutation_commands"])
