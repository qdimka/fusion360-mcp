// Compile-time stub of the adsk.fusion .NET API surface used by
// Fusion360.Mcp.AddIn (task T5). Companion to AdskCoreStubs.cs —
// everything here compiles, every member throws at runtime.
//
// The stub mirrors the documented C# adsk surface (Python binding
// names, camelCase→PascalCase). Where the C# API is not verifiable
// offline (method signatures of preview APIs such as AutoConstrain,
// the exact collection item types), the documented assumption is
// noted inline; reconcile against the genuine adsk assemblies on a
// Windows Fusion install (PLAN.md §6, task T8).

namespace adsk.fusion
{
    using System.Collections;
    using System.Collections.Generic;
    using adsk.core;

    /// <summary>Common to every deletable entity (each real type has Name/DeleteMe).</summary>
    public interface IDeletableEntity
    {
        string Name { get; }
        void DeleteMe();
    }

    /// <summary>Base of all products (reference: adsk.fusion.Product).</summary>
    public class Product
    {
    }

    /// <summary>The Design product of a document (reference: adsk.fusion.Design).</summary>
    public sealed class Design : Product
    {
        /// <summary>The document this product belongs to.</summary>
        public Document ParentDocument => throw new NotSupportedException("adsknet stub");

        /// <summary>Product type string (e.g. "Design").</summary>
        public string ProductType => throw new NotSupportedException("adsknet stub");

