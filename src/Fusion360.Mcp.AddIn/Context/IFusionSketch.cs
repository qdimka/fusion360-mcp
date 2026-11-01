namespace Fusion360.Mcp.AddIn;

/// <summary>A 2-D sketch on a construction plane.</summary>
public interface IFusionSketch
{
    /// <summary>The sketch's name (set by create_ucs for its reference sketch).</summary>
    string Name { get; set; }

    bool IsVisible { get; set; }

    /// <summary>Number of closed profiles (get_scene_info / extrudability).</summary>
    int ProfilesCount { get; }

    /// <summary>The sketch's closed profiles (feature inputs take profile i).</summary>
    IFusionProfileCollection Profiles { get; }

    /// <summary>Sketch curve collections (lines, circles, arcs, splines).</summary>
    IFusionSketchCurveCollections Curves { get; }

    /// <summary>All curves of the sketch (reference: list(sketch.sketchCurves)).</summary>
    IReadOnlyList<IFusionSketchCurve> SketchCurves { get; }

    IFusionGeometricConstraints Constraints { get; }
    IFusionSketchDimensions Dimensions { get; }

    /// <summary>Sketch points (add_constraint "midpoint").</summary>
    IFusionSketchPointCollection Points { get; }

    /// <summary>
    /// Adds a sketch point at a model-space position (reference
    /// <c>sketch.sketchPoints.add(sketch.modelToSketchSpace(p))</c> — the
    /// model-to-sketch mapping is the adapter's concern).
    /// </summary>
    IFusionSketchPoint AddPointInModelSpace(FusionPoint modelPoint);

    /// <summary>Offsets one curve in the sketch plane (reference: sketch.offset).</summary>
    void Offset(IFusionSketchCurve curve, FusionPoint direction, double distanceCm);

    /// <summary>Projects a solid edge into the sketch; returns how many curves were projected.</summary>
    int ProjectEdge(IFusionBodyEdge edge);

    /// <summary>
    /// Auto-constrain (Fusion 2026+ preview API; see resultOption 1/2/3).
    /// Returns null when the sketch is not eligible (reference's
    /// <c>result is None</c> path) and raises <see cref="FusionCommandException"/>
    /// when the API is unavailable in this Fusion version.
    /// </summary>
    IFusionAutoConstrainResult? AutoConstrain(int resultOption);

    void DeleteMe();
}

/// <summary>Sketch curve collections of one sketch.</summary>
public interface IFusionSketchCurveCollections
{
    IFusionSketchLineCollection Lines { get; }
    IFusionSketchCircleCollection Circles { get; }
    IFusionSketchArcCollection Arcs { get; }
    IFusionSketchFittedSplineCollection FittedSplines { get; }
    IFusionSketchControlPointSplineCollection ControlPointSplines { get; }
}

public interface IFusionSketchLineCollection
{
    IFusionSketchCurve AddTwoPointRectangle(FusionPoint p1, FusionPoint p2);
    IFusionSketchCurve AddByTwoPoints(FusionPoint p1, FusionPoint p2);

    /// <summary>The i-th sketch line (add_bend's <c>sketchLines.item(0)</c>).</summary>
    IFusionSketchCurve Item(int index);

    /// <summary>
    /// Adds a two-point rectangle and returns its two dimensioning lines —
    /// bottom (item 0) and right (item 1) — for create_box_parametric's
    /// length/width dimensions (reference addTwoPointRectangle's .item(0)/.item(1)).
    /// </summary>
    (IFusionSketchCurve Bottom, IFusionSketchCurve Right) AddTwoPointRectangleLines(FusionPoint p1, FusionPoint p2);
}

public interface IFusionSketchCircleCollection
{
    IFusionSketchCurve AddByCenterRadius(FusionPoint center, double radiusCm);
}

public interface IFusionSketchArcCollection
{
    IFusionSketchCurve AddByCenterStartSweep(FusionPoint center, FusionPoint start, double sweepRadians);
}

public interface IFusionSketchFittedSplineCollection
{
    IFusionSketchCurve Add(IReadOnlyList<FusionPoint> points);
}

public interface IFusionSketchControlPointSplineCollection
{
    IFusionSketchCurve Add(IReadOnlyList<FusionPoint> controlPoints, int degree);
}

/// <summary>A sketch curve (line, circle, arc or spline).</summary>
public interface IFusionSketchCurve : IFusionRevolveAxis
{
    /// <summary>The curve's start sketch point (distance dimensions reference it).</summary>
    IFusionSketchPoint StartSketchPoint { get; }

