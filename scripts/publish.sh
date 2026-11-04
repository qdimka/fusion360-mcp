#!/bin/bash
# Publish versioned MCP server and Python Fusion add-in artifacts:
#
#   1. MCP server single-file builds for 4 RIDs (win-x64, osx-arm64,
#      osx-x64, linux-x64) -> dist/Fusion360.Mcp.Server/<rid>/
#      Self-contained (~80 MB, runtime bundled) — target machine needs
#      no .NET install.
#   2. Python add-in zip for Windows and macOS -> dist/Fusion360MCP-python.zip
#      Contains the AddIns folder layout (Fusion360MCP/...).
#
# Usage: ./scripts/publish.sh [rid ...]   (default: all four RIDs)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(dirname "$SCRIPT_DIR")"
DIST="$REPO_DIR/dist"

RIDS=("$@")
[ "${#RIDS[@]}" -eq 0 ] && RIDS=(win-x64 osx-arm64 osx-x64 linux-x64)

SERVER_PROJ="$REPO_DIR/src/Fusion360.Mcp.Server/Fusion360.Mcp.Server.csproj"

echo "== Publishing MCP server (single-file, self-contained) =="
mkdir -p "$DIST/Fusion360.Mcp.Server"
for rid in "${RIDS[@]}"; do
    echo "  $rid"
    rm -rf "$DIST/Fusion360.Mcp.Server/$rid"
    dotnet publish "$SERVER_PROJ" -c Release -r "$rid" \
        -p:SelfContained=true -p:PublishSingleFile=true -p:DebugType=none \
        -o "$DIST/Fusion360.Mcp.Server/$rid" --nologo
    python3 - "$DIST/Fusion360.Mcp.Server/$rid" "$DIST/Fusion360.Mcp.Server-$rid.zip" "$rid" <<'PY'
from pathlib import Path
import stat
import sys
import zipfile

source = Path(sys.argv[1])
archive_path = Path(sys.argv[2])
prefix = sys.argv[3]
with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for path in sorted(source.rglob("*"), key=lambda item: item.relative_to(source).as_posix()):
        if not path.is_file():
            continue
        relative = Path(prefix) / path.relative_to(source)
        info = zipfile.ZipInfo(relative.as_posix(), date_time=(1980, 1, 1, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        mode = stat.S_IFREG | stat.S_IMODE(path.stat().st_mode)
        info.external_attr = mode << 16
        archive.writestr(info, path.read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)
PY
done

echo "== Packaging Python add-in =="
python3 "$REPO_DIR/scripts/package_addin.py"

echo "== Writing SHA256SUMS =="
python3 - "$DIST" <<'PY'
import hashlib
from pathlib import Path
import sys
root = Path(sys.argv[1])
files = sorted(root.glob('*.zip'))
(root / 'SHA256SUMS').write_text(''.join(
    f'{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.relative_to(root)}\n'
    for path in files
))
PY

echo ""
echo "Artifacts in $DIST:"
find "$DIST" -maxdepth 3 -type f | sort
