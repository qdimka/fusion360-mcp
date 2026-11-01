using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.Core.Tests;

/// <summary>
/// P1 (ADR-0009, TOOL_CONTRACTS §1.2/§4): the verification ladder, the
/// named-check wire shape, the "nothing is silent" rule, and the single
/// unified tolerance table.
/// </summary>
public class VerificationTests
{
    // ------------------------------------------------------------------
    // Level ladder + wire round-trip
    // ------------------------------------------------------------------

    [Fact]
    public void Level_WireStrings_RoundTrip()
    {
        Assert.Equal("call_returned", VerificationLevel.CallReturned.ToWireString());
        Assert.Equal("existence_checked", VerificationLevel.ExistenceChecked.ToWireString());
        Assert.Equal("geometry_checked", VerificationLevel.GeometryChecked.ToWireString());
        Assert.Equal("roundtrip_checked", VerificationLevel.RoundtripChecked.ToWireString());

        Assert.Equal(VerificationLevel.CallReturned, VerificationLevelWire.FromWireString("call_returned"));
        Assert.Equal(VerificationLevel.ExistenceChecked, VerificationLevelWire.FromWireString("existence_checked"));
        Assert.Equal(VerificationLevel.GeometryChecked, VerificationLevelWire.FromWireString("geometry_checked"));
        Assert.Equal(VerificationLevel.RoundtripChecked, VerificationLevelWire.FromWireString("roundtrip_checked"));
    }

    [Fact]
    public void Level_UnknownWireString_DegradesToLowestRung()
    {
        // Additive contract: an unknown level must never break a read.
        Assert.Equal(VerificationLevel.CallReturned, VerificationLevelWire.FromWireString("future_level"));
        Assert.Equal(VerificationLevel.CallReturned, VerificationLevelWire.FromWireString(""));
    }

    // ------------------------------------------------------------------
    // Block shape (TOOL_CONTRACTS §1.2)
    // ------------------------------------------------------------------

    [Fact]
    public void ToJson_ProducesTheContractShape()
    {
        var block = Verification.ToJson(
            VerificationLevel.GeometryChecked,
            new[]
            {
                new VerificationCheck("body_count", JsonValue.Create(2), JsonValue.Create(2), true),
                VerificationCheck.Tolerance("mass_g", 7.905, 7.9, VerificationTolerances.Mass(7.9)),
            },
            new[] { new UnverifiedAspect("analytic_volume", "analytic volume unknown") });

        Assert.Equal("geometry_checked", block["level"]!.GetValue<string>());

        var checks = Assert.IsType<JsonArray>(block["checks"]);
        var first = Assert.IsType<JsonObject>(checks[0]);
        Assert.Equal("body_count", first["name"]!.GetValue<string>());
        Assert.Equal(2, first["observed"]!.GetValue<int>());
        Assert.Equal(2, first["expected"]!.GetValue<int>());
        Assert.True(first["passed"]!.GetValue<bool>());

        var mass = Assert.IsType<JsonObject>(checks[1]);
        Assert.Equal("mass_g", mass["name"]!.GetValue<string>());
        Assert.True(mass["passed"]!.GetValue<bool>(), "7.85 vs 7.9 is within the 0.1% mass tolerance");

        var unverified = Assert.IsType<JsonArray>(block["unverified_aspects"]);
        var aspect = Assert.IsType<JsonObject>(unverified[0]);
        Assert.Equal("analytic_volume", aspect["name"]!.GetValue<string>());
        Assert.Equal("analytic volume unknown", aspect["reason"]!.GetValue<string>());
    }

    [Fact]
    public void ToleranceCheck_NullObserved_IsNotAToleranceFailure()
    {
        // A null observation is not a tolerance miss — it is an unverified
        // aspect (§4), so the check must report passed: false.
        var check = VerificationCheck.Tolerance("volume", null, 8.0, VerificationTolerances.Volume(8.0));
        Assert.False(check.Passed);
        var json = check.ToJson();
        // "observed" is present and JSON null on the wire (STJ keeps the
        // property, in-memory value is a null node reference).
        Assert.True(json.TryGetPropertyValue("observed", out var observedNode) && observedNode is null);
        Assert.Equal(8.0, json["expected"]!.GetValue<double>());
    }

    [Fact]
    public void ToleranceCheck_OutsideTolerance_Fails()
    {
        var check = VerificationCheck.Tolerance("volume", 8.5, 8.0, VerificationTolerances.Volume(8.0));
        Assert.False(check.Passed);
    }

