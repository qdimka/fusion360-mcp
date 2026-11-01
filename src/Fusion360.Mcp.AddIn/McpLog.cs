using System;
using System.IO;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// Add-in logging. Port of the reference addon/server/__init__.py logger:
/// a file log at ~/fusion360mcp.log (2 MB rotation, 3 backups,
/// "yyyy-MM-dd HH:mm:ss [LEVEL] fusion360mcp.&lt;category&gt;: message")
/// plus a plain-text echo to the Fusion TEXT COMMANDS window
/// ("[MCP] message", INFO and above).
/// </summary>
public static class McpLog
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private const int BackupCount = 3;

    private static readonly object Gate = new();
    private static string? _pathOverride;

    /// <summary>Default log location: ~/fusion360mcp.log (reference LOG_PATH).</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "fusion360mcp.log");

    /// <summary>Active log file path (test override or the default).</summary>
    public static string FilePath
    {
        get
        {
            lock (Gate)
            {
                return _pathOverride ?? DefaultPath;
            }
        }
    }

    /// <summary>Points logging at a custom path, or back to the default (null).</summary>
    public static void Configure(string? path)
    {
        lock (Gate)
        {
            _pathOverride = path;
        }
    }

    /// <summary>Child logger "fusion360mcp.&lt;category&gt;" (reference get_logger: "main", "socket", "bridge").</summary>
    public static McpLogger ByName(string category) => new(category);

    internal static void Write(string name, string level, string message, bool echoToConsole)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {name}: {message}";
        lock (Gate)
        {
            string path = _pathOverride ?? DefaultPath;
            RotateIfNeeded(path);
            File.AppendAllText(path, line + Environment.NewLine);
            if (echoToConsole)
            {
                Console.WriteLine("[MCP] " + message);
            }
        }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxBytes)
        {
            return;
        }

        // log.3 -> gone, log.2 -> log.3, log.1 -> log.2, log -> log.1
        for (int i = BackupCount; i >= 1; i--)
        {
            string from = i == 1 ? path : path + "." + (i - 1);
            if (!File.Exists(from))
            {
                continue;
            }

            string to = path + "." + i;
            if (File.Exists(to))
            {
                File.Delete(to);
            }

            File.Move(from, to);
        }
    }
}

/// <summary>One named logger — a category child of the "fusion360mcp" root logger.</summary>
public sealed class McpLogger
{
    private readonly string _name;

    public McpLogger(string category) => _name = "fusion360mcp." + category;

    /// <summary>Writes an INFO line to the log and echoes it to TEXT COMMANDS.</summary>
    public void Info(string format, params object?[] args) =>
        McpLog.Write(_name, "INFO", Format(format, args), echoToConsole: true);

    /// <summary>Writes a WARNING line to the log and echoes it to TEXT COMMANDS.</summary>
    public void Warning(string format, params object?[] args) =>
        McpLog.Write(_name, "WARNING", Format(format, args), echoToConsole: true);

    /// <summary>Writes an ERROR line to the log and echoes it to TEXT COMMANDS.</summary>
    public void Error(string format, params object?[] args) =>
        McpLog.Write(_name, "ERROR", Format(format, args), echoToConsole: true);

    /// <summary>Writes a DEBUG line to the log file only.</summary>
    public void Debug(string format, params object?[] args) =>
        McpLog.Write(_name, "DEBUG", Format(format, args), echoToConsole: false);

    private static string Format(string format, object?[] args)
        => args.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
}
