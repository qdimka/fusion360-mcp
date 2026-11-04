using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Protocol;
using ModelContextProtocol.Protocol;

namespace Fusion360.Mcp.Server;

/// <summary>
/// Renders an add-in/mock result dict into MCP <see cref="CallToolResult"/>
/// content blocks. Faithful port of the reference
/// <c>_format_result</c> / <c>_fusion_error_result</c> / <c>_format_deltas</c>
/// (server.py) so the wire output matches the reference byte-for-byte where
/// it matters (tool name, OK/ERROR, error_kind, hints, deltas, image block).
/// </summary>
public static class ResultFormatter
{
    /// <summary>Fields surfaced at the top of the text block; the rest render as <c>key: value</c>. Mirrors the reference <c>_SPECIAL_KEYS</c> plus the P1 additive <c>verification</c> block (ADR-0009; note: <c>mode</c> is intentionally NOT special, so <c>mode: mock</c> renders as a field).</summary>
    private static readonly HashSet<string> SpecialKeys = new()
    {
        "ok", "error_kind", "error_message", "hints", "traceback", "deltas", "image_base64", "verification",
    };

    /// <summary>
    /// Renders a result dict (success or application-level failure) into a
    /// tool result. Port of <c>_format_result</c>.
    /// </summary>
    public static CallToolResult FormatResult(string name, JsonObject result)
    {
        // Application-level failure classified by the add-in (result.ok == false).
        if (!GetOk(result))
        {
            var lines = new List<string>
            {
                $"**{name}** ERROR ({GetString(result, "error_kind", "UNKNOWN")})",
                $"  {GetString(result, "error_message", "(no message)")}",
            };
            if (result.TryGetPropertyValue("operation_id", out var opId) && opId is JsonValue opIdVal &&
                opIdVal.TryGetValue<string>(out var opIdText))
            {
                lines.Add($"  operation_id: {opIdText}");
            }
            if (result["outcome_unknown"]?.GetValue<bool>() == true)
            {
                lines.Add("  outcome_unknown: true");
            }
            if (result["revision_after"] is JsonValue revisionValue &&
                (revisionValue.TryGetValue<long>(out _) || revisionValue.TryGetValue<int>(out _)))
            {
                lines.Add($"  revision_after: {revisionValue.ToJsonString()}");
            }
            // A failed mutation states partial_effects only when the change
            // was directly observed or no-effect was established.
            if (result.TryGetPropertyValue("partial_effects", out var partialNode) &&
                partialNode is JsonValue partialVal && partialVal.TryGetValue<bool>(out var partial))
            {
                lines.Add($"  partial_effects: {(partial ? "true" : "false")}");
            }
            var hints = result.TryGetPropertyValue("hints", out var hintsNode) && hintsNode is JsonArray hintsArray
                ? hintsArray.Where(h => h is not null).Select(h => h!.GetValue<string>()).ToArray()
                : Array.Empty<string>();
            if (hints.Length > 0)
            {
                lines.Add("  hints:");
                lines.AddRange(hints.Select(h => $"    - {h}"));
            }
            // P4: a partial failure enumerates the change it left behind.
            if (result.TryGetPropertyValue("deltas", out var failureDeltas) && failureDeltas is JsonObject failureDeltasObject)
            {
                lines.AddRange(FormatDeltas(failureDeltasObject));
            }
            if (result.TryGetPropertyValue("traceback", out var tbNode) && tbNode is JsonValue tbVal &&
                tbVal.TryGetValue<string>(out var tb) && !string.IsNullOrEmpty(tb))
            {
                lines.Add(string.Empty);
                lines.Add("traceback:");
                lines.Add(tb);
            }

            return new CallToolResult
            {
                Content = new List<ContentBlock> { new TextContentBlock { Text = string.Join("\n", lines) } },
                IsError = true,
            };
        }

        // Success path.
        var successLines = new List<string> { $"**{name}** OK" };
        foreach (var (key, value) in result)
        {
            if (SpecialKeys.Contains(key))
            {
                continue;
            }

            successLines.Add($"  {key}: {RenderValue(value)}");
        }

        if (result.TryGetPropertyValue("deltas", out var deltasNode) && deltasNode is JsonObject deltas)
        {
            successLines.AddRange(FormatDeltas(deltas));
        }

        // P1 (ADR-0009): the optional verification summary — the agent reads
        // the text block, not structuredContent, so the level + check tally
        // must land in the text.
        if (result.TryGetPropertyValue("verification", out var verificationNode) && verificationNode is JsonObject verification)
        {
            successLines.AddRange(FormatVerification(verification));
        }

        var content = new List<ContentBlock> { new TextContentBlock { Text = string.Join("\n", successLines) } };

        // render_view: attach the PNG so vision models can see it.
        //
        // SDK quirk (ModelContextProtocol 1.4.x): ImageContentBlock.Data
        // (ReadOnlyMemory&lt;byte&gt;) is serialized to the wire as a UTF-8
        // character string, while the client-side ContentBlock converter
        // strictly base64-decodes the "data" field — an asymmetric bug. Storing
        // the base64 text's bytes in Data therefore produces a spec-compliant
        // base64 string on the wire (base64 is pure ASCII, so the char
        // serialization is lossless), which every MCP client — including the
        // SDK's own and the reference's Python server's clients — decodes
        // correctly.
        if (result.TryGetPropertyValue("image_base64", out var imageNode) && imageNode is JsonValue imageVal &&
            imageVal.TryGetValue<string>(out var imageB64) && !string.IsNullOrEmpty(imageB64))
        {
            var imageFormat = GetString(result, "image_format", "png");
            content.Add(new ImageContentBlock
            {
                Data = Encoding.UTF8.GetBytes(imageB64),
                MimeType = $"image/{imageFormat}",
            });
        }

        return new CallToolResult { Content = content, IsError = false };
    }

