namespace Fusion360.Mcp.Core.Protocol;

/// <summary>
/// Protocol constants shared by the MCP server client and the add-in
/// socket server. Port of the tuning values in reference
/// src/fusion360_mcp/connection.py (client side) and
/// addon/server/socket_server.py + event_bridge.py (server side).
/// All lengths are in the Fusion internal unit — centimetres.
/// </summary>
public static class ProtocolConstants
{
    // INVARIANT: ClientTimeout (45 s) > BridgeTimeout (30 s) so a structured TIMEOUT
    // arrives before the client gives up. History: docs/decisions/protocol.md#timeouts

    /// <summary>Default add-in host (override with FUSION_MCP_HOST).</summary>
    public const string DefaultHost = "localhost";

    /// <summary>Default add-in port (override with FUSION_MCP_PORT).</summary>
    public const int DefaultPort = 9876;

    /// <summary>Environment variable that overrides <see cref="DefaultHost"/>.</summary>
    public const string HostEnvVar = "FUSION_MCP_HOST";

    /// <summary>Explicit opt-in required before binding the add-in outside loopback.</summary>
    public const string AllowRemoteEnvVar = "FUSION_MCP_ALLOW_REMOTE";

    /// <summary>Environment variable that overrides <see cref="DefaultPort"/>.</summary>
    public const string PortEnvVar = "FUSION_MCP_PORT";

    /// <summary>
    /// Client command timeout in seconds. Must exceed the add-in's 30s
    /// bridge timeout so a structured TIMEOUT response arrives before the
    /// client gives up (reference connection.py: _TIMEOUT = 45.0).
    /// </summary>
    public const int ClientTimeoutSeconds = 45;

    /// <summary>Client command timeout as a TimeSpan (see <see cref="ClientTimeoutSeconds"/>).</summary>
    public static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(ClientTimeoutSeconds);

    /// <summary>Add-in bridge timeout for one command (event_bridge.submit).</summary>
    public static readonly TimeSpan BridgeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Short timeout (seconds) for connect + ping health checks.</summary>
    public const int PingTimeoutSeconds = 5;

    /// <summary>Short timeout as a TimeSpan (see <see cref="PingTimeoutSeconds"/>).</summary>
    public static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(PingTimeoutSeconds);

    /// <summary>Max retries for read-only commands after a failed send (mutations are never retried).</summary>
    public const int MaxRetries = 2;

    /// <summary>Delay between reconnect attempts.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Socket receive buffer size (both client and add-in use 64 KiB).</summary>
    public const int RecvBufferSize = 65536;

    /// <summary>Add-in accept-loop restart delay after a socket error.</summary>
    public static readonly TimeSpan ServerRestartDelay = TimeSpan.FromSeconds(2);

    /// <summary>Consecutive add-in restart cap before the accept loop gives up.</summary>
    public const int MaxServerRestarts = 10;

    /// <summary>EventBridge backup-timer interval.</summary>
    public const int TimerIntervalMs = 200;

    /// <summary>
    /// Note for a command cancelled before the main-thread handler starts.
    /// Running Fusion API calls are not interruptible and report an unknown outcome.
    /// </summary>
    public const string TimeoutCancellationNote =
        "The command was cancelled by the bridge and will not execute after the deadline; the model was not modified.";

    /// <summary>Resolves the host from <see cref="HostEnvVar"/>, falling back to <see cref="DefaultHost"/>.</summary>
    public static string ResolveHost()
        => Environment.GetEnvironmentVariable(HostEnvVar) ?? DefaultHost;

    /// <summary>Resolves the port from <see cref="PortEnvVar"/>, falling back to <see cref="DefaultPort"/>.</summary>
    public static int ResolvePort()
    {
        var raw = Environment.GetEnvironmentVariable(PortEnvVar);
        if (raw is not null && int.TryParse(raw, out var port))
        {
            return port;
        }

        return DefaultPort;
    }
}
