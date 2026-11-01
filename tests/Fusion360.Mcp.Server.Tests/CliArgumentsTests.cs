using Fusion360.Mcp.Core.Protocol;
using Fusion360.Mcp.Server;

namespace Fusion360.Mcp.Server.Tests;

public class CliArgumentsTests
{
    [Fact]
    public void NoArgs_DefaultsToSocketWithDefaults()
    {
        var (mode, host, port) = CliArguments.Parse(Array.Empty<string>());
        Assert.Equal(ServerMode.Socket, mode);
        Assert.Equal(ProtocolConstants.DefaultHost, host);
        Assert.Equal(ProtocolConstants.DefaultPort, port);
    }

    [Fact]
    public void ModeFlag_SeparateValue_SelectsMock()
    {
        var (mode, _, _) = CliArguments.Parse(new[] { "--mode", "mock" });
        Assert.Equal(ServerMode.Mock, mode);
    }

    [Fact]
    public void ModeFlag_CaseInsensitive_MatchesMock()
    {
        var (mode, _, _) = CliArguments.Parse(new[] { "--mode", "MOCK" });
        Assert.Equal(ServerMode.Mock, mode);
    }

    [Fact]
    public void ModeFlag_EqualsForm_SelectsMock()
    {
        var (mode, _, _) = CliArguments.Parse(new[] { "--mode=mock" });
        Assert.Equal(ServerMode.Mock, mode);
    }

    [Fact]
    public void UnknownMode_FallsBackToSocket()
    {
        var (mode, _, _) = CliArguments.Parse(new[] { "--mode", "nonsense" });
        Assert.Equal(ServerMode.Socket, mode);
    }

    [Fact]
    public void HostPort_SeparateValues_Applied()
    {
        var (_, host, port) = CliArguments.Parse(new[] { "--host", "10.0.0.5", "--port", "12345" });
        Assert.Equal("10.0.0.5", host);
        Assert.Equal(12345, port);
    }

    [Fact]
    public void Port_EqualsForm_Applied()
    {
        var (_, _, port) = CliArguments.Parse(new[] { "--port=4321" });
        Assert.Equal(4321, port);
    }

    [Fact]
    public void NonNumericPort_IgnoredFallsBackToDefault()
    {
        var (_, _, port) = CliArguments.Parse(new[] { "--port", "abc" });
        Assert.Equal(ProtocolConstants.DefaultPort, port);
    }

    [Fact]
    public void HostFlagWithNoValue_FallsBackToDefault()
    {
        // "--host" as the last token has no value → host stays null → default.
        var (_, host, _) = CliArguments.Parse(new[] { "--host" });
        Assert.Equal(ProtocolConstants.DefaultHost, host);
    }
}
