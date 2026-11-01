using System.Globalization;
using Fusion360.Mcp.Core.Units;

namespace Fusion360.Mcp.Core.Tests;

/// <summary>
/// Parameter-units rules — port of the reference
/// addon/server/parameter_units.py test cases (tests/test_parameter_units.py).
/// </summary>
public class ParameterUnitsTests
{
    [Theory]
    [InlineData(1000, "mm", "1000 mm")]
    [InlineData(1200, "mm", "1200 mm")]
    [InlineData(2.5, "cm", "2.5 cm")]
    [InlineData(90, "deg", "90 deg")]
    [InlineData(1, "in", "1 in")]
    public static void TheRequestedUnitReachesTheExpression(double value, string unit, string expected)
    {
        // The unit must survive into the expression — writing the value
        // against a fixed unit, or dropping it, is the bug being fixed.
        Assert.Equal(expected, ParameterUnits.ExpressionFor(value, unit));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public static void AUnitlessParameterKeepsTheBareNumber(string? unit)
    {
        Assert.Equal("7", ParameterUnits.ExpressionFor(7, unit));
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmed()
    {
        Assert.Equal("5 mm", ParameterUnits.ExpressionFor(5, "  mm  "));
    }

    [Theory]
    [InlineData("mm")]
    [InlineData("deg")]
    [InlineData(" in ")]
    public static void AUnitRequiresTheStringForm(string unit)
    {
        Assert.True(ParameterUnits.NeedsExpression(unit));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public static void UnitlessDoesNot(string? unit)
    {
        Assert.False(ParameterUnits.NeedsExpression(unit));
    }

    [Theory]
    [InlineData("mm", "mm")]
    [InlineData(" mm ", "mm")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public static void Normalisation(string? given, string expected)
    {
        Assert.Equal(expected, ParameterUnits.NormalizeUnit(given));
    }

    [Fact]
    public void ValueInputFor_UnitValue_UsesStringExpression()
    {
        Assert.Equal("10 mm", ParameterUnits.ValueInputFor(10, "mm"));
    }

    [Fact]
    public void ValueInputFor_UnitlessValue_UsesRealForm()
    {
        Assert.Null(ParameterUnits.ValueInputFor(10, " "));
    }
}

/// <summary>
/// Hole-geometry face-picking rules — port of the reference
/// addon/server/hole_geometry.py semantics.
/// </summary>
public class HoleGeometryTests
{
    [Theory]
    [InlineData(1.0, true, true)]
    [InlineData(-1.0, false, true)]
    [InlineData(1.0, false, false)]
    [InlineData(0.5, true, false)]
    public static void IsHorizontalFace(double normalZ, bool wantUp, bool expected)
    {
        Assert.Equal(expected, HoleGeometry.IsHorizontalFace(normalZ, wantUp));
    }

    [Fact]
    public void IsHorizontalFace_AllowsTiltWithinTolerance()
    {
        // 0.99995 ≈ acos → 0.554°: a 0.5° tilt passes, a 0.6° tilt does
        // not (cos(0.6°) = 0.9999452 < 0.99995).
        var allowed = Math.Cos(0.5 * Math.PI / 180.0);
        var tooTilted = Math.Cos(0.6 * Math.PI / 180.0);

        Assert.True(HoleGeometry.IsHorizontalFace(allowed, true));
        Assert.False(HoleGeometry.IsHorizontalFace(tooTilted, true));
    }

    [Fact]
    public void FaceRankKey_PicksHighestFaceTiesToLargerArea()
    {
        var best = new[]
        {
            HoleGeometry.FaceRankKey(1.0, 5.0, wantUp: true),
            HoleGeometry.FaceRankKey(1.0, 9.0, wantUp: true), // tie on Z, larger area
            HoleGeometry.FaceRankKey(0.5, 100.0, wantUp: true),
        };
        var winner = best.Max();
        Assert.Equal(1.0, winner.Rank);
        Assert.Equal(9.0, winner.Area);

        var lowest = new[]
        {
            HoleGeometry.FaceRankKey(-2.0, 1.0, wantUp: false),
            HoleGeometry.FaceRankKey(-1.0, 1.0, wantUp: false),
        };
        // wantUp: false inverts the key (rank = -edgeZ), so the LOWEST
        // face wins with the HIGHEST rank: -(-2) = 2.
        var lowestWinner = lowest.Max();
        Assert.Equal(2.0, lowestWinner.Rank);
    }

    [Fact]
    public void PlaneZAt_HorizontalPlane()
    {
        // z = 5 plane
        Assert.Equal(5.0, HoleGeometry.PlaneZAt((0, 0, 1), (0, 0, 5), x: 3, y: 4));
    }

    [Fact]
    public void PlaneZAt_SlopedPlane()
    {
        // Plane: 1*x + 1*y + 1*z = 1 → z = 1 - x - y
        var z = HoleGeometry.PlaneZAt((1, 1, 1), (0, 0, 1), x: 2, y: 3);
        Assert.Equal(1.0 - 2 - 3, z, 10);
    }

    [Fact]
    public void PlaneZAt_VerticalPlane_Throws()
    {
        Assert.Throws<ArgumentException>(() => HoleGeometry.PlaneZAt((1, 0, 0), (0, 0, 0), 1, 1));
    }
}

/// <summary>Unit conventions and conversions.</summary>
public class UnitsTests
{
    [Fact]
    public void InternalUnits_AreCmAndRadians()
    {
        Assert.Equal("cm", InternalUnits.LengthUnit);
        Assert.Equal("rad", InternalUnits.AngleUnit);
    }

    [Fact]
    public void Conversions_Roundtrip()
    {
        var cm = InternalUnits.MillimetresToInternal(1500);
        Assert.Equal(150.0, cm);
        Assert.Equal(1500.0, InternalUnits.InternalToMillimetres(cm));

        var radians = InternalUnits.DegreesToRadians(90);
        Assert.Equal(Math.PI / 2, radians, 10);
        Assert.Equal(90.0, InternalUnits.RadiansToDegrees(radians), 10);
    }

    [Fact]
    public void InvariantFormatting_HoldsAcrossCultures()
    {
        Assert.Equal("2.5", 2.5.ToString(CultureInfo.InvariantCulture));
    }
}
