using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;

namespace Fusion360.Mcp.Core.Protocol;

/// <summary>
/// Unified result of one add-in command. Carries either a success payload
/// (tool-specific fields, <c>ok: true</c>, synthetic deltas for
/// mutations) or a classified failure (<c>ok: false</c> plus
/// error_kind / error_message / hints / traceback).
///
/// Wire shape — this object is the reference add-in's "result" dict
/// (addon/server/command_handler.py execute_command):
///   success: {"ok": true, ...tool fields, "deltas"?: {...}}
///   failure: {"ok": false, "error_kind", "error_message", "hints", "traceback"}
/// </summary>
// DOC: baseline wire shape is stable; optional v2 mutation fields are
// described in TOOL_CONTRACTS §1.2. History: docs/decisions/protocol.md#envelope
public sealed class CommandResult
{
    private readonly JsonObject _fields;

    private CommandResult(JsonObject fields)
    {
        _fields = fields;
    }

    /// <summary>The full result object (all fields, in wire key order).</summary>
    public JsonObject Fields => _fields;

    /// <summary>True when the command succeeded.</summary>
    public bool Ok
    {
        get
        {
            var ok = _fields["ok"];
            return ok is JsonValue jv && jv.TryGetValue<bool>(out var value) && value;
        }
    }

    /// <summary>Mutation deltas (body_count_before/after/delta, mass, bbox) when present.</summary>
    public JsonNode? Deltas => _fields["deltas"];

    /// <summary>Optional P1 (ADR-0009) verification block (TOOL_CONTRACTS §1.2); null for baseline-only results.</summary>
    public JsonObject? Verification => _fields["verification"] as JsonObject;

    /// <summary>Optional P2 (ADR-0010) idempotency key (UUID) on mutation results; null for baseline-only results.</summary>
    public string? OperationId
    {
        get
        {
            if (!_fields.TryGetPropertyValue("operation_id", out var node) || node is not JsonValue value)
            {
                return null;
            }

            return value.TryGetValue<string>(out var wire) ? wire : null;
        }
    }

    /// <summary>
    /// P4: whether a failed mutation left changes in the scene (a failed
    /// handler may have partially applied). True only when the handler
    /// threw AFTER the model moved — the failure result's <c>deltas</c>
    /// enumerate the change. False on a clean failure or a timeout cancelled
    /// before execution. Null when the outcome is unknown or field is absent.
    /// </summary>
    public bool? PartialEffects
    {
        get
        {
            if (!_fields.TryGetPropertyValue("partial_effects", out var node) || node is not JsonValue value)
            {
                return null;
            }

            return value.TryGetValue<bool>(out var wire) ? wire : null;
        }
    }

    /// <summary>Error classification tag when the command failed.</summary>
    public ErrorKind? ErrorKind
    {
        get
        {
            if (!_fields.TryGetPropertyValue("error_kind", out var node) || node is not JsonValue value)
            {
                return null;
            }

            return value.TryGetValue<string>(out var wire) ? ErrorKindExtensions.FromWireString(wire) : null;
        }
    }

    /// <summary>Human-readable failure message.</summary>
    public string? ErrorMessage => _fields.TryGetPropertyValue("error_message", out var v) ? v?.GetValue<string>() : null;

    /// <summary>Contextual repair hints for the failure (empty when none).</summary>
    public string[] Hints
    {
        get
        {
            if (!_fields.TryGetPropertyValue("hints", out var node) || node is not JsonArray hints)
            {
                return Array.Empty<string>();
            }

            var result = new string[hints.Count];
            for (var i = 0; i < hints.Count; i++)
            {
                result[i] = hints[i]?.GetValue<string>() ?? string.Empty;
            }

            return result;
        }
    }

    /// <summary>Python traceback captured on the add-in side, when present.</summary>
    public string? Traceback => _fields.TryGetPropertyValue("traceback", out var t) ? t?.GetValue<string>() : null;

    /// <summary>Reads a tool-specific payload field (null when absent).</summary>
    public JsonNode? Field(string name) => _fields[name];

    /// <summary>
    /// Builds a success result: the tool-specific fields plus the
    /// reference's <c>result.setdefault("ok", True)</c> marker.
    /// </summary>
    public static CommandResult Success(JsonObject payload)
    {
        var fields = payload.DeepClone() as JsonObject ?? new JsonObject();
        fields.TryGetPropertyValue("ok", out _);
        fields["ok"] = true;
        return new CommandResult(fields);
    }

    /// <summary>Builds a classified failure result (reference application-error shape).</summary>
    public static CommandResult Failure(
        ErrorKind kind,
        string errorMessage,
        string[]? hints = null,
        string? traceback = null)
    {
        var fields = new JsonObject
        {
            ["ok"] = false,
            ["error_kind"] = kind.ToWireString(),
            ["error_message"] = errorMessage,
        };

        if (hints is not null && hints.Length > 0)
        {
            var hintArray = new JsonArray();
            foreach (var hint in hints)
            {
                hintArray.Add(JsonValue.Create(hint));
            }

            fields["hints"] = hintArray;
        }

        if (traceback is not null)
        {
            fields["traceback"] = traceback;
        }

        return new CommandResult(fields);
    }

    /// <summary>Serializes the result object to JSON (single line).</summary>
    public string ToJson() => _fields.ToJsonString();

    /// <summary>Parses a result object from JSON.</summary>
    public static CommandResult FromJson(string json)
    {
        var fields = JsonNode.Parse(json) as JsonObject
            ?? throw new FormatException($"Not a JSON object: {json}");
        return new CommandResult(fields);
    }
}
