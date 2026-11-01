namespace Fusion360.Mcp.Core.Units;

/// <summary>
/// Fusion 360's internal unit conventions: all length values the API
/// exchanges are centimetres, all angle values are radians. The MCP
/// tool surface documents these to callers; handler code must not mix
/// unit systems.
/// </summary>
public static class InternalUnits
{
    /// <summary>Fusion's internal length unit.</summary>
    public const string LengthUnit = "cm";

    /// <summary>Fusion's internal angle unit.</summary>
    public const string AngleUnit = "rad";

    /// <summary>Length conversion: millimetres → centimetres (internal).</summary>
    public static double MillimetresToInternal(double mm) => mm / 10.0;

    /// <summary>Length conversion: centimetres (internal) → millimetres.</summary>
    public static double InternalToMillimetres(double cm) => cm * 10.0;

    /// <summary>Angle conversion: degrees → radians (internal).</summary>
    public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>Angle conversion: radians (internal) → degrees.</summary>
    public static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}
