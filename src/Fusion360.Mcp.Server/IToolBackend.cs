using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.Server;

/// <summary>
/// Routes a single add-in command to its destination (the running Fusion
/// 360 add-in over TCP, or the Core mock layer) and returns the reference's
/// result dict. Port of the reference <c>_send(mode, command_type, params)</c>
/// (server.py) plus the connection reset after an error.
/// </summary>
public interface IToolBackend
{
    /// <summary>
    /// Sends <paramref name="toolName"/> with <paramref name="arguments"/>
    /// and returns the reference result dict (success fields, or
    /// <c>ok:false</c> + error_kind/error_message/hints/traceback for an
    /// application-level failure).
    /// </summary>
    /// <exception cref="FusionError">
    /// A structured add-in error frame (TIMEOUT on a mutation, or a
    /// classified error) — the connection itself is healthy.
    /// </exception>
    /// <exception cref="ConnectionException">
    /// A transport-level failure (not connected / connection lost).
    /// </exception>
    JsonObject Invoke(string toolName, JsonObject arguments);

    /// <summary>
    /// Drops the cached add-in connection so the next call reconnects
    /// (reference <c>reset_connection()</c>). No-op for the mock backend.
    /// </summary>
    void Reset();
}
