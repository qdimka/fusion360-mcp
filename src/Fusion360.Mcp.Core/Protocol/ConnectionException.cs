using System.IO;

namespace Fusion360.Mcp.Core.Protocol;

/// <summary>
/// Transport-level failure: not connected, or the connection was lost
/// mid-command. Port of the reference connection.py's use of Python's
/// built-in ConnectionError/ConnectionResetError.
/// </summary>
public sealed class ConnectionException : IOException
{
    public ConnectionException(string message)
        : base(message)
    {
    }

    public ConnectionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
