using System;
using System.Collections.Generic;

namespace Fusion360.Mcp.Core.Protocol;

/// <summary>
/// The single code-side home of the verification tolerance table
/// (TOOL_CONTRACTS §4, ADR-0009). Instruments REFERENCE this table — they
/// never duplicate the values (KOMPAS lesson: tolerances duplicated in two
/// tools drifted apart).
/// </summary>
public static class VerificationTolerances
{
    /// <summary>Volume floor (cm³) — a deviation below this is always a pass.</summary>
    public const double VolumeFloorCm3 = 0.01;

    /// <summary>Volume relative tolerance: 0.1% of |expected|.</summary>
    public const double VolumeRelative = 0.001;

    /// <summary>BBox coordinate tolerance (cm), per min/max coordinate.</summary>
    public const double BBoxCoordinateCm = 0.001;

    /// <summary>Mass relative tolerance: 0.1% of |expected| (g).</summary>
    public const double MassRelative = 0.001;

    /// <summary>§4 volume tolerance: <c>max(0.01, 0.001·|expected|)</c> cm³.</summary>
    public static double Volume(double expectedCm3)
        => Math.Max(VolumeFloorCm3, VolumeRelative * Math.Abs(expectedCm3));

    /// <summary>§4 BBox tolerance: 0.001 cm per coordinate.</summary>
    public static double BBox() => BBoxCoordinateCm;

    /// <summary>§4 mass tolerance: 0.1% of |expected| g.</summary>
    public static double Mass(double expectedGrams) => MassRelative * Math.Abs(expectedGrams);

    /// <summary><c>|observed − expected| ≤ tolerance</c>.</summary>
    public static bool Within(double observed, double expected, double tolerance)
        => Math.Abs(observed - expected) <= tolerance;

    /// <summary>Per-coordinate BBox comparison with the §4 coordinate tolerance.</summary>
    public static bool WithinAll(IReadOnlyList<double> observed, IReadOnlyList<double> expected)
        => WithinAll(observed, expected, BBoxCoordinateCm);

    /// <summary>Per-coordinate comparison with an explicit tolerance (lengths must match).</summary>
    public static bool WithinAll(IReadOnlyList<double> observed, IReadOnlyList<double> expected, double tolerance)
    {
        if (observed.Count != expected.Count)
        {
            return false;
        }

        for (var i = 0; i < observed.Count; i++)
        {
            if (!Within(observed[i], expected[i], tolerance))
            {
                return false;
            }
        }

        return true;
    }
}
