using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Fusion360.Mcp.Server.Tests;

/// <summary>
/// Spawns the compiled Fusion360.Mcp.Server binary over stdio and drives it
/// with a real MCP client (the official SDK's <see cref="McpClient"/>), which is
/// the "MCP test client" the acceptance criteria require. The client performs
/// the JSON-RPC initialize handshake for us.
/// </summary>
public sealed class StdioMcpClient : IAsyncDisposable
{
    public McpClient Client { get; }

    private StdioMcpClient(McpClient client)
    {
        Client = client;
    }

    /// <summary>Absolute path to the compiled server assembly in the test output dir.</summary>
    public static string ServerAssemblyPath =>
        Path.Combine(AppContext.BaseDirectory, "Fusion360.Mcp.Server.dll");

    /// <summary>
    /// Launch the server with the given CLI args (e.g. ["--mode","mock"]) and
    /// return a connected MCP client.
    /// </summary>
    public static async Task<StdioMcpClient> StartAsync(string[] serverArgs, CancellationToken ct = default)
    {
        if (!File.Exists(ServerAssemblyPath))
        {
            throw new FileNotFoundException(
                "Fusion360.Mcp.Server.dll not found in test output dir. Build the Server project first.",
                ServerAssemblyPath);
        }

        var transportOptions = new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [ServerAssemblyPath, .. serverArgs],
            InheritEnvironmentVariables = true,
        };
        var transport = new StdioClientTransport(transportOptions, NullLoggerFactory.Instance);
        McpClient client = await McpClient.CreateAsync(transport, null, NullLoggerFactory.Instance, ct);
        return new StdioMcpClient(client);
    }

    public async ValueTask DisposeAsync()
    {
        // Disposing the client closes the stdio connection; the server sees
        // EOF on stdin and exits, taking its spawned process with it.
        await Client.DisposeAsync();
    }

    // Convenient thin wrappers so test bodies read cleanly.

    public ValueTask<ListToolsResult> ListToolsAsync(CancellationToken ct = default) =>
        Client.ListToolsAsync(new ListToolsRequestParams(), ct);

    public ValueTask<ListResourcesResult> ListResourcesAsync(CancellationToken ct = default) =>
        Client.ListResourcesAsync(new ListResourcesRequestParams(), ct);

    public ValueTask<ListResourceTemplatesResult> ListResourceTemplatesAsync(CancellationToken ct = default) =>
        Client.ListResourceTemplatesAsync(new ListResourceTemplatesRequestParams(), ct);

    public ValueTask<ListPromptsResult> ListPromptsAsync(CancellationToken ct = default) =>
        Client.ListPromptsAsync(new ListPromptsRequestParams(), ct);

    public ValueTask<CallToolResult> CallToolAsync(string name, IReadOnlyDictionary<string, object?>? args, CancellationToken ct = default)
    {
        var wire = new Dictionary<string, JsonElement>();
        if (args is not null)
        {
            foreach (var kv in args)
            {
                wire[kv.Key] = JsonSerializer.SerializeToElement(kv.Value);
            }
        }
        return Client.CallToolAsync(new CallToolRequestParams { Name = name, Arguments = wire }, ct);
    }

    public ValueTask<GetPromptResult> GetPromptAsync(string name, IReadOnlyDictionary<string, object?>? args, CancellationToken ct = default) =>
        Client.GetPromptAsync(name, args ?? new Dictionary<string, object?>(), null, ct);

    public ValueTask<ReadResourceResult> ReadResourceAsync(string uri, CancellationToken ct = default) =>
        Client.ReadResourceAsync(new ReadResourceRequestParams { Uri = uri }, ct);
}
