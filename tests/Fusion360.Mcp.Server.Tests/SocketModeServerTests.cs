using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Fusion360.Mcp.Server.Tests;

/// <summary>
/// Socket-mode integration: the server points at a loopback
/// <see cref="FakeTcpAddIn"/> that speaks the add-in wire protocol, and a real
/// MCP client drives it. Covers both the success path (result forwarded) and
/// the structured error path (error frame → classified FusionError).
/// </summary>
public sealed class SocketServerFixture : IAsyncLifetime
{
    public FakeTcpAddIn AddIn { get; private set; } = default!;
    public StdioMcpClient Client { get; private set; } = default!;

    public Task InitializeAsync() => StartAsync();

    private async Task StartAsync()
    {
        AddIn = new FakeTcpAddIn();

        AddIn.SuccessHandlers["get_scene_info"] = _ => new JsonObject
        {
            ["design_name"] = "FakeDesign",
            ["bodies"] = new JsonArray("BodyA", "BodyB"),
            ["sketches"] = new JsonArray("SketchA"),
            ["features"] = new JsonArray("ExtrudeA"),
            ["components"] = new JsonArray("RootComponent"),
        };
        AddIn.SuccessHandlers["ping"] = _ => new JsonObject
        {
            ["status"] = "pong",
        };
        AddIn.Errors["extrude"] =
            ("sketch has no closed profile", ErrorKind.ProfileNotClosed,
                new[] { "close the sketch", "use a closed profile" });

        Client = await StdioMcpClient.StartAsync(new[]
        {
            "--mode", "socket",
            "--host", "localhost",
            "--port", AddIn.Port.ToString(),
        });
    }

    public async Task DisposeAsync()
    {
        await Client.DisposeAsync();
        await AddIn.DisposeAsync();
    }
}

public class SocketModeServerTests : IClassFixture<SocketServerFixture>
{
    private readonly SocketServerFixture _f;

    public SocketModeServerTests(SocketServerFixture f) => _f = f;

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
            ?? throw new Xunit.Sdk.XunitException("no text content in tool result");

    [Fact]
    public async Task ToolCall_ForwardsToAddInAndReturnsResult()
    {
        var result = await _f.Client.CallToolAsync("get_scene_info", null);

        Assert.False(result.IsError);
        var text = Text(result);
        Assert.Contains("**get_scene_info** OK", text);
        Assert.Contains("FakeDesign", text);
        // The add-in received the command.
        Assert.Equal("get_scene_info", _f.AddIn.LastCommand);
    }

    [Fact]
    public async Task ToolCall_AddInErrorFrame_IsClassifiedError()
    {
        var result = await _f.Client.CallToolAsync("extrude",
            new Dictionary<string, object?> { ["sketch"] = "S1" });

        Assert.True(result.IsError);
        var text = Text(result);
        Assert.Contains("**extrude** ERROR (PROFILE_NOT_CLOSED)", text);
        Assert.Contains("sketch has no closed profile", text);
        Assert.Contains("- close the sketch", text);
    }

    [Fact]
    public async Task ResourceRead_Status_IsConnected()
    {
        var result = await _f.Client.ReadResourceAsync("fusion360://status");
        var text = ((TextResourceContents)result.Contents[0]).Text;
        Assert.Contains("\"connected\"", text);
        Assert.Contains("true", text);
        Assert.Contains("pong", text);
    }

    [Fact]
    public async Task ToolsList_StillServesFullCatalog()
    {
        // The socket backend doesn't change the advertised tool surface.
        var result = await _f.Client.ListToolsAsync();
        Assert.Equal(Fusion360.Mcp.Core.Catalog.ToolCatalog.All.Count, result.Tools.Count);
    }
}

/// <summary>
/// Socket mode with no add-in listening: every call surfaces a
/// connection-error tool result telling the user to start the add-in.
/// </summary>
public class SocketModeNoAddInTests
{
    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;

    [Fact]
    public async Task ToolCall_WhenNoAddInListening_ReturnsConnectionError()
    {
        // Reserve a port, release it, then point the server at the dead port.
        await using var probe = new FakeTcpAddIn();
        var deadPort = probe.Port;
        await probe.DisposeAsync();

        await using var client = await StdioMcpClient.StartAsync(new[]
        {
            "--mode", "socket", "--host", "localhost", "--port", deadPort.ToString(),
        });

        var result = await client.CallToolAsync("get_scene_info", null);

        Assert.True(result.IsError);
        Assert.Contains("Make sure Fusion 360 is running", Text(result));
    }
}
