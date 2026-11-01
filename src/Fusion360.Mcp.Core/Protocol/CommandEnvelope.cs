using System.Text.Json.Nodes;

namespace Fusion360.Mcp.Core.Protocol;

/// <summary>
/// One request sent to the Fusion add-in.
/// Wire form (newline-delimited JSON, identical to the reference
/// fusion360-mcp-server): {"type":"extrude","params":{...}}
/// Port of the request payload from reference src/fusion360_mcp/connection.py
/// (which sends {"type": command, "params": params}).
/// </summary>
public sealed class CommandEnvelope
{
    /// <summary>Command name (tool name, e.g. "extrude"; "ping" for health checks).</summary>
    public required string Command { get; init; }

    /// <summary>Command parameters; empty object when the command takes none.</summary>
    public JsonNode? Params { get; init; }

    public static CommandEnvelope Create(string command, JsonNode? parameters = null)
        => new() { Command = command, Params = parameters };

    /// <summary>Serializes the envelope to a single JSON line (no trailing newline).</summary>
    public string Encode() => ProtocolCodec.EncodeCommand(this);

    /// <summary>Parses a request line produced by <see cref="Encode"/>.</summary>
    public static CommandEnvelope Parse(string jsonLine) => ProtocolCodec.ParseCommand(jsonLine);
}
