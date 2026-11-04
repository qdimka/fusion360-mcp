# Docker for fusion360-mcp

How to build and run the MCP server in Docker on this machine
(macOS + Docker Desktop 28.5.1, compose v2.40.3).

## What the image is (and is not)

- The image ships the **stdio MCP server** (`Fusion360.Mcp.Server`, .NET 10).
  An MCP client talks JSON-RPC over the process's stdin/stdout.
- It is a **std-in/std-out process, not a network server**. It opens **no
  inbound ports**. Its only network path is **outbound**: the container
  connects to the Fusion 360 add-in at `FUSION_MCP_HOST:PORT` (default
  `127.0.0.1:9876`; inside Docker Desktop that is `host.docker.internal:9876`).
- The Python add-in is **not** in the image — install it inside Fusion on the
  host using the platform script. The image contains only the C# MCP server.
- The 92-tool catalog is embedded in the `Fusion360.Mcp.Core` assembly
  (`tools_catalog.json`), so `tools/list` works with no add-in connected.

## Build

```bash
cd <repo>
docker compose build            # or: docker build -t fusion360-mcp .
```

The multi-stage `Dockerfile` restores + publishes only the net10 server,
then copies the published output into the aspnet runtime image.

## Run / client commands (fixed — siblings use these)

The client **attaches the process's stdio**. For Docker that means
`docker run -i` (interactive stdin), piping JSON-RPC on the **host side**.

```bash
# Socket mode (talk to the Fusion add-in on the host, port 9876)
docker run -i --rm \
  -e FUSION_MCP_HOST=host.docker.internal \
  -e FUSION_MCP_PORT=9876 \
  -e FUSION_MCP_JOURNAL_DIR=/data -v fusion360-mcp-journal:/data \
  fusion360-mcp

# Mock mode (no Fusion needed — answers from Core's mock data)
docker run -i --rm fusion360-mcp --mode mock
```

MCP clients wire this up as a **command** they spawn, e.g. in
`claude_desktop_config.json` / `mcp.json`:

```json
{
  "mcpServers": {
    "fusion360": {
      "command": "docker",
      "args": [
        "run", "-i", "--rm",
        "-e", "FUSION_MCP_HOST=host.docker.internal",
        "-e", "FUSION_MCP_PORT=9876",
        "fusion360-mcp"
      ]
    },
    "fusion360-mock": {
      "command": "docker",
      "args": ["run", "-i", "--rm", "fusion360-mcp", "--mode", "mock"]
    }
  }
}
```

`docker compose` equivalent (a long-lived reference/CI container; the
service keeps stdin open but is not itself an inbound endpoint — you cannot
attach a new client to the already-running process's stdin, so use
`docker run -i` for real sessions; the compose service exists for
`up -d`/`down` bookkeeping and CI):

```bash
docker compose up -d fusion360-mcp     # service stays "Up" (stdin_open)
docker compose ps                      # verify STATUS: Up
docker compose logs fusion360-mcp     # diagnostics go to stderr here
docker compose down
```

## Smoke test (verify the MCP handshake without Fusion)

Pipe an `initialize` + `tools/list` over stdin. One JSON-RPC record per line
(newline-delimited). `--mode mock` needs no add-in:

```bash
( printf '%s\n' \
    '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"smoke","version":"1.0"}}}' \
    '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
  sleep 5 ) | docker run -i --rm fusion360-mcp --mode mock
```

Expected: an `initialize` result (serverInfo) followed by a `tools/list`
result containing the full 92-tool catalog.

> **Note — keep stdin open.** The server is a stdio process that exits the
> moment stdin hits EOF. If you close stdin immediately (a bare `printf |`
> or a `<<EOF` heredoc that ends right after the requests), the process can
> terminate before it flushes its responses, and you get an empty stdout.
> The `sleep` in the example holds stdin open long enough for the responses
> to be written. Real MCP clients (Claude Desktop, OpenCode, Hermes, ...)
> keep the child process's stdin open for the session's whole lifetime, so
> they are not affected by this.

## Mock vs. socket mode

- `--mode mock` (also the default for smoke tests): every tool call returns
  canned data from Core. Use it to exercise the protocol / catalog with no
  Fusion running.
- `--mode socket` (default): forwards each tool call to the add-in over TCP.
  The add-in must be running inside Fusion 360 on the host and listening on
  `FUSION_MCP_HOST:PORT`.

## Troubleshooting

- **"Not connected"** on a socket-mode tool call → the add-in is not
  running in Fusion 360. Start it: *Shift+S → Add-Ins → Fusion360MCP → Run*.
  The container then needs to reach `host.docker.internal:9876`.
- **Timeouts** → the Fusion main thread is blocked (a modal dialog, or a
  heavy re-solve). After any error, call `get_scene_info` to see whether a
  mutation was partially applied before retrying.
- **Add-in log**: `~/fusion360mcp.log` on the host.
- Server diagnostics always go to **stderr** (stdout is the JSON-RPC channel);
  `docker logs <service>` shows them.
