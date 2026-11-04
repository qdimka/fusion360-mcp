#!/bin/sh
# Install the checked-in Python add-in on macOS. --symlink is for development.
set -eu

if [ "$(uname -s)" != Darwin ]; then
    echo "This installer targets Fusion for macOS; use install-addon.ps1 on Windows." >&2
    exit 1
fi

mode=${1:-}
if [ -n "$mode" ] && [ "$mode" != --symlink ]; then
    echo "Usage: scripts/install-addon.sh [--symlink]" >&2
    exit 2
fi

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if [ -f "$script_dir/Fusion360MCP.py" ]; then
    source_dir="$script_dir"  # release zip layout
else
    repo_dir=$(dirname "$script_dir")
    source_dir="$repo_dir/addon/Fusion360MCP"  # source checkout layout
fi
addins_dir=${FUSION_MCP_ADDINS_DIR:-"$HOME/Library/Application Support/Autodesk/Autodesk Fusion 360/API/AddIns"}
target="$addins_dir/Fusion360MCP"
backup="$addins_dir/.Fusion360MCP-backup-$$"

if [ ! -f "$source_dir/Fusion360MCP.py" ] || [ ! -f "$source_dir/Fusion360MCP.manifest" ]; then
    echo "Python add-in package is incomplete: $source_dir" >&2
    exit 1
fi
if ! grep -Eq '"supportedOS"[[:space:]]*:[[:space:]]*"windows\|mac"' "$source_dir/Fusion360MCP.manifest"; then
    echo "Python add-in manifest does not support Windows and macOS" >&2
    exit 1
fi

mkdir -p "$addins_dir"
stage_dir=$(mktemp -d "$addins_dir/.Fusion360MCP-stage.XXXXXX")
staged="$stage_dir/Fusion360MCP"

rollback() {
    if [ ! -e "$target" ] && [ ! -L "$target" ] && [ -e "$backup" ]; then
        mv "$backup" "$target"
    fi
    rm -rf "$stage_dir"
}
trap rollback EXIT HUP INT TERM

if [ "$mode" = --symlink ]; then
    ln -s "$source_dir" "$staged"
else
    cp -R "$source_dir" "$staged"
fi

if [ ! -f "$staged/Fusion360MCP.py" ] || [ ! -f "$staged/Fusion360MCP.manifest" ]; then
    echo "Staged Python add-in failed validation" >&2
    exit 1
fi
if [ -e "$backup" ] || [ -L "$backup" ]; then
    echo "Backup path already exists: $backup" >&2
    exit 1
fi
if [ -e "$target" ] || [ -L "$target" ]; then
    mv "$target" "$backup"
fi
mv "$staged" "$target"
rm -rf "$backup" "$stage_dir"
trap - EXIT HUP INT TERM

echo "Installed Python Fusion360MCP add-in at $target"
echo "Start or restart Fusion, run the add-in if needed, then call ping and get_scene_info."
