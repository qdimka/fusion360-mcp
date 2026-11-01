#!/bin/bash
# Publish artifacts for the Fusion360MCP C# port (README §Development):
#
#   1. MCP server single-file builds for 4 RIDs (win-x64, osx-arm64,
#      osx-x64, linux-x64) -> dist/Fusion360.Mcp.Server/<rid>/
#      Self-contained (~80 MB, runtime bundled) — target machine needs
#      no .NET install.
#   2. Add-in install zip (net48, cross-platform: one binary works on
#      Windows/macOS/Linux) -> dist/Fusion360MCP-addin.zip
#      Contains the AddIns folder layout (Fusion360MCP/...) ready to be
#      extracted into Fusion's AddIns directory; adsknet.dll stub and
#      .pdb files are stripped (runtime resolves adsknet in-process).
#
# Usage: ./scripts/publish.sh [rid ...]   (default: all four RIDs)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(dirname "$SCRIPT_DIR")"
DIST="$REPO_DIR/dist"

RIDS=("$@")
[ "${#RIDS[@]}" -eq 0 ] && RIDS=(win-x64 osx-arm64 osx-x64 linux-x64)

SERVER_PROJ="$REPO_DIR/src/Fusion360.Mcp.Server/Fusion360.Mcp.Server.csproj"
ADDIN_PROJ="$REPO_DIR/src/Fusion360.Mcp.AddIn/Fusion360.Mcp.AddIn.csproj"

echo "== Publishing MCP server (single-file, self-contained) =="
for rid in "${RIDS[@]}"; do
    echo "  $rid"
    dotnet publish "$SERVER_PROJ" -c Release -r "$rid" \
        -p:SelfContained=true -p:PublishSingleFile=true -p:DebugType=none \
        -o "$DIST/Fusion360.Mcp.Server/$rid" --nologo
done

echo "== Packaging add-in (net48) =="
rm -rf "$DIST/addin-staging"
dotnet build "$ADDIN_PROJ" -c Release -o "$DIST/addin-staging/build" --nologo
STAGE="$DIST/addin-staging/Fusion360MCP"
rm -rf "$STAGE"
mkdir -p "$STAGE"
cp -R "$DIST/addin-staging/build/." "$STAGE/"
# Never ship the adsknet compile-time stub or debug symbols into AddIns:
# at runtime the add-in resolves adsknet in-process from the Fusion runtime.
rm -f "$STAGE"/adsknet.dll "$STAGE"/*.pdb "$STAGE"/Microsoft.CodeAnalysis*.dll
( cd "$DIST/addin-staging" && zip -qr "$DIST/Fusion360MCP-addin.zip" Fusion360MCP )
rm -rf "$DIST/addin-staging"

echo ""
echo "Artifacts in $DIST:"
find "$DIST" -maxdepth 3 -type f | sort
