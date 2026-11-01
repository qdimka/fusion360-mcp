namespace Fusion360.Mcp.AddIn;

/// <summary>
/// A 3-D point/triple in Fusion internal units (cm). The facade's
/// replacement for adsk.core Point3D/Vector3D so handlers and fakes
/// never touch adsk types (PLAN.md D5).
/// </summary>
public readonly struct FusionPoint
{
    public FusionPoint(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }
}

/// <summary>
/// Application-level error raised by handlers (port of the reference's
/// <c>RuntimeError</c>). <see cref="Fusion360.Mcp.AddIn.CommandHandler" />
/// catches it, classifies the message with Core's ErrorClassifier and
/// returns the reference's ok:false result frame.
/// </summary>
public sealed class FusionCommandException : Exception
{
    public FusionCommandException(string message) : base(message)
    {
    }
}

/// <summary>
/// Design type ids, mirroring adsk.fusion.DesignTypes (the reference's
/// <c>design_type_id</c> wire field carries these numbers).
/// </summary>
public static class DesignTypes
{
    /// <summary>Parametric design (history/timeline enabled).</summary>
    public const int Parametric = 1;

    /// <summary>Direct design (no timeline).</summary>
    public const int Direct = 2;

    /// <summary>Wires the id to the reference's labels ("parametric"/"direct").</summary>
    public static string Label(int id) => id == Parametric ? "parametric" : "direct";
}

/// <summary>
/// Sketch dimension orientations, mirroring adsk.fusion.DimensionOrientations.
/// </summary>
public static class DimensionOrientations
{
    public const int Aligned = 1;
    public const int Horizontal = 2;
    public const int Vertical = 3;
}

/// <summary>
/// Feature operations, mirroring adsk.fusion.FeatureOperations (the
/// reference's <c>_operation_type</c> string table maps onto these ids).
/// </summary>
public enum FusionFeatureOperation
{
    /// <summary>adsk.fusion.FeatureOperations.NewBodyFeatureOperation.</summary>
    NewBody = 0,

    /// <summary>adsk.fusion.FeatureOperations.JoinFeatureOperation.</summary>
    Join = 1,

    /// <summary>adsk.fusion.FeatureOperations.CutFeatureOperation.</summary>
    Cut = 2,

    /// <summary>adsk.fusion.FeatureOperations.IntersectFeatureOperation.</summary>
    Intersect = 3,
}

/// <summary>
/// Patch-surface boundary continuity, mirroring
/// adsk.fusion.SurfaceContinuityTypes (reference patch_surface cont_map).
/// </summary>
public enum FusionSurfaceContinuity
{
    /// <summary>adsk.fusion.SurfaceContinuityTypes.ConnectedSurfaceContinuityType.</summary>
    Connected = 0,

    /// <summary>adsk.fusion.SurfaceContinuityTypes.TangentSurfaceContinuityType.</summary>
    Tangent = 1,

    /// <summary>adsk.fusion.SurfaceContinuityTypes.CurvatureSurfaceContinuityType.</summary>
    Curvature = 2,
}

/// <summary>Calculation accuracy for physical properties (adsk.fusion.CalculationAccuracy ids — documented assumption, T8).</summary>
public enum FusionCalculationAccuracy
{
    Low = 0,
    Medium = 1,
    High = 2,
    VeryHigh = 3,
}

/// <summary>Units accepted by import_mesh (adsk.fusion.MeshUnits ids — documented assumption, T8).</summary>
public enum FusionMeshUnits
{
    Millimeter = 0,
    Centimeter = 1,
    Meter = 2,
    Inch = 3,
    Foot = 4,
}

/// <summary>CAM setup operation type (adsk.cam OperationTypes ids — documented assumption, T8).</summary>
public enum FusionCamOperationType
{
    Milling = 0,
    Turning = 1,
    Cutting = 2,
}

/// <summary>Output units for CAM post-processing (adsk.cam PostOutputUnitOptions ids — documented assumption, T8).</summary>
public enum FusionCamOutputUnits
{
    Millimeters = 0,
    Inches = 1,
}

/// <summary>
/// A (x, y, z) point in cm as a measure entity (reference
/// measure_distance's "x,y,z" point path: adsk.core.Point3D.create).
/// Handlers build it; the Adsk adapter converts it back to an adsk
/// Point3D, the fakes record it as-is.
/// </summary>
public sealed class FusionMeasurePoint : IFusionMeasurePoint
{
    public FusionMeasurePoint(FusionPoint point) => Point = point;

    public FusionPoint Point { get; }
}
