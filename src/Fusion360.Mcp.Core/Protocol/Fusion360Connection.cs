using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Mock;
using Fusion360.Mcp.Core.Catalog;

namespace Fusion360.Mcp.Core.Protocol;

/// <summary>
/// Persistent TCP connection to the Fusion 360 add-in socket server.
/// Port of reference src/fusion360_mcp/connection.py::Fusion360Connection
/// with the reference's retry semantics: read-only commands are retried
/// with a fresh socket; mutation commands are never retried after a send
/// was attempted (the add-in may have executed them already).
/// INVARIANT: mutations are never auto-retried. History: docs/decisions/protocol.md#retries
/// </summary>
public sealed class Fusion360Connection : IDisposable
{
    private Socket? _socket;
    private readonly object _lock = new();
    private readonly StringBuilder _receiveBuffer = new();

    public Fusion360Connection(string host = ProtocolConstants.DefaultHost, int port = ProtocolConstants.DefaultPort)
    {
        Host = host;
        Port = port;
    }

    /// <summary>Host of the add-in socket server.</summary>
    public string Host { get; }

    /// <summary>Port of the add-in socket server.</summary>
    public int Port { get; }

    /// <summary>True when a socket is open.</summary>
    public bool Connected
    {
        get
        {
            lock (_lock)
            {
                return _socket is not null;
            }
        }
    }

    /// <summary>Opens the socket with a short timeout; true when connected.</summary>
    public bool Connect()
    {
        lock (_lock)
        {
            if (_socket is not null)
            {
                return true;
            }

            try
            {
                // The reference does socket.settimeout(_PING_TIMEOUT) before
                // connect(): resolve the host (DNS name or IP literal), then
                // connect non-blocking with the ping-timeout deadline.
                var addresses = Dns.GetHostAddresses(Host);
                Socket? connected = null;
                foreach (var address in addresses)
                {
                    try
                    {
                        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
                        var task = socket.ConnectAsync(new IPEndPoint(address, Port));
                        if (task.Wait(ProtocolConstants.PingTimeout))
                        {
                            task.GetAwaiter().GetResult();
                            connected = socket;
                            break;
                        }

                        socket.Dispose();
                    }
                    catch
                    {
                        // One resolved address failed (e.g. 'localhost' resolves to
                        // both ::1 and 127.0.0.1, and the add-in only listens on
                        // 127.0.0.1). Keep trying the remaining addresses — the
                        // reference's socket.connect() walks the whole address list.
                    }
                }

                if (connected is null)
                {
                    return false;
                }

                connected.Blocking = true;
                _socket = connected;
                return true;
            }
            catch
            {
                _socket = null;
                return false;
            }
        }
    }

    /// <summary>Closes the socket (no-op when disconnected).</summary>
    public void Disconnect()
    {
        lock (_lock)
        {
            _socket?.Dispose();
            _socket = null;
            _receiveBuffer.Clear();
        }
    }

    /// <summary>Drops the existing socket and opens a fresh one.</summary>
    public bool Reconnect()
    {
        Disconnect();
        return Connect();
    }

