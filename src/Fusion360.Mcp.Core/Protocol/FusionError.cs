using Fusion360.Mcp.Core.Errors;

namespace Fusion360.Mcp.Core.Protocol;

/// <summary>
/// Structured error reported by the add-in (or a timed-out command).
/// Port of reference connection.py::FusionError.
/// </summary>
public sealed class FusionError : Exception
{
    /// <summary>Stable classification tag for the failure.</summary>
    public ErrorKind Kind { get; }

    /// <summary>Contextual repair hints (empty when none apply).</summary>
    public IReadOnlyList<string> Hints { get; }

    /// <summary>True when a mutation may have run but its response was lost.</summary>
    public bool OutcomeUnknown { get; }

    public FusionError(string message, ErrorKind kind = ErrorKind.Unknown, IReadOnlyList<string>? hints = null, Exception? innerException = null, bool outcomeUnknown = false)
        : base(message, innerException)
    {
        Kind = kind;
        Hints = hints ?? Array.Empty<string>();
        OutcomeUnknown = outcomeUnknown;
    }
}
