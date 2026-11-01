using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.Server;

/// <summary>
/// <c>--mode socket</c>: forwards commands to the running Fusion 360 add-in
/// over a persistent TCP connection (newline-JSON, retry/timeout semantics
/// from Core's <see cref="Fusion360Connection"/> — port of the reference
/// connection.py). <see cref="FUSION_MCP_HOST"/>/<see cref="FUSION_MCP_PORT"/>
/// (or the --host/--port flags) select the endpoint.
/// </summary>
public sealed class SocketToolBackend : IToolBackend
{
    private readonly Fusion360Connection _connection;

    public SocketToolBackend(string host, int port)
    {
        _connection = new Fusion360Connection(host, port);
    }

    /// <inheritdoc />
    public JsonObject Invoke(string toolName, JsonObject arguments)
    {
        // A success frame carries the reference result dict even when the
        // command failed at the application level (result.ok == false); the
        // add-in classifies that and returns it inside a success envelope.
        var result = _connection.SendCommand(toolName, arguments);
        return result.Fields;
    }

    /// <inheritdoc /> Drops the socket so the next call reconnects.
    public void Reset() => _connection.Disconnect();
}
