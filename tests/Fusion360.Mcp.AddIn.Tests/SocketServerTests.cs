using System.Net;
using System.Net.Sockets;
using System.Text;
using Fusion360.Mcp.AddIn;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Protocol;
using Xunit;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// End-to-end socket tests against the real <see cref="Fusion360MCPServer"/>:
/// the wire format (newline-delimited JSON + raw blob fallback), invalid
/// JSON, accept-loop rebind, and stop semantics. The bridge runs on a
/// <see cref="SyncDispatcher"/> so no Fusion main thread is involved.
/// </summary>
public sealed class SocketServerTests : IDisposable
{
    private readonly string _logPath;
    private readonly McpLogger _log;

    public SocketServerTests()
    {
        _logPath = Path.Combine(Path.GetTempPath(), $"fusion360mcp-addintests-{Guid.NewGuid():N}.log");
        McpLog.Configure(_logPath);
        _log = McpLog.ByName("socket-test");
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
    public void RemoteBind_RequiresExplicitOptIn()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Fusion360MCPServer.ValidateRemoteBind("0.0.0.0", allowRemote: false));
        Fusion360MCPServer.ValidateRemoteBind("0.0.0.0", allowRemote: true);
        Fusion360MCPServer.ValidateRemoteBind("127.0.0.1", allowRemote: false);
        Assert.False(Fusion360MCPServer.IsNonLoopbackBind("localhost"));
    }

    /// <summary>Starts the server on <paramref name="port"/>, runs <paramref name="body"/>, always tears down.</summary>
    private static void WithServer(int port, Action<Fusion360MCPServer, int> body, IAddInCommandHandler? handler = null)
    {
        var dispatcher = new SyncDispatcher();
        var handlerInstance = handler ?? new StubHandler();
        var bridge = new EventBridge(dispatcher, handlerInstance, McpLog.ByName("socket-test"));
        var server = new Fusion360MCPServer(bridge, "127.0.0.1", port, McpLog.ByName("socket-test"));
        server.Start();

        try
        {
            var boundPort = WaitForPort(server);
            body(server, boundPort);
        }
        finally
        {
            server.Stop();
            bridge.Stop();
        }
    }

    private static int WaitForPort(Fusion360MCPServer server, int ms = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (server.BoundPort == 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }

        Assert.True(server.BoundPort > 0, "server did not bind within the timeout");
        return server.BoundPort;
    }

    private static string WriteCommand(int port, string json, bool trailingNewline = true)
    {
        using var client = new TcpClient();
        client.Connect("127.0.0.1", port);
        client.ReceiveTimeout = 10000; // a stalled response must fail the test, not hang the suite
        var stream = client.GetStream();
        var payload = Encoding.UTF8.GetBytes(json + (trailingNewline ? "\n" : string.Empty));
        stream.Write(payload, 0, payload.Length);
        return ReadResponseLine(client);
    }

    /// <summary>Reads one newline-terminated response frame.</summary>
    private static string ReadResponseLine(TcpClient client)
    {
        var stream = client.GetStream();
        var sb = new StringBuilder();
        var one = new byte[1];
        int b;
        while ((b = stream.Read(one, 0, 1)) > 0 && (char)one[0] != '\n')
        {
            sb.Append((char)one[0]);
        }

        return sb.ToString();
    }

    [Fact]
    public void Ping_OverSocket_ReturnsPong()
    {
        WithServer(0, (_, port) =>
        {
            var line = WriteCommand(port, """{"type":"ping","params":{}}""");
            var frame = ProtocolCodec.ParseResponse(line);

            Assert.True(frame.IsSuccess);
            Assert.True(frame.Result!.Ok);
            Assert.Equal("pong", frame.Result.Field("status")!.GetValue<string>());
        });
    }

    [Fact]
    public void UnknownCommand_OverSocket_ReturnsTheLegacyErrorEnvelope()
    {
        // The real CommandHandler dispatch table: unknown commands must be
        // answered by the handler (not a stub) with the legacy error frame.
        WithServer(0, (_, port) =>
        {
            var line = WriteCommand(port, """{"type":"fly_to_moon","params":{}}""");
            var frame = ProtocolCodec.ParseResponse(line);

            Assert.True(frame.IsError);
            Assert.Equal("Unknown command: fly_to_moon", frame.Message);
            Assert.Equal(ErrorKind.UnknownCommand, frame.ErrorKind);
        }, new CommandHandler());
    }

    [Fact]
    public void ExecuteCode_OverSocket_IsRejectedBeforeExecution()
    {
        WithServer(0, (_, port) =>
        {
            var line = WriteCommand(port, """{"type":"execute_code","params":{"code":"throw new Exception()"}}""");
            var frame = ProtocolCodec.ParseResponse(line);

            Assert.True(frame.IsError);
            Assert.Equal("Unknown command: execute_code", frame.Message);
            Assert.Equal(ErrorKind.UnknownCommand, frame.ErrorKind);
        }, new CommandHandler());
    }

    [Fact]
    public void InvalidJsonLine_ReturnsTheInvalidJsonError()
    {
        WithServer(0, (_, port) =>
        {
            var line = WriteCommand(port, """{"type": """);
            var frame = ProtocolCodec.ParseResponse(line);

            Assert.True(frame.IsError);
            Assert.Equal("Invalid JSON", frame.Message);
        });
    }

    [Fact]
    public void RawJsonBlobWithoutNewline_IsDispatchedByTheFallbackPath()
    {
        WithServer(0, (_, port) =>
        {
            var line = WriteCommand(port, """{"type":"ping"}""", trailingNewline: false);
            var frame = ProtocolCodec.ParseResponse(line);

            Assert.True(frame.IsSuccess);
            Assert.Equal("pong", frame.Result!.Field("status")!.GetValue<string>());
        });
    }

    [Fact]
    public void AcceptLoop_RebindsWhenThePortIsReleased()
    {
        // Occupy the port so the server's first bind attempt fails.
        var blocker = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        blocker.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)blocker.LocalEndPoint!).Port; // net48: LocalEndPoint

        var dispatcher = new SyncDispatcher();
        var bridge = new EventBridge(dispatcher, new StubHandler(), _log);
        var server = new Fusion360MCPServer(bridge, "127.0.0.1", port, _log);

        try
        {
            server.Start();
            Thread.Sleep(1000); // the first bind has failed (port held) — the 2 s retry cycle is running

            // Release the port: the retry cycle rebinds within its 2 s cadence.
            blocker.Close();

            string line = string.Empty;
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    line = WriteCommand(port, """{"type":"ping","params":{}}""");
                    break;
                }
                catch (SocketException)
                {
                    Thread.Sleep(500);
                }
            }

            var frame = ProtocolCodec.ParseResponse(line);
            Assert.True(frame.IsSuccess);
            Assert.Equal("pong", frame.Result!.Field("status")!.GetValue<string>());
        }
        finally
        {
            blocker.Close();
            server.Stop();
            bridge.Stop();
        }
    }

    [Fact]
    public void Stop_ClosesTheListeningSocket()
    {
        var dispatcher = new SyncDispatcher();
        var bridge = new EventBridge(dispatcher, new StubHandler(), _log);
        var server = new Fusion360MCPServer(bridge, "127.0.0.1", 0, _log);
        server.Start();

        try
        {
            var port = WaitForPort(server);

            // The listener is up.
            using (var alive = new TcpClient())
            {
                alive.Connect("127.0.0.1", port);
            }

            server.Stop();

            // The listener is gone — connecting must now fail.
            var ex = Record.Exception(() =>
            {
                using var dead = new TcpClient();
                dead.Connect("127.0.0.1", port);
            });

            Assert.NotNull(ex);
        }
        finally
        {
            bridge.Stop();
        }
    }
}
