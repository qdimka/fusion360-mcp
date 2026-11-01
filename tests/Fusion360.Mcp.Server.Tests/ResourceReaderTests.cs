using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Mock;
using Fusion360.Mcp.Server;

namespace Fusion360.Mcp.Server.Tests;

public class ResourceReaderTests
{
    private static ResourceReader NewReader() => new(new MockToolBackend());

    [Fact]
    public void Status_Resource_ContainsConnectedTrueAndPing()
    {
        var text = NewReader().Read("fusion360://status");
        var obj = JsonNode.Parse(text)!;
        Assert.Equal("true", obj["connected"]!.ToJsonString());
        Assert.NotNull(obj["ping"]);
        // The mock ping carries the marker.
        Assert.Contains("\"mode\"", obj["ping"]!.ToJsonString());
    }

    [Fact]
    public void Design_Resource_ReturnsSceneInfo()
    {
        var text = NewReader().Read("fusion360://design");
        Assert.Contains("MockDesign", text);
    }

    [Fact]
    public void Parameters_Resource_ReturnsParametersObject()
    {
        var text = NewReader().Read("fusion360://parameters");
        var obj = JsonNode.Parse(text)!;
        // get_parameters result is a plain JSON object (no exception, has content).
        Assert.NotNull(obj);
        Assert.True(obj is JsonObject);
    }

    [Fact]
    public void BodyTemplate_ExtractsNameAndQueriesObjectInfo()
    {
        var text = NewReader().Read("fusion360://body/Body1");
        Assert.Contains("Body1", text);
    }

    [Fact]
    public void ComponentTemplate_ExtractsName()
    {
        var text = NewReader().Read("fusion360://component/RootComponent");
        Assert.Contains("RootComponent", text);
    }

    [Fact]
    public void UnknownUri_ReturnsErrorObject()
    {
        var text = NewReader().Read("fusion360://bogus");
        Assert.Contains("error", text);
    }

    [Fact]
    public void EmptyTemplateName_ReturnsErrorObject()
    {
        var text = NewReader().Read("fusion360://body/");
        Assert.Contains("error", text);
    }
}
