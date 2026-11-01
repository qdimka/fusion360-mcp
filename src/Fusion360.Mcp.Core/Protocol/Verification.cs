using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Fusion360.Mcp.Core.Protocol;

/// <summary>
/// P1 (ADR-0009, TOOL_CONTRACTS §1.2): the confidence ladder for mutation
/// results. Monotone — a server reports exactly the level it actually
/// confirmed, never higher; an unverified aspect is always named, never
/// hidden behind the level.
/// </summary>
public enum VerificationLevel
{
    /// <summary>The call returned — no scene state was re-read.</summary>
    CallReturned,

    /// <summary>The expected entity / effect exists in the scene (re-read).</summary>
    ExistenceChecked,

    /// <summary>A geometry measurement (volume, bbox) was re-read and compared to the §4 tolerances.</summary>
    GeometryChecked,

    /// <summary>The result survived an export/import round-trip (STL/STEP) and re-read.</summary>
    RoundtripChecked,
}

public static class VerificationLevelWire
{
    /// <summary>Wire string of a level ("call_returned" … "roundtrip_checked").</summary>
    public static string ToWireString(this VerificationLevel level) => level switch
    {
        VerificationLevel.CallReturned => "call_returned",
        VerificationLevel.ExistenceChecked => "existence_checked",
        VerificationLevel.GeometryChecked => "geometry_checked",
        VerificationLevel.RoundtripChecked => "roundtrip_checked",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    /// <summary>
    /// Parses a wire string; an unknown value degrades to the lowest rung
    /// (additive contract: old clients / unknown levels never break a read).
    /// </summary>
    public static VerificationLevel FromWireString(string wire) => wire switch
    {
        "call_returned" => VerificationLevel.CallReturned,
        "existence_checked" => VerificationLevel.ExistenceChecked,
        "geometry_checked" => VerificationLevel.GeometryChecked,
        "roundtrip_checked" => VerificationLevel.RoundtripChecked,
        _ => VerificationLevel.CallReturned,
    };
}

/// <summary>
/// One named check (TOOL_CONTRACTS §1.2): <c>{name, observed, expected,
/// passed}</c>. <c>observed</c> / <c>expected</c> are JSON values (number,
/// string, bool, null); <c>expected</c> is compared with a §4 tolerance, not
/// with the input parameters.
/// </summary>
public sealed record VerificationCheck(string Name, JsonNode? Observed, JsonNode? Expected, bool Passed)
{
    /// <summary>Wire shape <c>{name, observed, expected, passed}</c> (nulls are explicit JSON null).</summary>
    public JsonObject ToJson() => new()
    {
        ["name"] = Name,
        ["observed"] = Observed ?? JsonValue.Create((string?)null),
        ["expected"] = Expected ?? JsonValue.Create((string?)null),
        ["passed"] = Passed,
    };

    /// <summary>A tolerance check: passed when both values are read and within <paramref name="tolerance"/>.</summary>
    public static VerificationCheck Tolerance(string name, double? observed, double? expected, double tolerance)
        => new(name, Value(observed), Value(expected), observed is { } o && expected is { } e && Math.Abs(o - e) <= tolerance);

    /// <summary>A string-outcome check ("applied" vs "applied"-style sentinels).</summary>
    public static VerificationCheck Outcome(string name, string observed, string expected)
        => new(name, JsonValue.Create(observed), JsonValue.Create(expected), observed == expected);

    /// <summary>An integer-outcome check (e.g. body_count_delta vs −1).</summary>
    public static VerificationCheck Count(string name, long observed, long expected)
        => new(name, JsonValue.Create(observed), JsonValue.Create(expected), observed == expected);

    /// <summary>A coordinate-array check (bbox before/after vs expected, §4 BBox tolerance per coordinate).</summary>
    public static VerificationCheck Coords(string name, IReadOnlyList<double> observed, IReadOnlyList<double> expected)
        => new(name, CoordsJson(observed), CoordsJson(expected), VerificationTolerances.WithinAll(observed, expected));

    private static JsonNode? Value(double? v) => v is { } d ? JsonValue.Create(d) : null;

