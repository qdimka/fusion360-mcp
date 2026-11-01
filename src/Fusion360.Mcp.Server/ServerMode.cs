namespace Fusion360.Mcp.Server;

/// <summary>
/// How the MCP server routes tool calls to the add-in.
/// Port of the reference's <c>--mode socket|mock</c> CLI flag
/// (src/fusion360_mcp/server.py::main).
/// </summary>
public enum ServerMode
{
    /// <summary>Forward commands over TCP to the running Fusion 360 add-in (default).</summary>
    Socket,

    /// <summary>Answer from Core's mock data with no network — <c>"mode":"mock"</c> on every result.</summary>
    Mock,
}
