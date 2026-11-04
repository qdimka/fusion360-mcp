# Install the Fusion360 MCP server

Use these steps when an AI coding agent installs the server for you.

## Prerequisites

- Fusion 360 on Windows or macOS. Windows installer CI is required before
  release; live Windows Fusion E2E is scheduled after release.
- .NET 10 SDK when building from source.
- Docker Desktop when using the container image.
- An MCP client such as Claude Code, OpenCode, Codex, or Cursor.
- Access to the Fusion host and permission to update the MCP client configuration.

## Installation steps

1. Check the platform where Fusion 360 is running.
2. Install the add-in for that Fusion host:

   **macOS** (checked-in Python add-in, from the repository root):

   ```bash
   ./scripts/install-addon.sh
   ```

   **Windows** (the same Python add-in):

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-addon.ps1
   ```

   For a release download, extract `Fusion360MCP-python.zip` outside the
   AddIns directory and run `Fusion360MCP/install-addon.sh` with `sh` on
   macOS or `Fusion360MCP\install-addon.ps1` with PowerShell on Windows.
   Both installers are included in the zip.

3. If an older C# add-in is installed, stop or remove it in Fusion before
   starting the Python add-in. This repository no longer builds or distributes
   the C# add-in. Keep the server's operation journal. Start or restart Fusion
   360, then run **Shift+S → Add-Ins → Fusion360MCP → Run**; the Python manifest
   does not enable autostart.
4. Confirm that Fusion's TEXT COMMANDS window shows:

   ```text
   [MCP] Server listening on localhost:9876
   ```

5. Choose one server installation method:

   - From source:

     ```bash
     dotnet build Fusion360Mcp.sln -c Release
     dotnet src/Fusion360.Mcp.Server/bin/Release/net10.0/Fusion360.Mcp.Server.dll --mode socket
     ```

   - With Docker:

     ```bash
     docker build -t fusion360-mcp:local .
     docker run --rm -i -e FUSION_MCP_HOST=host.docker.internal \
       -e FUSION_MCP_PORT=9876 -e FUSION_MCP_JOURNAL_DIR=/data \
       -v fusion360-mcp-journal:/data fusion360-mcp:local --mode socket
     ```

6. Configure the MCP client to launch the server over stdio. For Claude Code:

   ```bash
   claude mcp add fusion360 -- /path/to/Fusion360.Mcp.Server --mode socket
   ```

   For Docker, use `docker` as the command and pass `run`, `--rm`, `-i`, the `fusion360-mcp-journal` volume, and the image name as arguments.
7. Preserve the `fusion360-mcp-journal` volume between runs. Do not delete it during routine upgrades.
8. Call the `ping` tool. The expected response is:

   ```json
   {"ok": true, "status": "pong"}
   ```

## macOS and remote Fusion hosts

Both installers copy the checked-in Python add-in by default; their symlink
options are for development. Docker with `--mode mock` is available without a
Fusion host. For a remote Fusion host, set `FUSION_MCP_HOST` and optionally
`FUSION_MCP_PORT` on the server, and set both `FUSION_MCP_HOST` to the bind
address and `FUSION_MCP_ALLOW_REMOTE=1` on the Fusion host. The TCP connection
has no authentication, so use only a trusted network.

## Safety rules for the agent

- Do not delete Fusion documents, exports, or the journal volume.
- Do not restart an already running Fusion instance automatically.
- After a failed mutation, call `get_scene_info` before retrying.
- Report the exact `ping` result and any step that requires manual action.
