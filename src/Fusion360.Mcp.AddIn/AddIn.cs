using System;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// Fusion 360 add-in entry point — the adsk.core.AddIn implementation that
/// Fusion instantiates (see Fusion360MCP.manifest). C# port of the
/// reference addon/Fusion360MCP.py run()/stop(): starts the socket server
/// and the main-thread EventBridge, streams status to TEXT COMMANDS.
/// </summary>
public sealed class Fusion360MCPAddIn : adsk.core.AddIn
{
    // Reference module-level globals (prevent GC of the handler/server refs).
    private static Fusion360MCPAddIn? _instance;

    private CommandHandler? _handler;
    private EventBridge? _bridge;
    private Fusion360MCPServer? _server;
    private McpLogger? _log;

    public void Run()
    {
        try
        {
            var app = adsk.core.Application.get();
            _log = McpLog.ByName("main");

            // Non-loopback hosts additionally require FUSION_MCP_ALLOW_REMOTE=1.
            string host = Environment.GetEnvironmentVariable(ProtocolConstants.HostEnvVar) ?? ProtocolConstants.DefaultHost;
            int port = int.TryParse(Environment.GetEnvironmentVariable(ProtocolConstants.PortEnvVar), out int parsed)
                ? parsed
                : ProtocolConstants.DefaultPort;

            _handler = new CommandHandler(new AdskFusionContext(app));
            _bridge = new EventBridge(new AdskMainThreadDispatcher(app), _handler, _log);
            _server = new Fusion360MCPServer(_bridge, host, port, _log);
            _server.Start();
            _instance = this;

            _log.Info("Fusion360MCP loaded - server on {0}:{1}  (log: {2})", host, port, McpLog.FilePath);
            if (Fusion360MCPServer.IsNonLoopbackBind(host))
            {
                _log.Warning("Listening on non-loopback host {0}; the TCP protocol has no authentication.", host);
            }
        }
        catch (Exception ex)
        {
            // Reference fallback: messageBox + stdout so the failure is
            // visible even when the UI object graph failed to start.
            // (Console output in a .NET add-in may not reach the TEXT
            // COMMANDS window — verified against the real Fusion in T8.)
            string error = "Fusion360MCP failed to start:\n" + ex;
            try
            {
                adsk.core.Application.get().UserInterface.MessageBox("Fusion360MCP", error);
            }
            catch
            {
                // UI unavailable — fall through to the console echo.
            }

            Console.WriteLine("Fusion360MCP startup error:\n" + ex);
        }
    }

    public void Stop()
    {
        try
        {
            _server?.Stop();
            _bridge?.Stop();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }

        _log?.Info("Fusion360MCP stopped");
        _server = null;
        _bridge = null;
        _handler = null;
        _instance = null;
    }
}
