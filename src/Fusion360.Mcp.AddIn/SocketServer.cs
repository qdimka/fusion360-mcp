using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// TCP socket server on FUSION_MCP_HOST (default localhost) :
/// FUSION_MCP_PORT (default 9876). Each client gets its own background
/// thread; all Fusion API work is dispatched through the
/// <see cref="EventBridge"/> so it runs on the main thread. The accept
/// loop auto-restarts on transient socket errors (2 s delay, cap of 10
/// consecutive bind failures) so the bridge stays alive even if the
/// listening socket dies. C# port of reference addon/server/socket_server.py.
/// </summary>
public sealed class Fusion360MCPServer
{
    private const double RestartDelaySeconds = 2.0;
    private const int MaxRestarts = 10;
    private const int Backlog = 5;
    private const int ReceiveChunkBytes = 65536;

    private readonly EventBridge _bridge;
    private readonly string _host;
    private readonly int _port;
    private readonly McpLogger _log;
    private readonly ConcurrentDictionary<Socket, byte> _clients = new();
    private readonly object _listenerGate = new();
    private Socket? _listenSocket;
    private Thread? _acceptThread;
    private volatile bool _running;

    public Fusion360MCPServer(EventBridge bridge, string host, int port, McpLogger log)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>The port actually bound (the real port when the server was created with port 0).</summary>
    public int BoundPort { get; private set; }

    /// <summary>True while the accept loop is running.</summary>
    public bool IsRunning => _running;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    /// <summary>Starts the accept loop on a background thread.</summary>
    public void Start()
    {
        if (_running)
        {
            return;
        }

        var allowRemote = Environment.GetEnvironmentVariable(ProtocolConstants.AllowRemoteEnvVar) == "1";
        ValidateRemoteBind(_host, allowRemote);

        _running = true;
        _acceptThread = new Thread(AcceptLoopWithRestart)
        {
            IsBackground = true,
            Name = "mcp-socket-accept",
        };
        _acceptThread.Start();
    }

    /// <summary>Stops the server and closes all client sockets (reference stop()).</summary>
    public void Stop()
    {
        _running = false;
        CloseListener();

        // Close accepted client sockets so per-client threads exit
        // instead of blocking on recv() against a stopped server.
        foreach (var client in _clients.Keys.ToArray())
        {
            try
            {
                client.Close();
            }
            catch
            {
                // Already gone.
            }

            _clients.TryRemove(client, out _);
        }

        if (_acceptThread is { IsAlive: true })
        {
            _acceptThread.Join(TimeSpan.FromSeconds(2));
        }

        _acceptThread = null;
        _log.Info("Server stopped");
    }

    // ------------------------------------------------------------------
    // Socket helpers
    // ------------------------------------------------------------------

    private static IPAddress ResolveAddress(string host)
    {
        if (IPAddress.TryParse(host, out var parsed))
        {
            return parsed;
        }

        var addresses = Dns.GetHostAddresses(host);
        var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        return ipv4 ?? addresses[0];
    }

    public static void ValidateRemoteBind(string host, bool allowRemote)
    {
        if (IsNonLoopbackBind(host) && !allowRemote)
        {
            throw new InvalidOperationException(
                $"Non-loopback Fusion MCP binding '{host}' requires " +
                $"{ProtocolConstants.AllowRemoteEnvVar}=1. The TCP protocol has no authentication.");
        }
    }

    public static bool IsNonLoopbackBind(string host)
        => !IPAddress.IsLoopback(ResolveAddress(host));

