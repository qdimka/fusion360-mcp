#!/bin/sh
# Install the macOS Python add-in. Fusion for macOS does not load this
# repository's C#/.NET Framework add-in; that package is Windows-only.
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_DIR=$(dirname "$SCRIPT_DIR")

case "$(uname -s)" in
  Darwin)
    exec "$REPO_DIR/reference/fusion360-mcp-server/scripts/install-addon.sh" "$@"
    ;;
  MINGW*|MSYS*|CYGWIN*)
    echo "On Windows, run: powershell -ExecutionPolicy Bypass -File scripts/install-addon.ps1" >&2
    exit 1
    ;;
  *)
    echo "Fusion add-in installation is supported here only for macOS (Python add-in)." >&2
    exit 1
    ;;
esac
