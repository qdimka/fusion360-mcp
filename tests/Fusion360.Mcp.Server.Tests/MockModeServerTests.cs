using System.Text.Json;
using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Catalog;
using Fusion360.Mcp.Core.Mock;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Fusion360.Mcp.Server.Tests;

/// <summary>Shared mock-mode server process, driven by a real MCP client.</summary>
public sealed class MockServerFixture : IAsyncLifetime
{
    public StdioMcpClient Client { get; private set; } = default!;

    public Task InitializeAsync() => StartAsync();

    private async Task StartAsync()
    {
        Client = await StdioMcpClient.StartAsync(["--mode", "mock"]);
    }

    public async Task DisposeAsync()
    {
        await Client.DisposeAsync();
    }
}

public class MockModeServerTests : IClassFixture<MockServerFixture>
{
    private readonly MockServerFixture _f;

    public MockModeServerTests(MockServerFixture f) => _f = f;

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
            ?? throw new Xunit.Sdk.XunitException("no text content in tool result");

    [Fact]
    public async Task ToolsList_ReturnsAllCatalogTools()
    {
        var result = await _f.Client.ListToolsAsync();
        var names = result.Tools.Select(t => t.Name).ToHashSet();

        Assert.Equal(ToolCatalog.All.Count, result.Tools.Count);
        Assert.Equal(new HashSet<string>(ToolCatalog.All.Select(t => t.Name)), names);
    }

    [Fact]
    public async Task ToolsList_Schema_MatchesCatalogForSample()
    {
        var result = await _f.Client.ListToolsAsync();
        var tool = result.Tools.Single(t => t.Name == "create_box");

        var wire = JsonNode.Parse(tool.InputSchema.GetRawText());
        var expected = JsonNode.Parse(ToolCatalog.GetByName("create_box")!.InputSchema.ToJsonString());
        Assert.True(JsonNode.DeepEquals(wire, expected),
            $"create_box inputSchema on the wire does not match the catalog.\nwire={wire}\nexpected={expected}");
    }

    [Fact]
    public async Task ResourcesList_ReturnsThreeUris()
    {
        var result = await _f.Client.ListResourcesAsync();
        var uris = result.Resources.Select(r => r.Uri).OrderBy(u => u).ToArray();

        Assert.Equal(3, result.Resources.Count);
        Assert.Equal(new[]
        {
            "fusion360://design",
            "fusion360://parameters",
            "fusion360://status",
        }, uris);
    }

    [Fact]
    public async Task TemplatesList_ReturnsTwoTemplates()
    {
        var result = await _f.Client.ListResourceTemplatesAsync();
        var uris = result.ResourceTemplates.Select(t => t.UriTemplate).OrderBy(u => u).ToArray();

        Assert.Equal(2, result.ResourceTemplates.Count);
        Assert.Contains("fusion360://body/{name}", uris);
        Assert.Contains("fusion360://component/{name}", uris);
    }

    [Fact]
    public async Task PromptsList_ReturnsThreePrompts()
    {
        var result = await _f.Client.ListPromptsAsync();
        var names = result.Prompts.Select(p => p.Name).ToHashSet();

        Assert.Equal(3, result.Prompts.Count);
        Assert.Contains("create-box", names);
        Assert.Contains("model-threaded-bolt", names);
        Assert.Contains("sheet-metal-enclosure", names);
    }

    [Fact]
    public async Task EveryTool_CallSucceedsWithMockMarker()
    {
        // Acceptance criterion: mock mode responds to any tool with a mock marker.
        var failures = new List<string>();

        foreach (var tool in ToolCatalog.All)
        {
            var result = await _f.Client.CallToolAsync(tool.Name, null);
            var text = Text(result);
            if (result.IsError is true || !text.Contains("mode: mock"))
            {
                failures.Add($"'{tool.Name}' isError={result.IsError} text={text}");
            }
        }

        Assert.True(failures.Count == 0,
            $"Tools that did not return a success with the mock marker ({failures.Count}):\n{string.Join("\n", failures)}");
    }

    [Fact]
    public async Task ToolCall_EchoesArguments()
    {
        var result = await _f.Client.CallToolAsync("create_box",
            new Dictionary<string, object?> { ["length"] = 10.0, ["width"] = 5.0, ["height"] = 3.0 });

        var text = Text(result);
        Assert.False(result.IsError);
        Assert.Contains("length: 10", text);
        Assert.Contains("width: 5", text);
        Assert.Contains("height: 3", text);
    }

    [Fact]
    public async Task ToolCall_ForcedError_IsClassifiedError()
    {
        var args = new Dictionary<string, object?> { [MockResponses.MockErrorSentinel] = "bad value" };
        var result = await _f.Client.CallToolAsync("get_scene_info", args);

        Assert.True(result.IsError);
        var text = Text(result);
        Assert.Contains("ERROR", text);
        Assert.Contains("bad value", text);
    }

    [Fact]
    public async Task ToolCall_AfterForcedError_StillWorks()
    {
        // The mock reset is a no-op; the next call must still succeed.
        var errArgs = new Dictionary<string, object?> { [MockResponses.MockErrorSentinel] = "x" };
        _ = await _f.Client.CallToolAsync("get_scene_info", errArgs);

        var result = await _f.Client.CallToolAsync("get_scene_info", null);
        Assert.False(result.IsError);
        Assert.Contains("MockDesign", Text(result));
    }

    [Fact]
    public async Task ResourceRead_Status_IsConnected()
    {
        var result = await _f.Client.ReadResourceAsync("fusion360://status");
        var text = ((TextResourceContents)result.Contents[0]).Text;
        Assert.Contains("\"connected\"", text);
        Assert.Contains("true", text);
    }

    [Fact]
    public async Task PromptGet_CreateBox_UsesArguments()
    {
        var args = new Dictionary<string, object?>
        {
            ["length"] = "12", ["width"] = "6", ["height"] = "4",
        };
        var result = await _f.Client.GetPromptAsync("create-box", args);
        var text = ((TextContentBlock)result.Messages[0].Content).Text;

        Assert.Contains("draw_rectangle width=6 height=12", text);
        Assert.Contains("extrude height=4", text);
    }
}
