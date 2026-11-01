using Xunit;
using Fusion360.Mcp.Core;

namespace Fusion360.Mcp.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CorePlaceholder_Exists()
    {
        Assert.Equal("0.1.0", CorePlaceholder.Version);
    }
}
