using Xunit;
using Fusion360.Mcp.AddIn;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// File logging semantics (reference addon/server/__init__.py): the
/// "yyyy-MM-dd HH:mm:ss [LEVEL] fusion360mcp.&lt;category&gt;: message"
/// line format, and 2 MB rotation with 3 backups.
/// </summary>
public sealed class McpLogTests : IDisposable
{
    private readonly string _logPath;

    public McpLogTests()
    {
        _logPath = Path.Combine(Path.GetTempPath(), $"fusion360mcp-addintests-{Guid.NewGuid():N}.log");
        McpLog.Configure(_logPath);
    }

    public void Dispose()
    {
        McpLog.Configure(null);
        try
        {
            File.Delete(_logPath);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void Info_WritesTheReferenceLineFormat()
    {
        var log = McpLog.ByName("socket");
        log.Info("Server listening on {0}:{1}", "127.0.0.1", 9876);

        var content = File.ReadAllText(_logPath);
        var lines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        var last = lines[lines.Length - 1];

        Assert.Matches(
            @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} \[INFO\] fusion360mcp\.socket: Server listening on 127\.0\.0\.1:9876$",
            last);
    }

    [Fact]
    public void Rotation_KeepsTheThreeNewestBackups()
    {
        var log = McpLog.ByName("rot");
        var filler = new string('x', 200);

        // ~2 MB of lines forces a rotation (MaxBytes = 2 MB).
        for (int i = 0; i < 11000; i++)
        {
            log.Debug("{0}", filler);
        }

        Assert.True(File.Exists(_logPath), "the live log must exist after rotation");
        Assert.True(File.Exists(_logPath + ".1"), "the most recent backup must exist after rotation");
        Assert.True(new FileInfo(_logPath).Length < 2 * 1024 * 1024, "the live log is truncated below 2 MB after rotation");
        Assert.True(new FileInfo(_logPath + ".1").Length >= 2 * 1024 * 1024, "the newest backup held the rotated pre-rotation content");
    }
}
