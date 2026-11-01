# Install the Fusion360 MCP server

Use these steps when an AI coding agent installs the server for you.

## Prerequisites

- Fusion 360 for Windows for the C# add-in.
- .NET 10 SDK when building from source.
- Docker Desktop when using the container image.
- An MCP client such as Claude Code, OpenCode, Codex, or Cursor.
- Access to the Fusion host and permission to update the MCP client configuration.

## Installation steps

1. Check the platform where Fusion 360 is running.
2. Install the add-in for that Fusion host:

   **macOS** (reference Python add-in):

   ```bash
   ./scripts/install-addon.sh
   ```

   **Windows** (C# add-in):

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-addon.ps1
   ```

3. Start Fusion 360 and run **Shift+S → Add-Ins → Fusion360MCP → Run** if the add-in did not start automatically.
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
     docker run --rm -i -v fusion360-mcp-journal:/data \
       fusion360-mcp:local --mode socket
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

On macOS, `./scripts/install-addon.sh` installs the reference Python add-in for a local Fusion connection. The C# add-in is Windows-only. Docker with `--mode mock` is available without a Fusion host. For a remote host, set `FUSION_MCP_HOST` and `FUSION_MCP_PORT` on the server side, and explicitly enable `FUSION_MCP_ALLOW_REMOTE=1` on the Fusion host before binding to a non-loopback address.

## Safety rules for the agent

- Do not delete Fusion documents, exports, or the journal volume.
- Do not restart an already running Fusion instance automatically.
- After a failed mutation, call `get_scene_info` before retrying.
- Report the exact `ping` result and any step that requires manual action.
