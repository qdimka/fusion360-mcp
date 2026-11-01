using System.Text.Json.Nodes;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// Typed reads of a command frame's params object. Null/absent keys
/// fall back to the caller's default (the tool schema marks required
/// fields; defaults only cover a caller that omitted one).
/// </summary>
public static class ParamReader
{
    /// <summary>String value of <paramref name="key"/> (or <paramref name="fallback"/>).</summary>
    public static string Str(JsonObject p, string key, string fallback)
    {
        var node = p[key];
        return node is JsonValue value && value.TryGetValue<string>(out var s) ? s : fallback;
    }

    /// <summary>String value or null.</summary>
    public static string? StrOrNull(JsonObject p, string key)
    {
        var node = p[key];
        return node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
    }

    /// <summary>Numeric value (JSON numbers deserialize as int/double).</summary>
    public static double Num(JsonObject p, string key, double fallback)
    {
        var node = p[key];
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out var d))
            {
                return d;
            }

            if (value.TryGetValue<int>(out var i))
            {
                return i;
            }
        }

        return fallback;
    }

    /// <summary>Numeric value or null (absent/z_offset-style optionals).</summary>
    public static double? NumOrNull(JsonObject p, string key)
    {
        var node = p[key];
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out var d))
            {
                return d;
            }

            if (value.TryGetValue<int>(out var i))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Integer value (JSON numbers).</summary>
    public static int Int(JsonObject p, string key, int fallback)
    {
        var node = p[key];
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var i))
            {
                return i;
            }

            if (value.TryGetValue<double>(out var d))
            {
                return (int)d;
            }
        }

        return fallback;
    }

    /// <summary>Integer value or null.</summary>
    public static int? IntOrNull(JsonObject p, string key)
    {
        var node = p[key];
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var i))
            {
                return i;
            }

            if (value.TryGetValue<double>(out var d))
            {
                return (int)d;
            }
        }

        return null;
    }

    /// <summary>Boolean value.</summary>
    public static bool Bool(JsonObject p, string key, bool fallback)
    {
        var node = p[key];
        return node is JsonValue value && value.TryGetValue<bool>(out var b) ? b : fallback;
    }

    /// <summary>List value; null when the key is absent or not an array.</summary>
    public static JsonArray? ListOrNull(JsonObject p, string key)
        => p[key] is JsonArray array ? array : null;

    /// <summary>List-or-empty value.</summary>
    public static JsonArray ListOrEmpty(JsonObject p, string key)
        => ListOrNull(p, key) ?? new JsonArray();

    /// <summary>List of string elements (reference <c>profile_sketch_names</c> / <c>body_names</c>).</summary>
    public static string[] StringList(JsonObject p, string key)
    {
        var result = new List<string>();
        foreach (var node in ListOrEmpty(p, key))
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var s))
            {
                result.Add(s);
            }
        }

        return result.ToArray();
    }

    /// <summary>List of int elements (reference <c>face_indices</c>).</summary>
    public static int[] IntList(JsonObject p, string key)
    {
        var result = new List<int>();
        foreach (var node in ListOrEmpty(p, key))
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out var i))
                {
                    result.Add(i);
                }
                else if (value.TryGetValue<double>(out var d))
                {
                    result.Add((int)d);
                }
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// A [x, y, z] number array (create_construction_plane / create_ucs
    /// points); null when the key is absent or not a 3-number array.
    /// </summary>
    public static FusionPoint? Point3(JsonObject p, string key)
    {
        if (p[key] is not JsonArray array || array.Count != 3)
        {
            return null;
        }

        if (NodeNum(array[0], out var x) && NodeNum(array[1], out var y) && NodeNum(array[2], out var z))
        {
            return new FusionPoint(x, y, z);
        }

        return null;
    }

    private static bool NodeNum(JsonNode? node, out double value)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d))
            {
                value = d;
                return true;
            }

            if (v.TryGetValue<int>(out var i))
            {
                value = i;
                return true;
            }
        }

        value = 0;
        return false;
    }

    /// <summary>
    /// A number-or-expression value (create_box_parametric's dimensions:
    /// a JSON number in cm, or a string expression such as 'boxL').
    /// Null when the key is absent or carries some other type.
    /// </summary>
    public static (double Value, string? Expression)? DimOrExpression(JsonObject p, string key)
    {
        var node = p[key];
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var i))
            {
                return (i, (string?)null);
            }

            if (value.TryGetValue<double>(out var d))
            {
                return (d, (string?)null);
            }

            if (value.TryGetValue<string>(out var s) && s.Length > 0)
            {
                return (0.0, s);
            }
        }

        return null;
    }
}