    /// <summary>Creates, binds, and listens. Returns true on success (reference _bind_socket).</summary>
    private bool BindListener()
    {
        CloseListener();
        try
        {
            // .NET 4.8's TcpListener has no ReuseAddress property and no
            // Socket-accepting constructor, so drive the listening socket
            // directly (reference socket_server sets SO_REUSEADDR on a raw
            // AF_INET socket).
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, 1);
            socket.Bind(new IPEndPoint(ResolveAddress(_host), _port));
            socket.Listen(Backlog);

            lock (_listenerGate)
            {
                _listenSocket = socket;
            }

            BoundPort = ((IPEndPoint)socket.LocalEndPoint).Port; // net48: LocalEndPoint (lowercase 'p')
            _log.Info("Server listening on {0}:{1}", _host, BoundPort);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("Bind failed: {0}", ex.Message);
            CloseListener();
            return false;
        }
    }

    private void CloseListener()
    {
        Socket? socket;
        lock (_listenerGate)
        {
            socket = _listenSocket;
            _listenSocket = null;
        }

        if (socket is not null)
        {
            try
            {
                socket.Close();
            }
            catch
            {
                // Already closed.
            }
        }
    }

    // ------------------------------------------------------------------
    // Accept loop with automatic restart
    // ------------------------------------------------------------------

    /// <summary>Outer loop: rebinds the socket up to <see cref="MaxRestarts"/> consecutive failures.</summary>
    private void AcceptLoopWithRestart()
    {
        int restarts = 0;

        while (_running)
        {
            if (!BindListener())
            {
                restarts++;
                if (restarts > MaxRestarts)
                {
                    _log.Error("Exceeded {0} restart attempts — giving up", MaxRestarts);
                    break;
                }

                _log.Warning("Retrying bind in {0:0.0}s (attempt {1}/{2})", RestartDelaySeconds, restarts, MaxRestarts);
                Thread.Sleep(TimeSpan.FromSeconds(RestartDelaySeconds));
                continue;
            }

            // Reset the counter on a successful bind.
            restarts = 0;

            try
            {
                AcceptLoop();
            }
            catch (Exception ex)
            {
                if (_running)
                {
                    _log.Error("Accept loop crashed:\n{0}", ex.ToString());
                }
            }

            // If we get here and are still running, the socket died — restart.
            if (_running)
            {
                _log.Warning("Socket lost — restarting in {0:0.0}s", RestartDelaySeconds);
                CloseListener();
                Thread.Sleep(TimeSpan.FromSeconds(RestartDelaySeconds));
            }
        }
    }

    /// <summary>Inner loop: accepts clients until the socket errors out (reference _accept_loop).</summary>
    private void AcceptLoop()
    {
        while (_running)
        {
            var listenSocket = _listenSocket ?? throw new ObjectDisposedException("listen socket");

            Socket clientSocket;
            try
            {
                clientSocket = listenSocket.Accept(); // blocks; unblocked when Stop() closes the socket
            }
            catch (Exception)
            {
                if (!_running)
                {
                    break; // Stop() closed the socket — exit without restart
                }

                throw; // socket died — the outer loop restarts it
            }

            var client = clientSocket;
            _log.Info("Client connected: {0}", client.RemoteEndPoint);
            _clients.TryAdd(client, 0);

            var thread = new Thread(() => HandleClient(client))
            {
                IsBackground = true,
                Name = "mcp-socket-client",
            };
            thread.Start();
        }
    }

    // ------------------------------------------------------------------
    // Per-client handler (background thread)
    // ------------------------------------------------------------------

    private void HandleClient(Socket client)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[ReceiveChunkBytes];

        try
        {
            while (_running)
            {
                int read = client.Receive(chunk, SocketFlags.None);
                if (read == 0)
                {
                    break; // client closed the connection
                }

                buffer.Write(chunk, 0, read);

                // Newline-delimited JSON messages.
                ConsumeNewlineDelimitedLines(client, buffer);

                // Fallback: try a raw JSON blob (no newline framing).
                TryDispatchRawBlob(client, buffer);
            }
        }
        catch (Exception ex)
        {
            if (_running)
            {
                _log.Debug("Client handler error:\n{0}", ex.ToString());
            }
        }
        finally
        {
            _clients.TryRemove(client, out _);
            try
            {
                client.Close();
            }
            catch
            {
                // Already closed.
            }

            _log.Info("Client disconnected");
        }
    }

    /// <summary>
    /// Drains every complete newline-terminated line out of <paramref name="buffer"/>,
    /// dispatching each; the buffer is compacted down to the trailing partial
    /// data (reference inner while loop).
    /// </summary>
    private void ConsumeNewlineDelimitedLines(Socket client, MemoryStream buffer)
    {
        while (true)
        {
            var data = buffer.ToArray();
            int newline = Array.IndexOf(data, (byte)'\n');
            if (newline < 0)
            {
                return; // no complete line left — wait for more data
            }

            string line = Encoding.UTF8.GetString(data, 0, newline).Trim();
            int restStart = newline + 1;
            buffer.SetLength(0);
            if (restStart < data.Length)
            {
                buffer.Write(data, restStart, data.Length - restStart);
            }

            if (line.Length == 0)
            {
                continue; // blank line
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                SendLine(client, ProtocolCodec.EncodeError("Invalid JSON"));
                continue;
            }

            if (node is not JsonObject command)
            {
                SendLine(client, ProtocolCodec.EncodeError("Invalid JSON"));
                continue;
            }

            Dispatch(client, command);
        }
    }

    /// <summary>
    /// When the leftover buffer (no newline yet) is a complete JSON blob,
    /// dispatch it immediately; otherwise keep it and wait for more data
    /// (reference raw-JSON fallback). The buffer only ever holds the
    /// trailing partial data, so nothing is lost.
    /// </summary>
    private void TryDispatchRawBlob(Socket client, MemoryStream buffer)
    {
        var remaining = buffer.ToArray();
        if (remaining.Length == 0)
        {
            return;
        }

        string stripped = Encoding.UTF8.GetString(remaining).Trim();
        if (stripped.Length == 0)
        {
            buffer.SetLength(0);
            return;
        }

        try
        {
            var node = JsonNode.Parse(stripped) as JsonObject;
            buffer.SetLength(0);
            if (node is not null)
            {
                Dispatch(client, node);
            }
        }
        catch (JsonException)
        {
            // Incomplete — the buffer already holds the bytes; wait for more.
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Submits the command to the bridge and sends the response back to the client (reference _dispatch).</summary>
    private void Dispatch(Socket client, JsonObject command)
    {
        string response;
        try
        {
            response = _bridge.Submit(command, 30.0).ToJson();
        }
        catch (Exception ex)
        {
            response = ProtocolCodec.EncodeError(ex.Message);
        }

        SendLine(client, response);
    }

    private void SendLine(Socket client, string payload)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(payload + "\n");
            client.Send(bytes, SocketFlags.None);
        }
        catch (Exception ex)
        {
            _log.Warning("Failed to send response to client: {0}", ex.Message);
        }
    }
}