    [Fact]
    public void Level_AndCheckTallies_ReadBackFromWire()
    {
        var block = Verification.CallReturned(
            new[] { new UnverifiedAspect("geometry", "no analytic expected geometry") });

        var json = JsonNode.Parse(block.ToJsonString())!.AsObject()!;
        Assert.Equal(VerificationLevel.CallReturned, Verification.Level(json));
        Assert.Equal(0, Verification.PassedCount(json));
        Assert.Equal(0, Verification.TotalCount(json));

        var geometry = Verification.Geometry(
            Array.Empty<UnverifiedAspect>(),
            VerificationCheck.Count("body_exists", 1, 1),
            VerificationCheck.Tolerance("volume", 100.0, 105.0, VerificationTolerances.Volume(105.0)));
        var geometryJson = JsonNode.Parse(geometry.ToJsonString())!.AsObject()!;
        Assert.Equal(VerificationLevel.GeometryChecked, Verification.Level(geometryJson));
        Assert.Equal(1, Verification.PassedCount(geometryJson));
        Assert.Equal(2, Verification.TotalCount(geometryJson));

        // A block read out of baseline-only fields is the lowest rung.
        var baseline = new JsonObject { ["ok"] = true, ["deltas"] = new JsonObject() };
        Assert.Equal(VerificationLevel.CallReturned, Verification.Level(Verification.Read(baseline)));
        Assert.Null(Verification.Read(baseline));
    }

    // ------------------------------------------------------------------
    // The single unified tolerance table (§4)
    // ------------------------------------------------------------------

    [Fact]
    public void VolumeTolerance_UsesTheFloorAndThePercent()
    {
        // |obs − exp| ≤ max(0.01, 0.001·|exp|)
        Assert.Equal(0.01, VerificationTolerances.Volume(8.0), 1e-9);
        Assert.Equal(0.01, VerificationTolerances.Volume(1.0), 1e-9);
        Assert.Equal(1.0, VerificationTolerances.Volume(1000.0), 1e-9);
        Assert.Equal(0.785, VerificationTolerances.Volume(-785.0), 1e-9);
    }

    [Fact]
    public void BBoxTolerance_IsHalfAMillimetre()
    {
        Assert.Equal(0.001, VerificationTolerances.BBox(), 1e-9);
    }

    [Fact]
    public void MassTolerance_IsOneTenthPercent()
    {
        // |obs − exp| ≤ 0.001·|exp| (0.1%)
        Assert.Equal(7.85, VerificationTolerances.Mass(7850.0), 1e-9);
        Assert.Equal(0.0, VerificationTolerances.Mass(0.0), 1e-9);
    }

    [Fact]
    public void WithinAll_PerCoordinate()
    {
        var observed = new[] { 0.0, 0.0, 0.0, 2.0, 2.0, 2.0 };
        Assert.True(VerificationTolerances.WithinAll(observed, observed));
        Assert.True(VerificationTolerances.WithinAll(new[] { 0.0005, 0, 0, 2, 2, 2 }, observed));
        Assert.False(VerificationTolerances.WithinAll(new[] { 0.002, 0, 0, 2, 2, 2 }, observed));
        Assert.False(VerificationTolerances.WithinAll(new[] { 0.0, 0.0 }, observed), "length mismatch is a fail");
    }

    // ------------------------------------------------------------------
    // Envelope additivity: baseline clients keep working
    // ------------------------------------------------------------------

    [Fact]
    public void CommandResult_CarriesVerificationAdditively()
    {
        var payload = new JsonObject
        {
            ["feature_name"] = "Extrude1",
            ["deltas"] = new JsonObject { ["body_count_delta"] = 1 },
            ["verification"] = Verification.Existence(Array.Empty<UnverifiedAspect>(), VerificationCheck.Count("body_exists", 1, 1)),
        };

        var result = CommandResult.Success(payload);
        Assert.True(result.Ok);
        Assert.NotNull(result.Deltas);

        var verification = result.Verification;
        Assert.NotNull(verification);
        Assert.Equal("existence_checked", verification!["level"]!.GetValue<string>());

        // Baseline keys are untouched: the block is purely additive.
        var json = JsonNode.Parse(result.ToJson())!.AsObject()!;
        Assert.True(json["ok"]!.GetValue<bool>());
        Assert.Equal("Extrude1", json["feature_name"]!.GetValue<string>());
        Assert.NotNull(json["deltas"]);
        Assert.NotNull(json["verification"]);

        // Round-trip.
        var reparsed = CommandResult.FromJson(result.ToJson());
        Assert.Equal(VerificationLevel.ExistenceChecked, Verification.Level(reparsed.Verification));
    }
}
