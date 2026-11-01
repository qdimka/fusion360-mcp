using Fusion360.Mcp.Server;

namespace Fusion360.Mcp.Server.Tests;

public sealed class ServerOptionsTests
{
    [Fact]
    public void ServerInfoVersion_DefaultsToAssemblyVersion()
    {
        var server = new Fusion360McpServer(new MockToolBackend());
        var expected = typeof(Fusion360McpServer).Assembly.GetName().Version!.ToString(3);

        Assert.Equal(expected, server.CreateOptions().ServerInfo!.Version);
    }

    [Fact]
    public void ServerInfoVersion_AllowsAnExplicitOverride()
    {
        var server = new Fusion360McpServer(new MockToolBackend());

        Assert.Equal("2.3.4", server.CreateOptions(serverVersion: "2.3.4").ServerInfo!.Version);
    }
}
