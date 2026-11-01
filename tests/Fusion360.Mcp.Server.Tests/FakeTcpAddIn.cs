using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.Server.Tests;

/// <summary>
/// A loopback TCP server that speaks the add-in wire protocol (newline-delimited
/// JSON envelopes). It stands in for the Fusion 360 add-in so socket-mode
/// tests don't need the real application. It honours a small set of command
/// handlers and can emit structured error frames.
/// </summary>
public sealed class FakeTcpAddIn : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<TcpClient> _clients = new();
    private Task? _acceptLoop;
    private int _disposed;

    /// <summary>Default success handler for commands not in <see cref="SuccessHandlers"/> / <see cref="Errors"/>.</summary>
    public Func<JsonObject?, JsonObject> DefaultSuccessHandler { get; set; } =
        _ => new JsonObject { ["ok"] = true };

    /// <summary>Per-command success handlers (params → result fields).</summary>
    public Dictionary<string, Func<JsonObject?, JsonObject>> SuccessHandlers { get; } = new();

    /// <summary>Commands that must return a structured error frame.</summary>
    public Dictionary<string, (string Message, ErrorKind Kind, string[]? Hints)> Errors { get; } = new();

    public int Port { get; }

    /// <summary>Number of command lines received so far.</summary>
    public int CommandCount { get; private set; }

    /// <summary>The most recent command name received.</summary>
    public string? LastCommand { get; private set; }

    public FakeTcpAddIn()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            lock (_gate) { _clients.Add(client); }
            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            while (!_cts.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(_cts.Token).ConfigureAwait(false);
                if (line is null) { break; } // EOF / disconnected

                CommandEnvelope cmd;
                try
                {
                    cmd = ProtocolCodec.ParseCommand(line);
                }
                catch (Exception)
                {
                    await writer.WriteLineAsync("{}").ConfigureAwait(false);
                    continue;
                }

                CommandCount++;
                LastCommand = cmd.Command;

                if (Errors.TryGetValue(cmd.Command, out var err))
                {
                    var errorLine = ProtocolCodec.EncodeError(err.Message, err.Kind, err.Hints);
                    await writer.WriteLineAsync(errorLine).ConfigureAwait(false);
                }
                else if (SuccessHandlers.TryGetValue(cmd.Command, out var handler))
                {
                    var fields = handler(cmd.Params is JsonObject po ? po : new JsonObject());
                    var okLine = ProtocolCodec.EncodeSuccess(CommandResult.Success(fields));
                    await writer.WriteLineAsync(okLine).ConfigureAwait(false);
                }
                else
                {
                    var fields = DefaultSuccessHandler(cmd.Params is JsonObject po2 ? po2 : new JsonObject());
                    var okLine = ProtocolCodec.EncodeSuccess(CommandResult.Success(fields));
                    await writer.WriteLineAsync(okLine).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (IOException)
        {
            // socket closed
        }
        finally
        {
            client.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        // Idempotent: tests may dispose explicitly and again via 'await using'.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _cts.Cancel();
        lock (_gate)
        {
            foreach (var c in _clients) { c.Dispose(); }
        }
        _listener.Stop();
        _acceptLoop?.Wait();
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