    /// <summary>
    /// P1 (ADR-0009): the curve's kind tag — "line" / "circle" / "arc" /
    /// "spline". The P1 verification builder classifies a profile as
    /// rectangle (2/4 lines), circle (one circle) or complex from these.
    /// </summary>
    string CurveKind { get; }

    /// <summary>The curve's end sketch point (reference add_two_point_rectangle dimensioning of a line's own span).</summary>
    IFusionSketchPoint EndSketchPoint { get; }

    /// <summary>
    /// Marks the curve as a construction entity (reference
    /// <c>line.isConstruction = True</c> — the revolve axis fallback).
    /// </summary>
    void MarkAsConstruction();

    /// <summary>Trims the curve at the given point.</summary>
    void Trim(FusionPoint point);

    /// <summary>Extends the curve through the given point.</summary>
    void Extend(FusionPoint point);
}

/// <summary>A sketch point (midpoint constraints).</summary>
public interface IFusionSketchPoint
{
}

/// <summary>Sketch points of a sketch.</summary>
public interface IFusionSketchPointCollection
{
    IFusionSketchPoint Item(int index);
}

/// <summary>Geometric constraints of a sketch (add_constraint).</summary>
public interface IFusionGeometricConstraints
{
    void AddCoincident(IFusionSketchCurve? e1, IFusionSketchCurve? e2);
    void AddParallel(IFusionSketchCurve? e1, IFusionSketchCurve? e2);
    void AddPerpendicular(IFusionSketchCurve? e1, IFusionSketchCurve? e2);
    void AddTangent(IFusionSketchCurve? e1, IFusionSketchCurve? e2);
    void AddEqual(IFusionSketchCurve? e1, IFusionSketchCurve? e2);
    void AddFix(IFusionSketchCurve? e1);
    void AddHorizontal(IFusionSketchCurve? e1);
    void AddVertical(IFusionSketchCurve? e1);
    void AddConcentric(IFusionSketchCurve? e1, IFusionSketchCurve? e2);
    void AddCollinear(IFusionSketchCurve? e1, IFusionSketchCurve? e2);
    void AddSmooth(IFusionSketchCurve? e1, IFusionSketchCurve? e2);
    void AddMidPoint(IFusionSketchPoint point, IFusionSketchCurve? e2);
    void AddSymmetry(IFusionSketchCurve? e1, IFusionSketchCurve? e2, IFusionSketchCurve symmetryLine);
}

/// <summary>Sketch dimensions (add_dimension).</summary>
public interface IFusionSketchDimensions
{
    /// <summary>Distance dimension between two entities (orientation: see <see cref="DimensionOrientations"/>).</summary>
    IFusionSketchDimension AddDistanceDimension(IFusionSketchCurve e1, IFusionSketchCurve e2, int orientation, FusionPoint textPoint);

    /// <summary>
    /// Distance dimension between a line's own endpoints
    /// (create_box_parametric: <c>add_distance_dimension(line.startSketchPoint, line.endSketchPoint, ...)</c>).
    /// </summary>
    IFusionSketchDimension AddEndpointDistanceDimension(IFusionSketchCurve line, int orientation, FusionPoint textPoint);

    IFusionSketchDimension AddAngularDimension(IFusionSketchCurve e1, IFusionSketchCurve e2, FusionPoint textPoint);
    IFusionSketchDimension AddRadialDimension(IFusionSketchCurve e1, FusionPoint textPoint);
    IFusionSketchDimension AddDiameterDimension(IFusionSketchCurve e1, FusionPoint textPoint);
}

/// <summary>One sketch dimension; carries its driving parameter.</summary>
public interface IFusionSketchDimension
{
    /// <summary>Sets the dimension's parameter value (cm).</summary>
    void SetParameterValue(double valueCm);

    /// <summary>
    /// Sets the dimension's driving parameter as an expression
    /// (create_box_parametric parametric dims — reference
    /// <c>dim.parameter.expression = value</c>).
    /// </summary>
    void SetParameterExpression(string expression);
}

/// <summary>The auto-constrain outcome (preview API).</summary>
public interface IFusionAutoConstrainResult
{
    bool IsFullyConstrained { get; }
    int AddedConstraints { get; }
    int AddedDimensions { get; }
    int MovedGeometry { get; }
}

/// <summary>Sketches of a root component.</summary>
public interface IFusionSketchCollection
{
    int Count { get; }
    IFusionSketch Item(int index);

    /// <summary>Adds a sketch on the given construction plane.</summary>
    IFusionSketch Add(IFusionConstructionPlane plane);

    /// <summary>
    /// Adds a sketch on a body face (reference create_hole:
    /// <c>root.sketches.add(target_face)</c>).
    /// </summary>
    IFusionSketch AddOnFace(IFusionFace face);
}
