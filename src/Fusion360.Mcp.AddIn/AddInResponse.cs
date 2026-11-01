using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// One add-in response frame — either a success frame carrying a
/// <see cref="CommandResult"/> object, or the reference's legacy error
/// envelope (reference socket_server.py / event_bridge.py wire format):
///   success: {"status":"success","result":{ ...CommandResult fields... }}
///   error:   {"status":"error","message":"...","error_kind"?:"...","hints"?:[...],"traceback"?:"..."}
/// </summary>
public sealed class AddInResponse
{
    private readonly JsonObject _root;

    public AddInResponse(JsonObject root) => _root = root;

    /// <summary>The raw frame object (inspected by tests and the socket layer).</summary>
    public JsonObject Root => _root;

    /// <summary>Encodes the frame to a single JSON line (no trailing newline).</summary>
    public string ToJson() => _root.ToJsonString();

    /// <summary>Success frame: {"status":"success","result":{...}}.</summary>
    public static AddInResponse Success(CommandResult result)
        => new(new JsonObject
        {
            ["status"] = "success",
            ["result"] = result.Fields,
        });

    /// <summary>Infrastructure error frame (invalid JSON, bridge timeout, unknown command, ...).</summary>
    public static AddInResponse Error(string message, ErrorKind? errorKind = null, string[]? hints = null, string? traceback = null)
        => new(ProtocolCodec.BuildErrorFrame(message, errorKind, hints, traceback));
}