    /// <summary>
    /// Sends a ping and returns true when the add-in responds. Uses a
    /// short timeout and no retries so it stays a cheap health check even
    /// when the Fusion main thread is wedged.
    /// </summary>
    public bool Ping()
    {
        try
        {
            SendCommand("ping", retries: 0, timeoutSeconds: ProtocolConstants.PingTimeoutSeconds);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Verifies the connection is alive (via ping), reconnects if not.</summary>
    public bool EnsureConnected()
    {
        if (Connected && Ping())
        {
            return true;
        }

        return Reconnect();
    }

    /// <summary>
    /// Sends a JSON command and blocks until a JSON response arrives.
    ///
    /// On connection failure, read-only commands are retried up to
    /// *retries* times with a fresh socket. Mutation commands are
    /// never retried after a send was attempted: the add-in may have
    /// queued or executed the command already, and re-sending could
    /// apply the change twice.
    ///
    /// <exception cref="FusionError">Structured add-in failure (kind + hints).</exception>
    /// <exception cref="ConnectionException">Lost connection / not connected.</exception>
    /// </summary>
    public CommandResult SendCommand(
        string commandType,
        JsonNode? parameters = null,
        int retries = ProtocolConstants.MaxRetries,
        int timeoutSeconds = ProtocolConstants.ClientTimeoutSeconds)
    {
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        if (!Connected && !Connect())
        {
            throw new ConnectionException("Not connected to Fusion 360.  Make sure the add-in is running.");
        }

        var envelope = CommandEnvelope.Create(commandType, parameters);
        var payload = Encoding.UTF8.GetBytes(ProtocolCodec.EncodeCommand(envelope) + "\n");

        var isMutation = ToolEffects.OperationCommands.Contains(commandType);
        try
        {
            ResponseEnvelope frame;
            string responseLine;
            lock (_lock)
            {
                // A persistent stream has one response owner at a time.
                // Protect the complete exchange, not only individual socket calls.
                SendAll(payload, timeout);
                responseLine = RecvJsonLine(timeout);
                frame = ProtocolCodec.ParseResponse(responseLine);
            }

            if (frame.IsError)
            {
                var message = frame.Message ?? "Unknown error";
                var kind = frame.ErrorKind;
                string[] hints = frame.Hints.Length > 0 ? frame.Hints : Array.Empty<string>();
                if (kind is null)
                {
                    var (classifiedKind, classifiedHints) = ErrorClassifier.Classify(message);
                    kind = classifiedKind;
                    if (hints.Length == 0)
                    {
                        hints = classifiedHints;
                    }
                }

                throw new FusionError(message, kind.Value, hints);
            }

            return frame.Result
                ?? throw new FormatException($"Success frame missing 'result': {responseLine}");
        }
        catch (SocketException ex)
        {
            Disconnect();
            if (isMutation)
            {
                // The socket closed without a structured bridge response.
                // Execution may have started, so the mutation outcome is unknown.
                throw new FusionError(
                    $"{commandType}: no response from Fusion ({ex.Message}). The command was NOT retried.",
                    ErrorKind.Timeout,
                    new[]
                    {
                        "Verify the scene with get_scene_info before re-issuing the operation.",
                        "Long operations (CAM toolpaths, complex fillets) exceed the add-in's 30 s " +
                        "bridge timeout — split them into smaller steps.",
                    },
                    innerException: ex,
                    outcomeUnknown: true);
            }

            if (retries > 0)
            {
                Thread.Sleep(ProtocolConstants.RetryDelay);
                if (Connect())
                {
                    return SendCommand(commandType, parameters, retries: retries - 1, timeoutSeconds: timeoutSeconds);
                }
            }

            throw new ConnectionException($"Lost connection to Fusion 360: {ex.Message}", ex);
        }
        catch (IOException ex)
        {
            Disconnect();
            if (isMutation)
            {
                // As with a socket exception, the command may have started.
                throw new FusionError(
                    $"{commandType}: no response from Fusion ({ex.Message}). The command was NOT retried.",
                    ErrorKind.Timeout,
                    new[]
                    {
                        "Verify the scene with get_scene_info before re-issuing the operation.",
                        "Long operations (CAM toolpaths, complex fillets) exceed the add-in's 30 s " +
                        "bridge timeout — split them into smaller steps.",
                    },
                    innerException: ex,
                    outcomeUnknown: true);
            }

            if (retries > 0)
            {
                Thread.Sleep(ProtocolConstants.RetryDelay);
                if (Connect())
                {
                    return SendCommand(commandType, parameters, retries: retries - 1, timeoutSeconds: timeoutSeconds);
                }
            }

            throw new ConnectionException($"Lost connection to Fusion 360: {ex.Message}", ex);
        }
    }

    private void SendAll(byte[] payload, TimeSpan timeout)
    {
        lock (_lock)
        {
            var socket = _socket ?? throw new ConnectionException("Not connected to Fusion 360.");
            socket.SendTimeout = (int)Math.Max(1, timeout.TotalMilliseconds);

            var offset = 0;
            while (offset < payload.Length)
            {
                var sent = socket.Send(payload, offset, payload.Length - offset, SocketFlags.None);
                offset += sent;
            }
        }
    }

    /// <summary>Reads a newline-delimited JSON line from the socket.</summary>
    private string RecvJsonLine(TimeSpan timeout)
    {
        var chunk = new byte[ProtocolConstants.RecvBufferSize];

        lock (_lock)
        {
            var socket = _socket ?? throw new ConnectionException("Not connected to Fusion 360.");
            socket.ReceiveTimeout = (int)Math.Max(1, timeout.TotalMilliseconds);

            while (true)
            {
                var pending = _receiveBuffer.ToString();
                var newline = pending.IndexOf('\n');
                if (newline >= 0)
                {
                    var line = pending.Substring(0, newline).Trim();
                    _receiveBuffer.Remove(0, newline + 1);
                    return line;
                }

                var read = socket.Receive(chunk);
                if (read == 0)
                {
                    throw new IOException("Connection closed by Fusion 360");
                }

                _receiveBuffer.Append(Encoding.UTF8.GetString(chunk, 0, read));
                if (_receiveBuffer.Length > 16 * 1024 * 1024)
                {
                    throw new FormatException("Fusion response exceeds the 16 MiB frame limit");
                }

                // Fallback: try to parse the whole buffer as JSON
                try
                {
                    _ = JsonDocument.Parse(_receiveBuffer.ToString());
                    var response = _receiveBuffer.ToString().Trim();
                    _receiveBuffer.Clear();
                    return response;
                }
                catch (JsonException)
                {
                    // Incomplete — wait for more data
                }
            }
        }
    }

    public void Dispose() => Disconnect();
}