    private static JsonNode CoordsJson(IReadOnlyList<double> v)
    {
        var arr = new JsonArray();
        foreach (var d in v)
        {
            arr.Add(JsonValue.Create(d));
        }

        return arr;
    }
}

/// <summary>
/// An aspect that could NOT be verified (TOOL_CONTRACTS §1.2):
/// <c>{name, reason}</c>. Nothing is silent — every unverified aspect is
/// named with a reason.
/// </summary>
public sealed record UnverifiedAspect(string Name, string Reason)
{
    /// <summary>Wire shape <c>{name, reason}</c>.</summary>
    public JsonObject ToJson() => new() { ["name"] = Name, ["reason"] = Reason };
}

/// <summary>
/// P1 helper: assembles the optional <c>verification</c> block of a mutation
/// result (TOOL_CONTRACTS §1.2) and reads it back out of result fields.
/// </summary>
public static class Verification
{
    /// <summary>
    /// Builds the wire block: <c>{level, checks[], unverified_aspects[]}</c>
    /// — an additive v2 field; absent for non-mutations and old clients.
    /// </summary>
    public static JsonObject ToJson(
        VerificationLevel level,
        IReadOnlyList<VerificationCheck> checks,
        IReadOnlyList<UnverifiedAspect> unverified)
    {
        var checksArray = new JsonArray();
        foreach (var c in checks)
        {
            checksArray.Add(c.ToJson());
        }

        var unverifiedArray = new JsonArray();
        foreach (var a in unverified)
        {
            unverifiedArray.Add(a.ToJson());
        }

        return new JsonObject
        {
            ["level"] = level.ToWireString(),
            ["checks"] = checksArray,
            ["unverified_aspects"] = unverifiedArray,
        };
    }

    /// <summary><c>call_returned</c> block (the default for every mutation without a deeper check).</summary>
    public static JsonObject CallReturned(IReadOnlyList<UnverifiedAspect> unverified, params VerificationCheck[] checks)
        => ToJson(VerificationLevel.CallReturned, checks, unverified);

    /// <summary><c>existence_checked</c> block.</summary>
    public static JsonObject Existence(IReadOnlyList<UnverifiedAspect> unverified, params VerificationCheck[] checks)
        => ToJson(VerificationLevel.ExistenceChecked, checks, unverified);

    /// <summary><c>geometry_checked</c> block.</summary>
    public static JsonObject Geometry(IReadOnlyList<UnverifiedAspect> unverified, params VerificationCheck[] checks)
        => ToJson(VerificationLevel.GeometryChecked, checks, unverified);

    /// <summary><c>roundtrip_checked</c> block (export/import survived).</summary>
    public static JsonObject Roundtrip(IReadOnlyList<UnverifiedAspect> unverified, params VerificationCheck[] checks)
        => ToJson(VerificationLevel.RoundtripChecked, checks, unverified);

    /// <summary>Reads the optional block from result fields (null when absent — old add-in / mock).</summary>
    public static JsonObject? Read(JsonObject fields) => fields["verification"] as JsonObject;

    /// <summary>The confirmed level of a block (the lowest rung when the field is missing).</summary>
    public static VerificationLevel Level(JsonObject? block)
    {
        if (block is null
            || !block.TryGetPropertyValue("level", out var node)
            || node is not JsonValue value
            || !value.TryGetValue<string>(out var wire))
        {
            return VerificationLevel.CallReturned;
        }

        return VerificationLevelWire.FromWireString(wire);
    }

    /// <summary>How many checks in a block passed (0 for an absent block).</summary>
    public static int PassedCount(JsonObject? block)
    {
        var total = 0;
        var passed = 0;
        foreach (var c in Checks(block))
        {
            total++;
            if (c is JsonObject co && co["passed"] is JsonValue pv && pv.TryGetValue<bool>(out var b) && b)
            {
                passed++;
            }
        }

        return passed;
    }

    /// <summary>How many checks a block carries (0 for an absent block).</summary>
    public static int TotalCount(JsonObject? block)
    {
        var n = 0;
        foreach (var _ in Checks(block))
        {
            n++;
        }

        return n;
    }

    private static IEnumerable<JsonNode?> Checks(JsonObject? block)
    {
        if (block?.TryGetPropertyValue("checks", out var node) is true && node is JsonArray arr)
        {
            foreach (var c in arr)
            {
                yield return c;
            }
        }
    }
}
