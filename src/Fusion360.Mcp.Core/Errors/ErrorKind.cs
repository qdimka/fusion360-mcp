namespace Fusion360.Mcp.Core.Errors;

/// <summary>
/// Stable, machine-readable error tags reported by the add-in.
/// The wire form is the SCREAMING_SNAKE string (e.g. "PROFILE_NOT_CLOSED");
/// see <see cref="ToWireString"/> / <see cref="FromWireString"/>.
/// The 11 reference error kinds in src/fusion360_mcp/hints.py plus the
/// C#-side additions: OperationIdConflict (P2, ADR-0010) and
/// RevisionConflict / StaleReference (P3, ADR-0011);
/// UNKNOWN is the catch-all fallback.
/// INVARIANT: the SCREAMING_SNAKE wire strings are a stable public contract
/// (TOOL_CONTRACTS §2); rename only via a documented migration.
/// History: docs/decisions/errors.md#catalog
/// </summary>
public enum ErrorKind
{
    /// <summary>The sketch has no closed profile to extrude/revolve.</summary>
    ProfileNotClosed,

    /// <summary>The named sketch does not exist.</summary>
    SketchNotFound,

    /// <summary>The named body does not exist.</summary>
    BodyNotFound,

    /// <summary>The resulting geometry self-intersects.</summary>
    SelfIntersection,

    /// <summary>Fusion could not regenerate the feature.</summary>
    RegenFailed,

    /// <summary>Boolean operation produced no change.</summary>
    BooleanNoOp,

    /// <summary>Fusion rejected one of the parameters.</summary>
    InvalidInput,

    /// <summary>No design is open/active in Fusion.</summary>
    NoActiveDesign,

    /// <summary>The operation is not permitted in the current design type.</summary>
    DesignTypeMismatch,

    /// <summary>The operation exceeded the bridge timeout.</summary>
    Timeout,

    /// <summary>The add-in does not know this command.</summary>
    UnknownCommand,

    /// <summary>
    /// A retry reused an <c>operation_id</c> that is already bound to a
    /// different operation (different command or arguments), or whose first
    /// execution cannot be safely deduplicated (unknown outcome or partial
    /// effects) — raised by the server's operation journal, not the add-in
    /// (ADR-0010).
    /// </summary>
    OperationIdConflict,

    /// <summary>
    /// The caller's <c>expected_revision</c> does not match the current
    /// document revision — the operation was rejected before touching
    /// the model (ADR-0011).
    /// </summary>
    RevisionConflict,

    /// <summary>
    /// A name the scene reported in a previous read no longer resolves —
    /// the object may have been deleted or renamed since the caller last
    /// read the scene (ADR-0011).
    /// </summary>
    StaleReference,

    /// <summary>Catch-all when no classifier rule matches.</summary>
    Unknown,
}

public static partial class ErrorKindExtensions
{
    /// <summary>Converts to the wire form ("PROFILE_NOT_CLOSED").</summary>
    public static string ToWireString(this ErrorKind kind)
    {
        if (kind is ErrorKind.ProfileNotClosed) return "PROFILE_NOT_CLOSED";
        if (kind is ErrorKind.SketchNotFound) return "SKETCH_NOT_FOUND";
        if (kind is ErrorKind.BodyNotFound) return "BODY_NOT_FOUND";
        if (kind is ErrorKind.SelfIntersection) return "SELF_INTERSECTION";
        if (kind is ErrorKind.RegenFailed) return "REGEN_FAILED";
        if (kind is ErrorKind.BooleanNoOp) return "BOOLEAN_NO_OP";
        if (kind is ErrorKind.InvalidInput) return "INVALID_INPUT";
        if (kind is ErrorKind.NoActiveDesign) return "NO_ACTIVE_DESIGN";
        if (kind is ErrorKind.DesignTypeMismatch) return "DESIGN_TYPE_MISMATCH";
        if (kind is ErrorKind.Timeout) return "TIMEOUT";
        if (kind is ErrorKind.UnknownCommand) return "UNKNOWN_COMMAND";
        if (kind is ErrorKind.OperationIdConflict) return "OPERATION_ID_CONFLICT";
        if (kind is ErrorKind.RevisionConflict) return "REVISION_CONFLICT";
        if (kind is ErrorKind.StaleReference) return "STALE_REFERENCE";
        return "UNKNOWN";
    }

    /// <summary>Parses the wire form; unknown strings map to <see cref="ErrorKind.Unknown"/>.</summary>
    public static ErrorKind FromWireString(string? wire)
    {
        switch ((wire ?? string.Empty).ToUpperInvariant())
        {
            case "PROFILE_NOT_CLOSED": return ErrorKind.ProfileNotClosed;
            case "SKETCH_NOT_FOUND": return ErrorKind.SketchNotFound;
            case "BODY_NOT_FOUND": return ErrorKind.BodyNotFound;
            case "SELF_INTERSECTION": return ErrorKind.SelfIntersection;
            case "REGEN_FAILED": return ErrorKind.RegenFailed;
            case "BOOLEAN_NO_OP": return ErrorKind.BooleanNoOp;
            case "INVALID_INPUT": return ErrorKind.InvalidInput;
            case "NO_ACTIVE_DESIGN": return ErrorKind.NoActiveDesign;
            case "DESIGN_TYPE_MISMATCH": return ErrorKind.DesignTypeMismatch;
            case "TIMEOUT": return ErrorKind.Timeout;
            case "UNKNOWN_COMMAND": return ErrorKind.UnknownCommand;
            case "OPERATION_ID_CONFLICT": return ErrorKind.OperationIdConflict;
            case "REVISION_CONFLICT": return ErrorKind.RevisionConflict;
            case "STALE_REFERENCE": return ErrorKind.StaleReference;
            default: return ErrorKind.Unknown;
        }
    }
}
