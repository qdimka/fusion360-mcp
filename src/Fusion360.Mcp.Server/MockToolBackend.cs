using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Mock;

namespace Fusion360.Mcp.Server;

/// <summary>
/// <c>--mode mock</c>: answers every command from Core's mock layer with no
/// network. Every response carries <c>"mode":"mock"</c> (plus synthetic
/// deltas for mutations, and the <c>__mock_error__</c> sentinel forces a
/// classified error). Port of reference <c>mock_command</c>.
/// </summary>
public sealed class MockToolBackend : IToolBackend
{
    public JsonObject Invoke(string toolName, JsonObject arguments)
        => MockResponses.MockCommand(toolName, arguments);

    /// <inheritdoc /> No persistent connection to drop in mock mode.
    public void Reset()
    {
    }
}
