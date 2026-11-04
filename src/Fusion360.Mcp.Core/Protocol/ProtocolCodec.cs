using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fusion360.Mcp.Core.Errors;

namespace Fusion360.Mcp.Core.Protocol;

/// <summary>
/// Newline-delimited JSON codec for the add-in protocol.
///
/// Request frame (client → add-in, reference connection.py):
///   {"type": "&lt;command&gt;", "params": {...}}
///
/// Success frame (add-in → client, reference event_bridge.py):
///   {"status": "success", "result": { ...CommandResult fields... }}
///
/// Error frame (add-in → client — bridge timeout / unknown command /
/// invalid JSON / handler-raise path in socket_server.py):
///   {"status": "error", "message": "...", "error_kind"?: "...", "hints"?: [...]}
///
/// The add-in may also report an application failure *inside* a success
/// frame (command_handler catches handler exceptions and returns
/// status "success" with result.ok == false), so consumers must check
/// <see cref="CommandResult.Ok"/> as well.
/// </summary>
public static class ProtocolCodec
{
    /// <summary>JSON options: wire keys are fixed; no indent; camelCase untouched.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
        PropertyNamingPolicy = null,
    };

    /// <summary>Encodes a command request to a single JSON line (no newline).</summary>
    public static string EncodeCommand(CommandEnvelope command)
    {
        var root = new JsonObject
        {
            ["type"] = command.Command,
            ["params"] = command.Params?.DeepClone() ?? new JsonObject(),
        };

        return root.ToJsonString();
    }

    /// <summary>Parses a request line. Throws FormatException on a malformed frame.</summary>
    public static CommandEnvelope ParseCommand(string jsonLine)
    {
        using var doc = JsonDocument.Parse(jsonLine);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"Command frame missing string 'type' field: {jsonLine}");
        }

        JsonNode? parameters = null;
        if (root.TryGetProperty("params", out var paramsElement))
        {
            parameters = JsonNode.Parse(paramsElement.GetRawText());
        }

        return new CommandEnvelope { Command = type.GetString()!, Params = parameters };
    }

    /// <summary>Encodes a success frame carrying a result object.</summary>
    public static string EncodeSuccess(CommandResult result)
    {
        var root = new JsonObject
        {
            ["status"] = "success",
            ["result"] = result.Fields,
        };

        return root.ToJsonString();
    }

    /// <summary>Builds an infrastructure error frame object (timeout / unknown command / invalid JSON).</summary>
    public static JsonObject BuildErrorFrame(string message, ErrorKind? errorKind = null, string[]? hints = null, string? traceback = null)
    {
        var root = new JsonObject
        {
            ["status"] = "error",
            ["message"] = message,
        };

        if (errorKind is not null)
        {
            root["error_kind"] = errorKind.Value.ToWireString();
        }

        if (hints is not null && hints.Length > 0)
        {
            var hintArray = new JsonArray();
            foreach (var hint in hints)
            {
                hintArray.Add(JsonValue.Create(hint));
            }

            root["hints"] = hintArray;
        }

        if (traceback is not null)
        {
            root["traceback"] = traceback;
        }

        return root;
    }

    /// <summary>Encodes an infrastructure error frame to a single JSON line.</summary>
    public static string EncodeError(string message, ErrorKind? errorKind = null, string[]? hints = null, string? traceback = null)
        => BuildErrorFrame(message, errorKind, hints, traceback).ToJsonString();

    /// <summary>Parses a response frame (success or error).</summary>
    public static ResponseEnvelope ParseResponse(string jsonLine)
    {
        using var doc = JsonDocument.Parse(jsonLine);
        var root = doc.RootElement;

        if (!root.TryGetProperty("status", out var statusElement) ||
            statusElement.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"Response frame missing string 'status' field: {jsonLine}");
        }

        var status = statusElement.GetString()!;
        ResponseEnvelope envelope = status switch
        {
            "success" => new ResponseEnvelope
            {
                Status = "success",
                Result = root.TryGetProperty("result", out var result)
                    ? CommandResult.FromJson(result.GetRawText())
                    : null,
            },
            "error" => new ResponseEnvelope
            {
                Status = "error",
                Message = root.TryGetProperty("message", out var message) ? message.GetString() : null,
                ErrorKind = root.TryGetProperty("error_kind", out var kind)
                    ? ErrorKindExtensions.FromWireString(kind.GetString())
                    : null,
                Hints = root.TryGetProperty("hints", out var hintsElement) && hintsElement.ValueKind == JsonValueKind.Array
                    ? hintsElement.EnumerateArray()
                        .Select(h => h.GetString() ?? string.Empty)
                        .ToArray()
                    : Array.Empty<string>(),
                Traceback = root.TryGetProperty("traceback", out var traceback) ? traceback.GetString() : null,
            },
            _ => throw new FormatException($"Unknown response status '{status}': {jsonLine}"),
        };

        return envelope;
    }
}

/// <summary>One add-in response frame: either a success result or an error.</summary>
public sealed class ResponseEnvelope
{
    /// <summary>"success" or "error".</summary>
    public string Status { get; init; } = "success";

    /// <summary>The result object when <see cref="Status"/> is "success".</summary>
    public CommandResult? Result { get; init; }

    /// <summary>The message when <see cref="Status"/> is "error".</summary>
    public string? Message { get; init; }

    /// <summary>The classification tag when the error frame carries one.</summary>
    public ErrorKind? ErrorKind { get; init; }

    /// <summary>Repair hints when the error frame carries them.</summary>
    public string[] Hints { get; init; } = Array.Empty<string>();

    /// <summary>A traceback when the error frame carries one.</summary>
    public string? Traceback { get; init; }

    /// <summary>True when the frame carries a success result.</summary>
    public bool IsSuccess => Status == "success";

    /// <summary>True when the frame is an error.</summary>
    public bool IsError => Status == "error";
}
