#!/usr/bin/env python3
"""Create a reproducible Fusion add-in zip from checked-in Python files."""

import argparse
import json
from pathlib import Path
import re
import zipfile

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "addon/Fusion360MCP"


def package(output: Path) -> None:
    manifest = json.loads((SOURCE / "Fusion360MCP.manifest").read_text())
    if manifest.get("autodeskProduct") != "Fusion360" or manifest.get("supportedOS") != "windows|mac":
        raise ValueError("Python add-in manifest is not valid for Windows and macOS")
    version_match = re.search(r"<Version>([^<]+)</Version>",
                              (ROOT / "Directory.Build.props").read_text())
    if not version_match or manifest.get("version") != version_match.group(1):
        raise ValueError("Python add-in manifest version differs from server version")
    required = [SOURCE / "Fusion360MCP.py", SOURCE / "server/command_handler.py",
                SOURCE / "server/event_bridge.py", SOURCE / "server/socket_server.py"]
    if not all(path.is_file() for path in required):
        raise ValueError("Python add-in source is incomplete")

    files = [(path, Path("Fusion360MCP") / path.relative_to(SOURCE))
             for path in sorted(SOURCE.rglob("*")) if path.is_file() and "__pycache__" not in path.parts]
    files += [
        (ROOT / "addon/LICENSE.upstream", Path("Fusion360MCP/LICENSE.upstream")),
        (ROOT / "addon/UPSTREAM.md", Path("Fusion360MCP/UPSTREAM.md")),
        (ROOT / "scripts/install-addon.sh", Path("Fusion360MCP/install-addon.sh")),
        (ROOT / "scripts/install-addon.ps1", Path("Fusion360MCP/install-addon.ps1")),
    ]
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for path, name in sorted(files, key=lambda item: str(item[1])):
            info = zipfile.ZipInfo(str(name), date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (0o755 if name.name.endswith(".sh") else 0o644) << 16
            archive.writestr(info, path.read_bytes())
    print(output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path, nargs="?", default=ROOT / "dist/Fusion360MCP-python.zip")
    package(parser.parse_args().output)