    /// <summary>
    /// Renders a structured add-in error (a <see cref="FusionError"/> raised by
    /// the transport — a timed-out mutation or a classified error frame) as an
    /// isError tool result. Port of <c>_fusion_error_result</c>.
    /// </summary>
    public static CallToolResult FusionErrorResult(string name, FusionError error)
    {
        var lines = new List<string>
        {
            $"**{name}** ERROR ({error.Kind.ToWireString()})",
            $"  {error.Message}",
        };
        if (error.OutcomeUnknown)
        {
            lines.Add("  outcome_unknown: true");
        }
        if (error.Hints.Count > 0)
        {
            lines.Add("  hints:");
            lines.AddRange(error.Hints.Select(h => $"    - {h}"));
        }

        return new CallToolResult
        {
            Content = new List<ContentBlock> { new TextContentBlock { Text = string.Join("\n", lines) } },
            IsError = true,
        };
    }

    /// <summary>
    /// Renders a transport-level failure (not connected / connection lost) as
    /// an isError tool result, mirroring the reference's generic-exception
    /// branch in <c>call_tool</c>.
    /// </summary>
    public static CallToolResult ConnectionErrorResult(string name, Exception error)
    {
        const string text =
            "Error ({0}): {1}\n\nMake sure Fusion 360 is running and the " +
            "Fusion360MCP add-in is started.";
        return new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = string.Format(CultureInfo.InvariantCulture, text, name, error.Message) },
            },
            IsError = true,
        };
    }

    /// <summary>Port of <c>_format_deltas</c> — the mutation deltas sub-dict.</summary>
    private static IReadOnlyList<string> FormatDeltas(JsonObject deltas)
    {
        var lines = new List<string> { "  deltas:" };
        var bcBefore = Num(deltas, "body_count_before");
        var bcAfter = Num(deltas, "body_count_after");
        var bcDelta = Num(deltas, "body_count_delta");
        if (bcBefore is not null || bcAfter is not null)
        {
            lines.Add(
                $"    body_count: {bcBefore?.ToString(CultureInfo.InvariantCulture) ?? "?"} → " +
                $"{bcAfter?.ToString(CultureInfo.InvariantCulture) ?? "?"} (Δ{Signed(bcDelta)})");
        }

        var mgDelta = Num(deltas, "mass_g_delta");
        if (mgDelta is not null)
        {
            var mgBefore = Num(deltas, "mass_g_before");
            var mgAfter = Num(deltas, "mass_g_after");
            lines.Add(
                $"    mass_g:     {mgBefore?.ToString("0.000", CultureInfo.InvariantCulture) ?? "?"} → " +
                $"{mgAfter?.ToString("0.000", CultureInfo.InvariantCulture) ?? "?"} (Δ{Signed3(mgDelta.Value)})");
        }

        if (deltas.TryGetPropertyValue("bbox_before", out var bboxBefore) && bboxBefore is not null)
        {
            lines.Add($"    bbox_before: {RenderValue(bboxBefore)}");
        }

        if (deltas.TryGetPropertyValue("bbox_after", out var bboxAfter) && bboxAfter is not null)
        {
            lines.Add($"    bbox_after:  {RenderValue(bboxAfter)}");
        }

        return lines;
    }

    /// <summary>
    /// P1 (ADR-0009): renders the optional <c>verification</c> block — a
    /// one-line tally <c>verified: &lt;level&gt; (N/M checks)</c> (N =
    /// passed, M = total) plus every unverified aspect on its own line
    /// (nothing is silent, TOOL_CONTRACTS §5.3).
    /// </summary>
    private static IReadOnlyList<string> FormatVerification(JsonObject verification)
    {
        var level = "call_returned";
        if (verification.TryGetPropertyValue("level", out var levelNode)
            && levelNode is JsonValue levelValue
            && levelValue.TryGetValue<string>(out var levelWire))
        {
            level = levelWire;
        }

        var checks = verification.TryGetPropertyValue("checks", out var checksNode) && checksNode is JsonArray checksArray
            ? checksArray
            : new JsonArray();
        var unverified = verification.TryGetPropertyValue("unverified_aspects", out var unverifiedNode) && unverifiedNode is JsonArray unverifiedArray
            ? unverifiedArray
            : new JsonArray();

        var total = 0;
        var passed = 0;
        foreach (var check in checks)
        {
            total++;
            if (check is JsonObject checkObject
                && checkObject["passed"] is JsonValue passedNode
                && passedNode.TryGetValue<bool>(out var p)
                && p)
            {
                passed++;
            }
        }

        var lines = new List<string>
        {
            $"  verified: {level} ({(total == 0 ? "no checks" : $"{passed}/{total} checks")})",
        };

        if (unverified.Count > 0)
        {
            lines.Add("  unverified:");
            foreach (var aspect in unverified)
            {
                var name = "?";
                var reason = string.Empty;
                if (aspect is JsonObject ao)
                {
                    if (ao["name"] is JsonValue nameNode && nameNode.TryGetValue<string>(out var n))
                    {
                        name = n;
                    }

                    if (ao["reason"] is JsonValue reasonNode && reasonNode.TryGetValue<string>(out var r))
                    {
                        reason = r;
                    }
                }

                lines.Add($"    - {name} — {reason}");
            }
        }

        return lines;
    }

    private static bool GetOk(JsonObject result)
        => !result.TryGetPropertyValue("ok", out var ok)
            || ok is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string GetString(JsonObject result, string key, string fallback)
        => result.TryGetPropertyValue(key, out var node) && node is JsonValue value &&
           value.TryGetValue<string>(out var text)
            ? text
            : fallback;

    private static double? Num(JsonObject o, string key)
        => o.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue<double>(out var d)
            ? d
            : null;

    private static string Signed(double? d)
        => d is null ? "?" : d.Value.ToString("+0;-0", CultureInfo.InvariantCulture);

    private static string Signed3(double d)
        => d.ToString("+0.000;-0.000", CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders a JSON value the way the reference's <c>f"{key}: {value}"</c> does
    /// for scalars (strings unquoted, booleans as Python-style words) and as
    /// compact JSON for containers.
    /// </summary>
    private static string RenderValue(JsonNode? node)
        => node switch
        {
            null => "null",
            JsonValue value when value.TryGetValue<string>(out var s) => s,
            JsonValue value when value.TryGetValue<bool>(out var b) => b ? "true" : "false",
            JsonValue value when value.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture),
            _ => node.ToJsonString(),
        };
}
