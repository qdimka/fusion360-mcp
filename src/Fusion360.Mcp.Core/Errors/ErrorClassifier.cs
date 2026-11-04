using System.Text.RegularExpressions;

namespace Fusion360.Mcp.Core.Errors;

/// <summary>
/// Maps exception messages to a stable <see cref="ErrorKind"/> plus a
/// small list of contextual repair hints. Port of reference
/// src/fusion360_mcp/hints.py (canonical table mirrored by
/// addon/server/hints.py; the two Python files are kept in sync by the
/// reference test suite, so this C# table is the C# single copy).
/// </summary>
// DOC: classifier rules live in this file; the error_kind catalog and
// retry_policy mapping are in docs/decisions/errors.md (#classifier).
public static class ErrorClassifier
{
    /// <summary>One classifier rule: regex applied to the message (ignore-case), kind, hints.</summary>
    private sealed record Rule(string Pattern, ErrorKind Kind, string[] Hints);

    // Order matters: first match wins (as in the reference table).
    private static readonly Rule[] Rules =
    {
        new Rule("no active design", ErrorKind.NoActiveDesign, new[]
        {
            "Open or create a design in Fusion before calling mutation tools.",
            "Use get_design_type to confirm the active product is a design.",
        }),
        new Rule("stale reference", ErrorKind.StaleReference, new[]
        {
            "The named object may have been deleted or renamed since your last scene read (get_scene_info).",
            "Re-read the scene with get_scene_info (model_revision) and re-resolve the name before retrying.",
        }),
        new Rule("no profiles? in sketch", ErrorKind.ProfileNotClosed, new[]
        {
            "The sketch has no closed profile to extrude/revolve.",
            "Check that your curves form a closed loop — draw_line segments must share endpoints exactly (use add_constraint coincident).",
            "Inspect the sketch with get_scene_info — a sketch with 0 profiles is never extrudable.",
        }),
        new Rule("sketch\\b.*\\bnot found|no sketch|sketch.*does not exist", ErrorKind.SketchNotFound, new[]
        {
            "The named sketch does not exist in the active design.",
            "List sketches via get_scene_info before referencing one by name.",
        }),
        new Rule("body\\b.*\\bnot found|no body|body.*does not exist", ErrorKind.BodyNotFound, new[]
        {
            "The named body does not exist in the active design.",
            "Use list_components or get_scene_info to see available bodies.",
        }),
        new Rule("self[- ]?intersect", ErrorKind.SelfIntersection, new[]
        {
            "The resulting geometry self-intersects — Fusion cannot resolve it.",
            "For sweeps: check the path curvature vs. the profile size.",
            "For lofts: ensure profiles are oriented consistently.",
            "Try reducing the feature size, or split into smaller operations.",
        }),
        new Rule("regenerat(e|ion) fail|feature\\s+fail(ed|ure)|rebuild fail", ErrorKind.RegenFailed, new[]
        {
            "Fusion could not regenerate the feature with these inputs.",
            "Check for references to deleted geometry upstream in the timeline.",
            "Try undo, then attempt the mutation with different parameters.",
        }),
        new Rule("boolean.*(empty|no[- ]?op|no result|failed)|subtract.*empty", ErrorKind.BooleanNoOp, new[]
        {
            "Boolean operation produced no change — tool and target do not intersect, or the result would be identical to the target.",
            "Verify the tool body actually overlaps the target — use check_interference.",
            "For subtract: confirm the tool body is inside or crosses the target.",
            "Consider move_body to reposition before retrying.",
        }),
        new Rule("invalid (argument|input|parameter)|bad (argument|input|value)", ErrorKind.InvalidInput, new[]
        {
            "Fusion rejected one of the parameters.",
            "Check units (all Fusion API values are in cm), enum values, and that referenced entities still exist.",
        }),
        new Rule("unknown command", ErrorKind.UnknownCommand, new[]
        {
            "The add-in does not know this command — typo or version skew.",
            "Restart the addon (via reload_handler) if you recently added tools.",
        }),
        new Rule("profile.*(open|not closed|unclosed)", ErrorKind.ProfileNotClosed, new[]
        {
            "The profile is open — extrude/revolve need a closed loop.",
            "Draw the missing segment or add coincident constraints at endpoints.",
        }),
        new Rule("timeout", ErrorKind.Timeout, new[]
        {
            "The operation exceeded the 300s bridge timeout.",
            "If a long operation is expected (CAM toolpath, complex fillet), split it into smaller steps.",
        }),
        new Rule("requires (?:parametric|direct[- ]edit)|not (?:supported|permitted|allowed) in (?:parametric|direct[- ]edit)|switch to (?:parametric|direct[- ]edit)", ErrorKind.DesignTypeMismatch, new[]
        {
            "This operation is not permitted in the current design type.",
            "Check get_design_type; some operations (like undo after direct edits) require parametric mode.",
        }),
    };

    /// <summary>Classifies a raw message. Falls back to (UNKNOWN, no hints).</summary>
    public static (ErrorKind Kind, string[] Hints) Classify(string message)
    {
        foreach (var rule in Rules)
        {
            if (Regex.IsMatch(message, rule.Pattern, RegexOptions.IgnoreCase))
            {
                return (rule.Kind, rule.Hints.ToArray());
            }
        }

        return (ErrorKind.Unknown, Array.Empty<string>());
    }

    /// <summary>Classifies an exception (message, or type name when the message is empty).</summary>
    public static (ErrorKind Kind, string[] Hints) Classify(Exception ex)
    {
        var message = ex.Message;
        if (string.IsNullOrEmpty(message))
        {
            message = ex.GetType().Name;
        }

        return Classify(message);
    }
}