        /// <summary>Current design type (parametric or direct).</summary>
        public DesignTypes DesignType { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>The root component of the design.</summary>
        public RootComponent RootComponent => throw new NotSupportedException("adsknet stub");

        /// <summary>The feature timeline (null for direct designs).</summary>
        public Timeline? Timeline => throw new NotSupportedException("adsknet stub");

        /// <summary>User parameters of the design.</summary>
        public ParameterCollection UserParameters => throw new NotSupportedException("adsknet stub");

        /// <summary>design.appearances (adsk.fusion.AppearanceCollection — set_color / set_appearance).</summary>
        public AppearanceCollection Appearances => throw new NotSupportedException("adsknet stub");

        /// <summary>design.exportManager (export_stl / export_step / export_fusion_file).</summary>
        public ExportManager ExportManager => throw new NotSupportedException("adsknet stub");

        /// <summary>reference design.createInterferenceInput(bodies) — check_interference.</summary>
        public InterferenceInput CreateInterferenceInput(ObjectCollection bodies)
            => throw new NotSupportedException("adsknet stub");

        /// <summary>reference design.analyzeInterference(inp) — null when there is no interference.</summary>
        public Interference? AnalyzeInterference(InterferenceInput input)
            => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>Products of a document (reference: adsk.fusion.ProductCollection).</summary>
    public sealed class ProductCollection
    {
        /// <summary>
        /// Finds the product of a given type string ("DesignProductType" or
        /// "CAMProductType"); null when the document has no such product yet
        /// (the CAM product appears only after the Manufacture workspace is
        /// activated once — reference _get_cam fallback).
        /// </summary>
        public Product? ItemByProductType(string productType) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>Design type ids (reference: adsk.fusion.DesignTypes).</summary>
    public enum DesignTypes
    {
        ParametricDesignType = 1,
        DirectDesignType = 2,
    }

    /// <summary>Dimension orientation (reference: adsk.fusion.DimensionOrientations).</summary>
    public enum DimensionOrientations
    {
        AlignedDimensionOrientation = 1,
        HorizontalDimensionOrientation = 2,
        VerticalDimensionOrientation = 3,
    }

    /// <summary>
    /// AutoConstrain result options (Fusion 2026+ preview API).
    /// NOTE: may be absent from older adsk builds — the adapter catches
    /// the lookup failure and raises a classified UNKNOWN error.
    /// </summary>
    public enum AutoConstrainResultTypes
    {
        Option1AutoConstrainResultType = 1,
        Option2AutoConstrainResultType = 2,
        Option3AutoConstrainResultType = 3,
    }

    /// <summary>A component (root or child) of a design.</summary>
    public class Component
    {
        public string Name { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
        public BRepBodyCollection BRepBodies => throw new NotSupportedException("adsknet stub");
        public OccurrenceCollection AllOccurrences => throw new NotSupportedException("adsknet stub");
        public OccurrenceCollection Occurrences => throw new NotSupportedException("adsknet stub");
        public PhysicalProperties PhysicalProperties => throw new NotSupportedException("adsknet stub");

        // Hosted geometry/feature entities (create_box_parametric's
        // component_name path drives these on a CHILD component).
        public SketchCollection Sketches => throw new NotSupportedException("adsknet stub");
        public FeatureCollection Features => throw new NotSupportedException("adsknet stub");
        public ConstructionPlaneCollection ConstructionPlanes => throw new NotSupportedException("adsknet stub");
        public ConstructionAxisCollection ConstructionAxes => throw new NotSupportedException("adsknet stub");
        public ConstructionPointCollection ConstructionPoints => throw new NotSupportedException("adsknet stub");

        /// <summary>Axis-aligned bounding box in world space (null when empty).</summary>
        public BoundingBox? BoundingBox => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.Component.originConstructionPoint (joint anchor).</summary>
        public ConstructionPoint OriginConstructionPoint => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.Component.appearance (null when unassigned).</summary>
        public Appearance? Appearance { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>adsk.fusion.Component.meshBodies (import_mesh / compare_meshes).</summary>
        public MeshBodyCollection MeshBodies => throw new NotSupportedException("adsknet stub");

        /// <summary>Builds joint geometry at a component occurrence's origin (reference
        /// adsk.fusion.JointGeometry.createByPoint — documented .NET shape, T8).</summary>
        public JointGeometry CreateJointGeometryByPoint(Occurrence occurrence, ConstructionPoint point)
            => throw new NotSupportedException("adsknet stub");

        /// <summary>Builds UCS geometry at a sketch point (reference component.createUcsGeometryAtPoint).</summary>
        public UserCoordinateSystemGeometry CreateUcsGeometryAtPoint(SketchPoint point)
            => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>The root component of a design (extends Component).</summary>
    public sealed class RootComponent : Component
    {
        public JointCollection Joints => throw new NotSupportedException("adsknet stub");
        public AsBuiltJointCollection AsBuiltJoints => throw new NotSupportedException("adsknet stub");
        public RigidGroupCollection RigidGroups => throw new NotSupportedException("adsknet stub");

        /// <summary>Section analyses (reference root.analyses — create_section_analysis).</summary>
        public AnalysisCollection Analyses => throw new NotSupportedException("adsknet stub");

        /// <summary>User coordinate systems (reference root.userCoordinateSystems — create_ucs).</summary>
        public UcsCollection UserCoordinateSystems => throw new NotSupportedException("adsknet stub");

        public ConstructionPlane XYConstructionPlane => throw new NotSupportedException("adsknet stub");
        public ConstructionPlane YZConstructionPlane => throw new NotSupportedException("adsknet stub");
        public ConstructionPlane XZConstructionPlane => throw new NotSupportedException("adsknet stub");

        /// <summary>Default construction axis along X (reference root.xConstructionAxis).</summary>
        public ConstructionAxis XConstructionAxis => throw new NotSupportedException("adsknet stub");

        /// <summary>Default construction axis along Y (reference root.yConstructionAxis).</summary>
        public ConstructionAxis YConstructionAxis => throw new NotSupportedException("adsknet stub");

        /// <summary>Default construction axis along Z (reference root.zConstructionAxis).</summary>
        public ConstructionAxis ZConstructionAxis => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A component occurrence (reference: adsk.fusion.Occurrence).</summary>
    public sealed class Occurrence
    {
        /// <summary>The component this occurrence references.</summary>
        public Component Component => throw new NotSupportedException("adsknet stub");

        public bool IsVisible => throw new NotSupportedException("adsknet stub");

        /// <summary>Body proxies of this occurrence in root coordinate space.</summary>
        public BRepBodyCollection BRepBodies => throw new NotSupportedException("adsknet stub");

        public PhysicalProperties PhysicalProperties => throw new NotSupportedException("adsknet stub");

        /// <summary>Axis-aligned bounding box of this occurrence in root space (export_view_sheet).</summary>
        public BoundingBox? BoundingBox => throw new NotSupportedException("adsknet stub");

        /// <summary>Removes the occurrence (and the component it references).</summary>
        public void DeleteMe() => throw new NotSupportedException("adsknet stub");
    }

    public sealed class OccurrenceCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public Occurrence Item(int index) => throw new NotSupportedException("adsknet stub");

        /// <summary>Adds a new component occurrence with a placement (reference addNewComponent).</summary>
        public Occurrence AddNewComponent(Matrix3D transform) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A B-rep body (reference: adsk.fusion.BRepBody).</summary>
    public sealed class BRepBody : IDeletableEntity
    {
        public string Name { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>Volume in cm^3.</summary>
        public double Volume => throw new NotSupportedException("adsknet stub");

        /// <summary>Surface area in cm^2.</summary>
        public double Area => throw new NotSupportedException("adsknet stub");

        public Material? Material => throw new NotSupportedException("adsknet stub");
        public bool IsVisible { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        public FaceCollection Faces => throw new NotSupportedException("adsknet stub");
        public EdgeCollection Edges => throw new NotSupportedException("adsknet stub");
        public VertexCollection Vertices => throw new NotSupportedException("adsknet stub");

        public BoundingBox? BoundingBox => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.BRepBody.entityToken (export's sibling-hide workaround).</summary>
        public string? EntityToken => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.BRepBody.assemblyContext (owning occurrence; null at root).</summary>
        public Occurrence? AssemblyContext => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.BRepBody.appearance (null when unassigned).</summary>
        public Appearance? Appearance { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>adsk.fusion.BRepBody.getPhysicalProperties(accuracy) — physical_properties tool.</summary>
        public PhysicalProperties GetPhysicalProperties(CalculationAccuracy accuracy)
            => throw new NotSupportedException("adsknet stub");

        public void DeleteMe() => throw new NotSupportedException("adsknet stub");
    }

    public sealed class BRepBodyCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public BRepBody Item(int index) => throw new NotSupportedException("adsknet stub");

        /// <summary>Adds a body under a base feature (direct-primitive path).</summary>
        public void Add(BRepBody body, BaseFeature baseFeature) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class Material
    {
        public string? Name => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A body face (shell/hole/draft/thicken face selection).</summary>
    public sealed class Face
    {
        /// <summary>Face area in cm^2.</summary>
        public double Area => throw new NotSupportedException("adsknet stub");

        /// <summary>The face's bounding box (reference face.boundingBox).</summary>
        public BoundingBox BoundingBox => throw new NotSupportedException("adsknet stub");

        /// <summary>An arbitrary point on the face (reference face.pointOnFace).</summary>
        public Point3D PointOnFace => throw new NotSupportedException("adsknet stub");

        /// <summary>The face's evaluator (normal queries).</summary>
        public FaceEvaluator Evaluator => throw new NotSupportedException("adsknet stub");

        /// <summary>The underlying geometry (castable to adsk.core.Plane when planar).</summary>
        public adsk.core.Geometry Geometry => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.Face.appearance (set_appearance face target — null when unassigned).</summary>
        public Appearance? Appearance { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    /// <summary>Face normal queries (reference face.evaluator.getNormalAtPoint).</summary>
    public sealed class FaceEvaluator
    {
        /// <summary>Outward normal at a point; false when the query fails (reference (ok, normal) tuple).</summary>
        public bool GetNormalAtPoint(Point3D point, out Vector3D normal) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class FaceCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public Face Item(int index) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A body vertex (edge start/end geometry).</summary>
    public sealed class Vertex
    {
        /// <summary>The vertex geometry as a point (reference vertex.geometry).</summary>
        public Point3D Geometry => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A body edge (reference adsk.fusion.Edge: pointOnEdge + end-vertices).</summary>
    public sealed class Edge
    {
        /// <summary>A representative point on the edge (reference edge.pointOnEdge).</summary>
        public Point3D PointOnEdge => throw new NotSupportedException("adsknet stub");

        /// <summary>The edge's start vertex.</summary>
        public Vertex StartVertex => throw new NotSupportedException("adsknet stub");

        /// <summary>The edge's end vertex.</summary>
        public Vertex EndVertex => throw new NotSupportedException("adsknet stub");
    }

    public sealed class EdgeCollection : IEnumerable<Edge>
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public Edge Item(int index) => throw new NotSupportedException("adsknet stub");
        public IEnumerator<Edge> GetEnumerator() => throw new NotSupportedException("adsknet stub");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class VertexCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A 3-D bounding box (reference: adsk.fusion.BoundingBox).</summary>
    public sealed class BoundingBox
    {
        public Point3D MinPoint => throw new NotSupportedException("adsknet stub");
        public Point3D MaxPoint => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>Physical properties of a body/component (mass in kg).</summary>
    public sealed class PhysicalProperties
    {
        /// <summary>Mass in kg (reference props.mass).</summary>
        public double Mass => throw new NotSupportedException("adsknet stub");

        /// <summary>Volume in cm^3 (reference props.volume).</summary>
        public double Volume => throw new NotSupportedException("adsknet stub");

        /// <summary>Surface area in cm^2 (reference props.area).</summary>
        public double Area => throw new NotSupportedException("adsknet stub");

        /// <summary>Density in g/cm^3 (reference props.density).</summary>
        public double Density => throw new NotSupportedException("adsknet stub");

        /// <summary>Center of mass in cm (reference props.centerOfMass).</summary>
        public Point3D CenterOfMass => throw new NotSupportedException("adsknet stub");
    }

    // ── sketches ────────────────────────────────────────────────────

    /// <summary>A 2-D sketch on a construction plane (reference: adsk.fusion.Sketch).</summary>
    public sealed class Sketch : IDeletableEntity
    {
        public string Name { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
        public bool IsVisible { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
        public ProfileCollection Profiles => throw new NotSupportedException("adsknet stub");
        public SketchCurveCollection SketchCurves => throw new NotSupportedException("adsknet stub");
        public GeometricConstraints GeometricConstraints => throw new NotSupportedException("adsknet stub");
        public SketchDimensions SketchDimensions => throw new NotSupportedException("adsknet stub");
        public SketchPointCollection SketchPoints => throw new NotSupportedException("adsknet stub");

        /// <summary>Offsets the given curves in the sketch plane.</summary>
        public void Offset(ObjectCollection curves, Point3D direction, double distance) =>
            throw new NotSupportedException("adsknet stub");

        /// <summary>Projects a solid edge into the sketch; returns the projected curves.</summary>
        public ObjectCollection Project(Edge source) => throw new NotSupportedException("adsknet stub");

        /// <summary>Maps a model-space point into this sketch's 2-D space (reference sketch.modelToSketchSpace).</summary>
        public Point3D ModelToSketchSpace(Point3D modelPoint) => throw new NotSupportedException("adsknet stub");

        /// <summary>Preview API (Fusion 2026+): builds the auto-constrain input.</summary>
        public AutoConstrainInput CreateAutoConstrainInput() => throw new NotSupportedException("adsknet stub");

        /// <summary>
        /// Preview API (Fusion 2026+): runs auto-constrain; null when the
        /// sketch is not eligible (option 3) — mirrors the reference's
        /// ``result is None`` path.
        /// </summary>
        public AutoConstrainResult? AutoConstrain(AutoConstrainInput input) =>
            throw new NotSupportedException("adsknet stub");

        public void DeleteMe() => throw new NotSupportedException("adsknet stub");
    }

    public sealed class SketchCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public Sketch Item(int index) => throw new NotSupportedException("adsknet stub");

        /// <summary>Adds a sketch on the given construction plane.</summary>
        public Sketch Add(ConstructionPlane plane) => throw new NotSupportedException("adsknet stub");

        /// <summary>Adds a sketch on a body face (reference create_hole: root.sketches.add(target_face)).</summary>
        public Sketch Add(Face face) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class Profile
    {
    }

    public sealed class ProfileCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public Profile Item(int index) => throw new NotSupportedException("adsknet stub");

        /// <summary>Reference loft_input.loftSections.add(profile).</summary>
        public void Add(Profile profile) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A sketch curve (base of lines, circles, arcs, splines).</summary>
    public abstract class SketchCurve
    {
        public SketchPoint StartSketchPoint => throw new NotSupportedException("adsknet stub");

        /// <summary>The curve's end sketch point (add_two_point_rectangle dimensioning).</summary>
        public SketchPoint EndSketchPoint => throw new NotSupportedException("adsknet stub");

        /// <summary>Trims the curve at the given point.</summary>
        public void Trim(Point3D point) => throw new NotSupportedException("adsknet stub");

        /// <summary>Extends the curve through the given point.</summary>
        public void Extend(Point3D point) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class SketchLine : SketchCurve
    {
        /// <summary>Construction flag (reference line.isConstruction).</summary>
        public bool IsConstruction { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }
    public sealed class SketchCircle : SketchCurve { }
    public sealed class SketchArc : SketchCurve { }
    public sealed class SketchFittedSpline : SketchCurve { }
    public sealed class SketchControlPointSpline : SketchCurve { }

    public sealed class SketchPoint
    {
        public Point3D Point => throw new NotSupportedException("adsknet stub");
    }

    public sealed class SketchPointCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public SketchPoint Item(int index) => throw new NotSupportedException("adsknet stub");
        public SketchPoint Add(Point3D point) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>All curves of a sketch (iterable, like the Python binding).</summary>
    public sealed class SketchCurveCollection : IEnumerable<SketchCurve>
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public SketchCurve Item(int index) => throw new NotSupportedException("adsknet stub");

        public SketchLineCollection SketchLines => throw new NotSupportedException("adsknet stub");
        public SketchCircleCollection SketchCircles => throw new NotSupportedException("adsknet stub");
        public SketchArcCollection SketchArcs => throw new NotSupportedException("adsknet stub");
        public SketchFittedSplineCollection SketchFittedSplines => throw new NotSupportedException("adsknet stub");
        public SketchControlPointSplineCollection SketchControlPointSplines => throw new NotSupportedException("adsknet stub");

        public IEnumerator<SketchCurve> GetEnumerator() => throw new NotSupportedException("adsknet stub");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class SketchLineCollection
    {
        /// <summary>adsk.fusion.SketchLineCollection.item(i) — reference add_bend uses item(0).</summary>
        public SketchLine Item(int index) => throw new NotSupportedException("adsknet stub");

        public SketchLine AddTwoPointRectangle(Point3D p1, Point3D p2) => throw new NotSupportedException("adsknet stub");
        public SketchLine AddByTwoPoints(Point3D p1, Point3D p2) => throw new NotSupportedException("adsknet stub");

        /// <summary>
        /// The two lines (bottom, right) of a two-point rectangle — reference
        /// create_box_parametric's <c>addTwoPointRectangle(...).item(0/1)</c>.
        /// Documented assumption (T8): the genuine adsk may return these directly
        /// from AddTwoPointRectangle.
        /// </summary>
        public SketchLineCollection AddTwoPointRectangleLines(Point3D p1, Point3D p2)
            => throw new NotSupportedException("adsknet stub");
    }

    public sealed class SketchCircleCollection
    {
        public SketchCircle AddByCenterRadius(Point3D center, double radius) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class SketchArcCollection
    {
        public SketchArc AddByCenterStartSweep(Point3D center, Point3D start, double sweepRadians) =>
            throw new NotSupportedException("adsknet stub");
    }

    public sealed class SketchFittedSplineCollection
    {
        public SketchFittedSpline Add(ObjectCollection points) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class SketchControlPointSplineCollection
    {
        public SketchControlPointSpline Add(ObjectCollection controlPoints, int degree) =>
            throw new NotSupportedException("adsknet stub");
    }

    /// <summary>Geometric constraints of a sketch (reference: adsk.fusion.GeometricConstraints).</summary>
    public sealed class GeometricConstraints
    {
        public GeometricConstraint AddCoincident(SketchCurve? e1, SketchCurve? e2) => throw new NotSupportedException("adsknet stub");
        public GeometricConstraint AddParallel(SketchCurve? e1, SketchCurve? e2) => throw new NotSupportedException("adsknet stub");
        public GeometricConstraint AddPerpendicular(SketchCurve? e1, SketchCurve? e2) => throw new NotSupportedException("adsknet stub");
        public GeometricConstraint AddTangent(SketchCurve? e1, SketchCurve? e2) => throw new NotSupportedException("adsknet stub");
        public GeometricConstraint AddEqual(SketchCurve? e1, SketchCurve? e2) => throw new NotSupportedException("adsknet stub");
        public GeometricConstraint AddFix(SketchCurve? e1) => throw new NotSupportedException("adsknet stub");
        public GeometricConstraint AddHorizontal(SketchCurve? e1) => throw new NotSupportedException("adsknet stub");
        public GeometricConstraint AddVertical(SketchCurve? e1) => throw new NotSupportedException("adsknet stub");
        public GeometricConstraint AddConcentric(SketchCurve? e1, SketchCurve? e2) => throw new NotSupportedException("adsknet stub");
        public GeometricConstraint AddCollinear(SketchCurve? e1, SketchCurve? e2) => throw new NotSupportedException("adsknet stub");
        public GeometricConstraint AddSmooth(SketchCurve? e1, SketchCurve? e2) => throw new NotSupportedException("adsknet stub");
        public GeometricConstraint AddMidPoint(SketchPoint point, SketchCurve? e2) => throw new NotSupportedException("adsknet stub");
        public GeometricConstraint AddSymmetry(SketchCurve? e1, SketchCurve? e2, SketchCurve symmetryLine) =>
            throw new NotSupportedException("adsknet stub");
    }

    public sealed class GeometricConstraint
    {
    }

    /// <summary>Sketch dimensions (distance/angular/radial/diameter).</summary>
    public sealed class SketchDimensions
    {
        public SketchDimension AddDistanceDimension(SketchPoint e1, SketchPoint e2, int orientation, Point3D textPoint) =>
            throw new NotSupportedException("adsknet stub");

        public SketchDimension AddAngularDimension(SketchCurve e1, SketchCurve e2, Point3D textPoint) =>
            throw new NotSupportedException("adsknet stub");

        public SketchDimension AddRadialDimension(SketchCurve e1, Point3D textPoint) =>
            throw new NotSupportedException("adsknet stub");

        public SketchDimension AddDiameterDimension(SketchCurve e1, Point3D textPoint) =>
            throw new NotSupportedException("adsknet stub");

        /// <summary>Distance dimension across a curve's own endpoints (create_box_parametric).</summary>
        public SketchDimension AddEndpointDistanceDimension(SketchCurve e1, int orientation, Point3D textPoint)
            => throw new NotSupportedException("adsknet stub");
    }

    public sealed class SketchDimension
    {
        /// <summary>The parameter that drives the dimension value.</summary>
        public Parameter Parameter => throw new NotSupportedException("adsknet stub");
    }

    // ── construction geometry ───────────────────────────────────────

    /// <summary>A construction plane (reference: adsk.fusion.ConstructionPlane).</summary>
    public sealed class ConstructionPlane : IDeletableEntity
    {
        public string Name => throw new NotSupportedException("adsknet stub");
        public void DeleteMe() => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ConstructionPlaneCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public ConstructionPlane Item(int index) => throw new NotSupportedException("adsknet stub");

        /// <summary>Creates a new offset-plane input (reference: createInput).</summary>
        public ConstructionPlaneInput CreateInput() => throw new NotSupportedException("adsknet stub");

        /// <summary>Adds a construction plane from an input.</summary>
        public ConstructionPlane Add(ConstructionPlaneInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ConstructionPlaneInput
    {
        public void SetByOffset(ConstructionPlane basePlane, ValueInput offset) =>
            throw new NotSupportedException("adsknet stub");

        /// <summary>Plane through an angle around an axis (create_construction_plane).</summary>
        public void SetByAngle(ConstructionAxis axis, ValueInput angle, ConstructionPlane basePlane)
            => throw new NotSupportedException("adsknet stub");

        /// <summary>Plane bisecting two planes (create_construction_plane).</summary>
        public void SetByTwoPlanes(ConstructionPlane planeOne, ConstructionPlane planeTwo)
            => throw new NotSupportedException("adsknet stub");

        /// <summary>Plane through three points (create_construction_plane).</summary>
        public void SetByThreePoints(Point3D p1, Point3D p2, Point3D p3)
            => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A construction axis.</summary>
    public sealed class ConstructionAxis : IDeletableEntity
    {
        public string Name => throw new NotSupportedException("adsknet stub");
        public void DeleteMe() => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ConstructionAxisCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public ConstructionAxis Item(int index) => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.ConstructionAxisCollection.createInput (create_construction_axis).</summary>
        public ConstructionAxisInput CreateInput() => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.ConstructionAxisCollection.add(input).</summary>
        public ConstructionAxis Add(ConstructionAxisInput input) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.fusion.ConstructionAxisInput (create_construction_axis method set).</summary>
    public sealed class ConstructionAxisInput
    {
        /// <summary>Axis through two points.</summary>
        public void SetByTwoPoints(Point3D p1, Point3D p2) => throw new NotSupportedException("adsknet stub");

        /// <summary>Axis of the intersection of two planes.</summary>
        public void SetByTwoPlanes(ConstructionPlane planeOne, ConstructionPlane planeTwo)
            => throw new NotSupportedException("adsknet stub");

        /// <summary>Axis along an edge.</summary>
        public void SetByEdge(Edge edge) => throw new NotSupportedException("adsknet stub");

        /// <summary>Axis perpendicular to a plane at a point.</summary>
        public void SetByPerpendicularAtPoint(ConstructionPlane plane, Point3D point)
            => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A construction point.</summary>
    public sealed class ConstructionPoint : IDeletableEntity
    {
        public string Name => throw new NotSupportedException("adsknet stub");
        public void DeleteMe() => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ConstructionPointCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public ConstructionPoint Item(int index) => throw new NotSupportedException("adsknet stub");
    }

    // ── assembly entities (delete_all sweeps) ───────────────────────

    public sealed class Joint : IDeletableEntity
    {
        public string Name => throw new NotSupportedException("adsknet stub");
        public void DeleteMe() => throw new NotSupportedException("adsknet stub");
    }

    public sealed class JointCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public Joint Item(int index) => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.JointCollection.createInput(geoOne, geoTwo) (add_joint).</summary>
        public JointInput CreateInput(JointGeometry geometryOne, JointGeometry geometryTwo)
            => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.JointCollection.add(input) (add_joint).</summary>
        public Joint Add(JointInput input) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>Joint types (reference add_joint joint_types table).</summary>
    public enum JointTypes
    {
        RigidJointType = 0,
        RevoluteJointType = 1,
        SliderJointType = 2,
        CylindricalJointType = 3,
        PinSlotJointType = 4,
        PlanarJointType = 5,
        BallJointType = 6,
    }

    /// <summary>
    /// adsk.fusion.JointGeometry — built via
    /// <see cref="Component.CreateJointGeometryByPoint"/> (documented .NET shape, T8).
    /// </summary>
    public sealed class JointGeometry
    {
    }

    /// <summary>adsk.fusion.JointInput (reference add_joint: setAsRigidJointMotion for rigid).</summary>
    public sealed class JointInput
    {
        /// <summary>Reference add_joint: inp.setAsRigidJointMotion() when joint_type is rigid.</summary>
        public void SetAsRigidJointMotion() => throw new NotSupportedException("adsknet stub");
    }

    public sealed class AsBuiltJoint : IDeletableEntity
    {
        public string Name => throw new NotSupportedException("adsknet stub");
        public void DeleteMe() => throw new NotSupportedException("adsknet stub");
    }

    public sealed class AsBuiltJointCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public AsBuiltJoint Item(int index) => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.AsBuiltJointCollection.createInput(occ1, occ2, motion=None) (create_as_built_joint).</summary>
        public AsBuiltJointInput CreateInput(Occurrence componentOne, Occurrence componentTwo, object? motion)
            => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.AsBuiltJointCollection.add(input).</summary>
        public AsBuiltJoint Add(AsBuiltJointInput input) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.fusion.AsBuiltJointInput (create_as_built_joint passes None motion).</summary>
    public sealed class AsBuiltJointInput
    {
    }

    public sealed class RigidGroup : IDeletableEntity
    {
        public string Name => throw new NotSupportedException("adsknet stub");
        public void DeleteMe() => throw new NotSupportedException("adsknet stub");
    }

    public sealed class RigidGroupCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public RigidGroup Item(int index) => throw new NotSupportedException("adsknet stub");

        /// <summary>Reference create_rigid_group: rigidGroups.add(occs, includeChildren).</summary>
        public RigidGroup Add(ObjectCollection occurrences, bool includeChildren)
            => throw new NotSupportedException("adsknet stub");
    }

    // ── timeline / parameters ────────────────────────────────────────

    /// <summary>The feature timeline (reference: adsk.fusion.Timeline).</summary>
    public sealed class Timeline
    {
        public int Count => throw new NotSupportedException("adsknet stub");

        /// <summary>Timeline item i (a Feature).</summary>
        public Feature Item(int index) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A timeline item / feature result base (reference: adsk.fusion.Feature).</summary>
    public class Feature
    {
        public string Name => throw new NotSupportedException("adsknet stub");

        /// <summary>The entity the timeline item wraps (null when unknown).</summary>
        public Entity? Entity => throw new NotSupportedException("adsknet stub");

        /// <summary>Suppression flag (reference item.isSuppressed).</summary>
        public bool IsSuppressed { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>The feature's resulting bodies (reference feat.bodies — T7 FirstBody).</summary>
        public BRepBodyCollection Bodies => throw new NotSupportedException("adsknet stub");
    }

    public sealed class FeatureCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public Feature Item(int index) => throw new NotSupportedException("adsknet stub");

        public ExtrudeFeatureCollection ExtrudeFeatures => throw new NotSupportedException("adsknet stub");
        public RevolveFeatureCollection RevolveFeatures => throw new NotSupportedException("adsknet stub");
        public SweepFeatureCollection SweepFeatures => throw new NotSupportedException("adsknet stub");
        public LoftFeatureCollection LoftFeatures => throw new NotSupportedException("adsknet stub");
        public FilletFeatureCollection FilletFeatures => throw new NotSupportedException("adsknet stub");
        public ChamferFeatureCollection ChamferFeatures => throw new NotSupportedException("adsknet stub");
        public ShellFeatureCollection ShellFeatures => throw new NotSupportedException("adsknet stub");
        public MirrorFeatureCollection MirrorFeatures => throw new NotSupportedException("adsknet stub");
        public HoleFeatureCollection HoleFeatures => throw new NotSupportedException("adsknet stub");
        public RectangularPatternFeatureCollection RectangularPatternFeatures => throw new NotSupportedException("adsknet stub");
        public CircularPatternFeatureCollection CircularPatternFeatures => throw new NotSupportedException("adsknet stub");
        public ThreadFeatureCollection ThreadFeatures => throw new NotSupportedException("adsknet stub");
        public DraftFeatureCollection DraftFeatures => throw new NotSupportedException("adsknet stub");
        public SplitBodyFeatureCollection SplitBodyFeatures => throw new NotSupportedException("adsknet stub");
        public SplitFaceFeatureCollection SplitFaceFeatures => throw new NotSupportedException("adsknet stub");
        public OffsetFeatureCollection OffsetFeatures => throw new NotSupportedException("adsknet stub");
        public ScaleFeatureCollection ScaleFeatures => throw new NotSupportedException("adsknet stub");
        public MoveFeatureCollection MoveFeatures => throw new NotSupportedException("adsknet stub");
        public CombineFeatureCollection CombineFeatures => throw new NotSupportedException("adsknet stub");
        public PatchFeatureCollection PatchFeatures => throw new NotSupportedException("adsknet stub");
        public StitchFeatureCollection StitchFeatures => throw new NotSupportedException("adsknet stub");
        public ThickenFeatureCollection ThickenFeatures => throw new NotSupportedException("adsknet stub");
        public RuledSurfaceFeatureCollection RuledSurfaceFeatures => throw new NotSupportedException("adsknet stub");
        public TrimFeatureCollection TrimFeatures => throw new NotSupportedException("adsknet stub");
        public BaseFeatureCollection BaseFeatures => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.FlangeFeatureCollection (T7 sheet metal).</summary>
        public FlangeFeatureCollection FlangeFeatures => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.BendFeatureCollection (T7 sheet metal).</summary>
        public BendFeatureCollection BendFeatures => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.FlatPatternFeatureCollection (T7 sheet metal).</summary>
        public FlatPatternFeatureCollection FlatPatternFeatures => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.UnfoldFeatureCollection (T7 sheet metal).</summary>
        public UnfoldFeatureCollection UnfoldFeatures => throw new NotSupportedException("adsknet stub");

        /// <summary>Builds a sweep path from a sketch curve (reference features.createPath).</summary>
        public Path CreatePath(SketchCurve curve) => throw new NotSupportedException("adsknet stub");
    }

    // ── T7 sheet-metal feature collections ───────────────────────────

    public sealed class FlangeFeatureCollection
    {
        /// <summary>Reference create_flange: flangeFeatures.createInput(edge, True).</summary>
        public FlangeFeatureInput CreateInput(Edge edge, bool includeAdjacent)
            => throw new NotSupportedException("adsknet stub");

        public FlangeFeature Add(FlangeFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class FlangeFeatureInput
    {
        /// <summary>Flange angle (reference inp.angle = valueInput.createByAngle(deg)).</summary>
        public ValueInput Angle { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>Flange height (reference inp.height = valueInput.createByLength(cm)).</summary>
        public ValueInput Height { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>Optional bend radius (reference create_flange bend_radius).</summary>
        public ValueInput BendRadius { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    public sealed class FlangeFeature : Feature
    {
    }

    public sealed class BendFeatureCollection
    {
        /// <summary>Reference create_bend: bendFeatures.createInput(body, bendLine, True).</summary>
        public BendFeatureInput CreateInput(BRepBody body, SketchLine bendLine, bool includeAdjacent)
            => throw new NotSupportedException("adsknet stub");

        public BendFeature Add(BendFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class BendFeatureInput
    {
        /// <summary>Bend angle (reference inp.bendAngle = valueInput.createByAngle(deg)).</summary>
        public ValueInput BendAngle { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>Optional bend radius (reference create_bend bend_radius).</summary>
        public ValueInput BendRadius { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    public sealed class BendFeature : Feature
    {
    }

    public sealed class FlatPatternFeatureCollection
    {
        /// <summary>Reference create_flat_pattern: flatPatternFeatures.createInput(body).</summary>
        public FlatPatternFeatureInput CreateInput(BRepBody body) => throw new NotSupportedException("adsknet stub");

        public FlatPatternFeature Add(FlatPatternFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class FlatPatternFeatureInput
    {
    }

    public sealed class FlatPatternFeature : Feature
    {
    }

    public sealed class UnfoldFeatureCollection
    {
        /// <summary>Reference unfold: unfoldFeatures.createInput(bends, stationaryFace).</summary>
        public UnfoldFeatureInput CreateInput(ObjectCollection bends, Face stationaryFace)
            => throw new NotSupportedException("adsknet stub");

        public UnfoldFeature Add(UnfoldFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class UnfoldFeatureInput
    {
    }

    public sealed class UnfoldFeature : Feature
    {
    }

    /// <summary>The entity behind a timeline item; only its DeleteMe is used.</summary>
    public sealed class Entity
    {
        public string Name => throw new NotSupportedException("adsknet stub");
        public void DeleteMe() => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A user parameter (reference: adsk.fusion.Parameter).</summary>
    public sealed class Parameter
    {
        public string Name => throw new NotSupportedException("adsknet stub");

        /// <summary>The value in Fusion internal units (cm, radians).</summary>
        public double Value { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>The parameter expression (reference set_parameter applyByString path).</summary>
        public string? Expression { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
        public string? Unit => throw new NotSupportedException("adsknet stub");
        public string? Comment => throw new NotSupportedException("adsknet stub");

        /// <summary>Removes the user parameter (reference delete_parameter param.deleteMe()).</summary>
        public void DeleteMe() => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ParameterCollection
    {
        public int Count => throw new NotSupportedException("adsknet stub");
        public Parameter Item(int index) => throw new NotSupportedException("adsknet stub");

        /// <summary>adsk.fusion.ParameterCollection.itemByName (set_parameter / delete_parameter).</summary>
        public Parameter? ItemByName(string name) => throw new NotSupportedException("adsknet stub");

        /// <summary>
        /// adsk.fusion.ParameterCollection.add(name, valueInput, unit, comment)
        /// (create_parameter). <paramref name="value"/> is the ValueInput built the way
        /// the reference builds it: a raw real when no unit, "value unit" expression otherwise.
        /// </summary>
        public Parameter Add(string name, ValueInput value, string? unit, string? comment)
            => throw new NotSupportedException("adsknet stub");
    }

    // ── auto-constrain (preview API) ────────────────────────────────

    public sealed class AutoConstrainInput
    {
        /// <summary>One of <see cref="AutoConstrainResultTypes"/>.</summary>
        public int ResultOption { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    public sealed class AutoConstrainResult
    {
        public bool IsFullyConstrained => throw new NotSupportedException("adsknet stub");

        /// <summary>Number of constraints the auto-constrain pass added.</summary>
        public int AddedConstraints => throw new NotSupportedException("adsknet stub");

        /// <summary>Number of dimensions the auto-constrain pass added.</summary>
        public int AddedDimensions => throw new NotSupportedException("adsknet stub");

        /// <summary>Number of geometry entities moved (option 3).</summary>
        public int MovedGeometry => throw new NotSupportedException("adsknet stub");
    }

    // ── feature operations (T6) ──────────────────────────────────────

    /// <summary>Feature operations (reference adsk.fusion.FeatureOperations).</summary>
    public enum FeatureOperations
    {
        NewBodyFeatureOperation = 0,
        JoinFeatureOperation = 1,
        CutFeatureOperation = 2,
        IntersectFeatureOperation = 3,
    }

    /// <summary>Pattern distance type (reference adsk.fusion.PatternDistanceType).</summary>
    public enum PatternDistanceType
    {
        SpacingPatternDistanceType = 0,
        CenterPatternDistanceType = 1,
        ToCenterPatternDistanceType = 2,
    }

    /// <summary>Surface continuity types (reference adsk.fusion.SurfaceContinuityTypes).</summary>
    public enum SurfaceContinuityTypes
    {
        ConnectedSurfaceContinuityType = 0,
        TangentSurfaceContinuityType = 1,
        CurvatureSurfaceContinuityType = 2,
    }

    /// <summary>A base feature (direct-primitive wrapper).</summary>
    public sealed class BaseFeature
    {
        public string Name => throw new NotSupportedException("adsknet stub");
        public void StartEdit() => throw new NotSupportedException("adsknet stub");
        public void FinishEdit() => throw new NotSupportedException("adsknet stub");
    }

    public sealed class BaseFeatureCollection
    {
        public BaseFeature Add() => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A sweep path (reference root.features.createPath).</summary>
    public sealed class Path
    {
    }

    /// <summary>
    /// The temporary-BRep factory (reference adsk.fusion.TemporaryBRepManager):
    /// direct primitives that bypass the timeline.
    /// </summary>
    public sealed class TemporaryBRepManager
    {
        /// <summary>Reference TemporaryBRepManager.get().</summary>
        public static TemporaryBRepManager get() => throw new NotSupportedException("adsknet stub");

        public BRepBody CreateBox(OrientedBoundingBox3D orientedBox) => throw new NotSupportedException("adsknet stub");
        public BRepBody CreateCylinderOrCone(Point3D basePoint, double baseRadius, Point3D topPoint, double topRadius)
            => throw new NotSupportedException("adsknet stub");
        public BRepBody CreateSphere(Point3D center, double radius) => throw new NotSupportedException("adsknet stub");
        public BRepBody CreateTorus(Point3D center, Vector3D axis, double majorRadius, double minorRadius)
            => throw new NotSupportedException("adsknet stub");
    }

    // ── feature collections / inputs / features ───────────────────────

    public sealed class ExtrudeFeatureCollection
    {
        public ExtrudeFeatureInput CreateInput(Profile profile, FeatureOperations operation) =>
            throw new NotSupportedException("adsknet stub");
        public ExtrudeFeature Add(ExtrudeFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ExtrudeFeatureInput
    {
        public void SetDistanceExtent(bool reverse, ValueInput distance) => throw new NotSupportedException("adsknet stub");
        public void SetSymmetricExtent(ValueInput distance, bool mirror) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ExtrudeFeature : Feature
    {
    }

    public sealed class RevolveFeatureCollection
    {
        /// <summary>The axis entity may be a construction axis or a sketch construction line.</summary>
        public RevolveFeatureInput CreateInput(Profile profile, object axisEntity, FeatureOperations operation)
            => throw new NotSupportedException("adsknet stub");
        public RevolveFeature Add(RevolveFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class RevolveFeatureInput
    {
        public void SetAngleExtent(bool reverse, ValueInput angle) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class RevolveFeature : Feature
    {
    }

    public sealed class SweepFeatureCollection
    {
        public SweepFeatureInput CreateInput(Profile profile, Path path, FeatureOperations operation)
            => throw new NotSupportedException("adsknet stub");
        public SweepFeature Add(SweepFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class SweepFeatureInput
    {
    }

    public sealed class SweepFeature : Feature
    {
    }

    public sealed class LoftFeatureCollection
    {
        public LoftFeatureInput CreateInput(FeatureOperations operation) => throw new NotSupportedException("adsknet stub");
        public LoftFeature Add(LoftFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class LoftFeatureInput
    {
        public ProfileCollection LoftSections => throw new NotSupportedException("adsknet stub");
    }

    public sealed class LoftFeature : Feature
    {
    }

    public sealed class FilletFeatureCollection
    {
        public FilletFeatureInput CreateInput() => throw new NotSupportedException("adsknet stub");
        public FilletFeature Add(FilletFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class FilletFeatureInput
    {
        public void AddConstantRadiusEdgeSet(ObjectCollection edges, ValueInput radius, bool includeAdjacent)
            => throw new NotSupportedException("adsknet stub");
    }

    public sealed class FilletFeature : Feature
    {
    }

    public sealed class ChamferFeatureCollection
    {
        public ChamferFeatureInput CreateInput(ObjectCollection edges, bool includeAdjacent)
            => throw new NotSupportedException("adsknet stub");
        public ChamferFeature Add(ChamferFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ChamferFeatureInput
    {
        public void SetToEqualDistance(ValueInput distance) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ChamferFeature : Feature
    {
    }

    public sealed class ShellFeatureCollection
    {
        public ShellFeatureInput CreateInput(ObjectCollection bodies) => throw new NotSupportedException("adsknet stub");
        public ShellFeature Add(ShellFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ShellFeatureInput
    {
        public ObjectCollection FacesToRemove { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
        public ValueInput InsideThickness { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    public sealed class ShellFeature : Feature
    {
    }

    public sealed class MirrorFeatureCollection
    {
        public MirrorFeatureInput CreateInput(ObjectCollection entities, ConstructionPlane plane)
            => throw new NotSupportedException("adsknet stub");
        public MirrorFeature Add(MirrorFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class MirrorFeatureInput
    {
    }

    public sealed class MirrorFeature : Feature
    {
    }

    public sealed class HoleFeatureCollection
    {
        /// <summary>Reference createSimpleInput(diameter): the DIAMETER, not the radius.</summary>
        public HoleFeatureInput CreateSimpleInput(ValueInput diameter) => throw new NotSupportedException("adsknet stub");
        public HoleFeature Add(HoleFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class HoleFeatureInput
    {
        public void SetPositionBySketchPoint(SketchPoint point) => throw new NotSupportedException("adsknet stub");
        public void SetDistanceExtent(ValueInput depth) => throw new NotSupportedException("adsknet stub");

        /// <summary>Reference participantBodies — a plain list, not an ObjectCollection.</summary>
        public List<BRepBody> ParticipantBodies { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    public sealed class HoleFeature : Feature
    {

        /// <summary>The resolved hole diameter (ValueInput.value).</summary>
        public ValueInput HoleDiameter => throw new NotSupportedException("adsknet stub");

        /// <summary>Bodies the hole cut (reference feat.bodies).</summary>
        public new BRepBodyCollection Bodies => throw new NotSupportedException("adsknet stub");
    }

    public sealed class RectangularPatternFeatureCollection
    {
        public RectangularPatternFeatureInput CreateInput(
            ObjectCollection bodies,
            ConstructionAxis xAxis,
            ValueInput xCount,
            ValueInput xSpacing,
            PatternDistanceType distanceType)
            => throw new NotSupportedException("adsknet stub");
        public RectangularPatternFeature Add(RectangularPatternFeatureInput input) =>
            throw new NotSupportedException("adsknet stub");
    }

    public sealed class RectangularPatternFeatureInput
    {
        public void SetDirectionTwo(ConstructionAxis yAxis, ValueInput yCount, ValueInput ySpacing)
            => throw new NotSupportedException("adsknet stub");
    }

    public sealed class RectangularPatternFeature : Feature
    {
    }

    public sealed class CircularPatternFeatureCollection
    {
        public CircularPatternFeatureInput CreateInput(ObjectCollection bodies, ConstructionAxis axis)
            => throw new NotSupportedException("adsknet stub");
        public CircularPatternFeature Add(CircularPatternFeatureInput input) =>
            throw new NotSupportedException("adsknet stub");
    }

    public sealed class CircularPatternFeatureInput
    {
        public ValueInput Quantity { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
        public ValueInput TotalAngle { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    public sealed class CircularPatternFeature : Feature
    {
    }

    public sealed class ThreadFeatureCollection
    {
        /// <summary>Reference threads.threadDataQuery.</summary>
        public ThreadDataQuery ThreadDataQuery => throw new NotSupportedException("adsknet stub");

        public ThreadFeatureInput CreateInput(Face face, ThreadDataQuery data) =>
            throw new NotSupportedException("adsknet stub");
        public ThreadFeature Add(ThreadFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ThreadDataQuery
    {
        public string ThreadType { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    public sealed class ThreadFeatureInput
    {
        public bool IsModeled { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
        public bool IsFullLength { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
        public ValueInput ThreadLength { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    public sealed class ThreadFeature : Feature
    {
    }

    public sealed class DraftFeatureCollection
    {
        /// <summary>Reference createInput(face_array, pull_plane, is_tangent_chain).</summary>
        public DraftFeatureInput CreateInput(IReadOnlyList<Face> faces, ConstructionPlane pullPlane, bool isTangentChain)
            => throw new NotSupportedException("adsknet stub");
        public DraftFeature Add(DraftFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class DraftFeatureInput
    {
        public void SetSingleAngle(bool twoSided, ValueInput angle) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class DraftFeature : Feature
    {
    }

    public sealed class SplitBodyFeatureCollection
    {
        /// <summary>The tool is a body (splitting_body) or a construction plane (splitting_plane).</summary>
        public SplitBodyFeatureInput CreateInput(BRepBody body, BRepBody tool, bool extendTool)
            => throw new NotSupportedException("adsknet stub");
        public SplitBodyFeatureInput CreateInput(BRepBody body, ConstructionPlane plane, bool extendTool)
            => throw new NotSupportedException("adsknet stub");
        public SplitBodyFeature Add(SplitBodyFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class SplitBodyFeatureInput
    {
    }

    public sealed class SplitBodyFeature : Feature
    {
    }

    public sealed class SplitFaceFeatureCollection
    {
        public SplitFaceFeatureInput CreateInput(ObjectCollection faces, ConstructionPlane plane, bool extendTool)
            => throw new NotSupportedException("adsknet stub");
        public SplitFaceFeature Add(SplitFaceFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class SplitFaceFeatureInput
    {
    }

    public sealed class SplitFaceFeature : Feature
    {
    }

    public sealed class OffsetFeatureCollection
    {
        public OffsetFeatureInput CreateInput(ObjectCollection faces, ValueInput distance, FeatureOperations operation)
            => throw new NotSupportedException("adsknet stub");
        public OffsetFeature Add(OffsetFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class OffsetFeatureInput
    {
    }

    public sealed class OffsetFeature : Feature
    {
    }

    public sealed class ScaleFeatureCollection
    {
        public ScaleFeatureInput CreateInput(ObjectCollection bodies, Point3D anchor, ValueInput scale)
            => throw new NotSupportedException("adsknet stub");
        public ScaleFeatureInput CreateInput(ObjectCollection bodies, Point3D anchor, ValueInput scaleX, ValueInput scaleY, ValueInput scaleZ)
            => throw new NotSupportedException("adsknet stub");
        public ScaleFeature Add(ScaleFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ScaleFeatureInput
    {
    }

    public sealed class ScaleFeature : Feature
    {
    }

    public sealed class MoveFeatureCollection
    {
        public MoveFeatureInput CreateInput(ObjectCollection bodies, Matrix3D transform)
            => throw new NotSupportedException("adsknet stub");
        public MoveFeature Add(MoveFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class MoveFeatureInput
    {
    }

    public sealed class MoveFeature : Feature
    {
    }

    public sealed class CombineFeatureCollection
    {
        public CombineFeatureInput CreateInput(BRepBody target, ObjectCollection tools)
            => throw new NotSupportedException("adsknet stub");
        public CombineFeature Add(CombineFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class CombineFeatureInput
    {
        public FeatureOperations Operation { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    public sealed class CombineFeature : Feature
    {
    }

    public sealed class PatchFeatureCollection
    {
        public PatchFeatureInput CreateInput(Profile profile, FeatureOperations operation)
            => throw new NotSupportedException("adsknet stub");
        public PatchFeature Add(PatchFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class PatchFeatureInput
    {
        public SurfaceContinuityTypes BoundaryContinuity { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    public sealed class PatchFeature : Feature
    {
    }

    public sealed class StitchFeatureCollection
    {
        public StitchFeatureInput CreateInput(ObjectCollection bodies, ValueInput tolerance, FeatureOperations operation)
            => throw new NotSupportedException("adsknet stub");
        public StitchFeature Add(StitchFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class StitchFeatureInput
    {
    }

    public sealed class StitchFeature : Feature
    {
    }

    public sealed class ThickenFeatureCollection
    {
        /// <summary>Reference thickenFeatures.createInput(faces, thickness, False, op, symmetric).</summary>
        public ThickenFeatureInput CreateInput(
            ObjectCollection faces,
            ValueInput thickness,
            bool hollow,
            FeatureOperations operation,
            bool symmetric)
            => throw new NotSupportedException("adsknet stub");
        public ThickenFeature Add(ThickenFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class ThickenFeatureInput
    {
    }

    public sealed class ThickenFeature : Feature
    {
    }

    public sealed class RuledSurfaceFeatureCollection
    {
        public RuledSurfaceFeatureInput CreateInput(Edge edge, ValueInput distance)
            => throw new NotSupportedException("adsknet stub");
        public RuledSurfaceFeature Add(RuledSurfaceFeatureInput input) =>
            throw new NotSupportedException("adsknet stub");
    }

    public sealed class RuledSurfaceFeatureInput
    {
    }

    public sealed class RuledSurfaceFeature : Feature
    {
    }

    public sealed class TrimFeatureCollection
    {
        public TrimFeatureInput CreateInput(BRepBody body, BRepBody tool) =>
            throw new NotSupportedException("adsknet stub");
        public TrimFeature Add(TrimFeatureInput input) => throw new NotSupportedException("adsknet stub");
    }

    public sealed class TrimFeatureInput
    {
    }

    public sealed class TrimFeature : Feature
    {
    }
}
