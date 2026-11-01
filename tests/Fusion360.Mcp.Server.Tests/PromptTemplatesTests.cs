using System.Text.Json;
using Fusion360.Mcp.Server;
using ModelContextProtocol.Protocol;

namespace Fusion360.Mcp.Server.Tests;

public class PromptTemplatesTests
{
    private static IDictionary<string, JsonElement> Args(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => JsonDocument.Parse($"\"{p.Value}\"").RootElement.Clone());

    private static string Body(string name, IDictionary<string, JsonElement> args)
    {
        var result = PromptTemplates.Build(name, args);
        return ((TextContentBlock)result.Messages[0].Content).Text;
    }

    [Fact]
    public void All_ReturnsThreePrompts_WithExpectedNames()
    {
        var names = PromptTemplates.All.Select(p => p.Name).ToArray();
        Assert.Equal(3, names.Length);
        Assert.Contains("create-box", names);
        Assert.Contains("model-threaded-bolt", names);
        Assert.Contains("sheet-metal-enclosure", names);
    }

    [Fact]
    public void CreateBox_UsesArguments()
    {
        var text = Body("create-box", Args(("length", "12"), ("width", "6"), ("height", "4")));
        // Reference quirk (faithful): rectangle width comes from the width arg, height from the length arg.
        Assert.Contains("draw_rectangle width=6 height=12", text);
        Assert.Contains("extrude height=4", text);
    }

    [Fact]
    public void CreateBox_DefaultsWhenNoArguments()
    {
        var text = Body("create-box", Args());
        Assert.Contains("draw_rectangle width=5 height=10", text);
        Assert.Contains("extrude height=3", text);
    }

    [Fact]
    public void ThreadedBolt_DefaultDesignation()
    {
        var text = Body("model-threaded-bolt", Args());
        Assert.Contains("M10x1.5", text);
        Assert.Contains("create_thread designation=M10x1.5", text);
    }

    [Fact]
    public void SheetMetal_DefaultDimensions()
    {
        var text = Body("sheet-metal-enclosure", Args());
        Assert.Contains("20x10x5", text);
    }

    [Fact]
    public void UnknownPrompt_ReportsNoTemplate()
    {
        var text = Body("does-not-exist", Args());
        Assert.Contains("No template for prompt: does-not-exist", text);
    }
}
