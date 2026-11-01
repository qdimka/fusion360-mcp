using Fusion360.Mcp.Core.Protocol;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Fusion360.Mcp.Server;

/// <summary>
/// Fusion360 MCP Server entry point. Port of the reference <c>main</c>
/// (server.py): a stdio MCP server that bridges an MCP client to the Fusion
/// 360 add-in. <c>--mode mock</c> answers from Core's mock data without a
/// running Fusion; <c>--mode socket</c> (the default) forwards to the add-in
/// over TCP. The MCP protocol itself runs over stdin/stdout, so all
/// diagnostics go to stderr.
///
/// P2 (ADR-0010): every backend is wrapped in an
/// <see cref="IdempotentToolBackend"/> backed by a durable
/// <see cref="OperationJournal"/> in a local catalog
/// (<c>FUSION_MCP_JOURNAL_DIR</c>, default <c>~/.fusion360-mcp</c>), so a
/// retried mutation is deduplicated instead of applied twice.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var (mode, host, port) = CliArguments.Parse(args);

        // P2 (ADR-0010): the durable operation journal lives in a local
        // catalog independent of Fusion; both modes dedup through it.
        var journalDirectory =
            Environment.GetEnvironmentVariable("FUSION_MCP_JOURNAL_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fusion360-mcp");
        var journal = new OperationJournal(journalDirectory);
        if (journal.PendingReconciliationCount > 0)
        {
            await Console.Error.WriteLineAsync(
                $"[journal] {journal.PendingReconciliationCount} operation(s) have no terminal record " +
                "(the last run was interrupted mid-mutation). Verify the scene with get_scene_info " +
                "and use a NEW operation_id for follow-up work.");
        }

        IToolBackend backend = mode switch
        {
            ServerMode.Mock => new IdempotentToolBackend(new MockToolBackend(), journal),
            _ => new IdempotentToolBackend(new SocketToolBackend(host, port), journal),
        };

        var server = new Fusion360McpServer(backend);
        var options = server.CreateOptions();
        var loggerFactory = new StderrLoggerFactory();

        var transport = new StdioServerTransport(options, loggerFactory);
        var mcpServer = McpServer.Create(transport, options, loggerFactory, new NoServices());

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, cancel) =>
        {
            cancel.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await mcpServer.RunAsync(cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Fusion360MCP server error: {ex}");
            return 1;
        }
    }
}

/// <summary>
/// Parses <c>--mode</c>, <c>--host</c>, and <c>--port</c> (both
/// <c>--key value</c> and <c>--key=value</c> forms). host/port fall back to
/// <c>FUSION_MCP_HOST</c>/<c>FUSION_MCP_PORT</c>, matching the reference's
/// click defaults.
/// </summary>
public static class CliArguments
{
    public static (ServerMode Mode, string Host, int Port) Parse(string[] args)
    {
        var mode = ServerMode.Socket;
        string? host = null;
        int? port = null;

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            string? value;
            var separator = key.IndexOf('=');
            if (separator >= 0)
            {
                value = key[(separator + 1)..];
                key = key[..separator];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }
            else
            {
                value = null;
            }

            switch (key)
            {
                case "--mode":
                    mode = string.Equals(value, "mock", StringComparison.OrdinalIgnoreCase)
                        ? ServerMode.Mock
                        : ServerMode.Socket;
                    break;
                case "--host":
                    host = value;
                    break;
                case "--port" when value is not null && int.TryParse(value, out var parsedPort):
                    port = parsedPort;
                    break;
            }
        }

        return (mode, host ?? ProtocolConstants.ResolveHost(), port ?? ProtocolConstants.ResolvePort());
    }
}

/// <summary>Writes all log events to stderr so stdout stays a clean JSON-RPC channel.</summary>
public sealed class StderrLoggerFactory : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    public ILogger CreateLogger(string categoryName) => new StderrLogger(categoryName);

    private sealed class StderrLogger : ILogger
    {
        private readonly string _category;

        public StderrLogger(string category) => _category = category;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            Console.Error.WriteLine(
                $"[{DateTime.Now:HH:mm:ss}] {logLevel} {_category}: {formatter(state, exception)}" +
                (exception is null ? string.Empty : $" {exception}"));
        }
    }
}

/// <summary>Minimal <see cref="IServiceProvider"/> — this server uses no DI-registered services.</summary>
public sealed class NoServices : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}
