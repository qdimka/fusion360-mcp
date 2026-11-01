# ADR-0002 — Official C# MCP SDK and TCP :9876 bridge (D2)

Status: accepted (implemented).
Date: Recorded 2026-10-30; decided in PLAN.md D2 (2026-10-27), implemented in T4.
Environment: .NET 10 server, `ModelContextProtocol` SDK 1.4.1; Fusion 2706.1.14 E2E (2026-10-29).

## Decision

- The MCP server uses Microsoft's official `ModelContextProtocol` NuGet SDK for stdio transport, tools, resources, resource templates, and prompts. The planned manual JSON-RPC fallback was unnecessary because SDK 1.4.1 supports the required surface.
- The add-in/server bridge retains the reference protocol: TCP at `FUSION_MCP_HOST` (default `localhost`) and `FUSION_MCP_PORT` (default `9876`), with newline-delimited JSON and a 64 KiB receive buffer. This keeps the reference Python add-in compatible and permits a remote Fusion host.

## Rationale

The supported API mirrors the reference project: 92 MCP tools plus resources, templates, and prompts. The official SDK avoids hand-building JSON-RPC. The TCP bridge is a public cross-machine contract and is retained for compatibility.

## Evidence recorded at implementation

- `Fusion360McpServer.cs` registers the MCP surface through the SDK; the current catalog contains 92 tools (see README and [TOOLS.md](../TOOLS.md)).
- A live socket smoke sequence against Fusion 2706.1.14 used the same :9876 protocol (`ping` then `export_stl`; see [E2E status](../E2E-STATUS.md)).
- Server tests cover the `fusion360://status`, `design`, and `parameters` resources; body/component resource templates; and the `create-box`, `model-threaded-bolt`, and `sheet-metal-enclosure` prompts in mock mode.

## Alternatives rejected

- **Implement JSON-RPC directly:** kept only as a fallback; the SDK supports the required surface.
- **Replace TCP :9876 with gRPC or named pipes:** this would break reference add-in and cross-machine compatibility.
- **Use HTTP instead of stdio for MCP:** target clients launch the server over the standard stdio transport.
