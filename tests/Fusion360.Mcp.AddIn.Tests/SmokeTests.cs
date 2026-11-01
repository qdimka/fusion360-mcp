using Fusion360.Mcp.AddIn;
using Xunit;

namespace Fusion360.Mcp.AddIn.Tests;

public class SmokeTests
{
    [Fact]
    public void AddIn_EntryPointIsConstructible()
    {
        var addIn = new Fusion360MCPAddIn();

        Assert.NotNull(addIn);
        Assert.IsAssignableFrom<adsk.core.AddIn>(addIn);
    }

    [Fact]
    public void DispatchHandler_ImplementsTheHandlerContract()
    {
        var handler = new CommandHandler();

        Assert.IsAssignableFrom<IAddInCommandHandler>(handler);
    }
}
