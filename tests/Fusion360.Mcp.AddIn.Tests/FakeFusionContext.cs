using System;
using System.Collections.Generic;
using System.IO;
using Fusion360.Mcp.AddIn;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// In-memory IFusionContext for testing the T5 handlers without Fusion.
/// Records the mutations the handlers perform so tests can assert on them.
/// Interface members are implemented explicitly (no access modifier) so the
/// concrete fake types stay directly usable from test code.
/// </summary>
public sealed class FakeFusionContext : IFusionContext
{
    public FakeDesign Design { get; } = new();
    public FakeViewport? Viewport { get; set; } = new();
    public FakeUiCommands Ui { get; } = new();
    public int DoEventsCount { get; private set; }
    public SceneSnapshot? Snapshot { get; set; }

    /// <summary>
    /// P1: per-call snapshot sequence (CommandHandler takes before/after
    /// snapshots around a mutation). When non-empty, TakeSnapshot returns
    /// sequence items in call order (the last item sticks); otherwise the
    /// single <see cref="Snapshot"/> is returned (existing tests).
    /// </summary>
    public List<SceneSnapshot> SnapshotSequence { get; } = new();

    private int _snapshotCalls;

    // T7: always-available measure/material fakes + an optional CAM product
    // (null = "CAM product unavailable" handler error path).
    public FakeMeasureManager MeasureManagerImpl { get; } = new();
    public FakeMaterialLibraries MaterialLibrariesImpl { get; } = new();
    public FakeCamProduct? CamProduct { get; set; }

    IFusionDesign IFusionContext.Design => Design;
    IFusionViewport? IFusionContext.Viewport => Viewport;
    IUiCommands IFusionContext.Ui => Ui;
    IFusionMeasureManager IFusionContext.MeasureManager => MeasureManagerImpl;
    IFusionMaterialLibraries IFusionContext.MaterialLibraries => MaterialLibrariesImpl;
    IFusionCamProduct? IFusionContext.CamProduct => CamProduct;

    public void DoEvents() => DoEventsCount++;

    public SceneSnapshot? TakeSnapshot()
    {
        if (SnapshotSequence.Count > 0)
        {
            var index = Math.Min(_snapshotCalls, SnapshotSequence.Count - 1);
            _snapshotCalls++;
            return SnapshotSequence[index];
        }

        return Snapshot;
    }

}

// ------------------------------------------------------------------------
// Design
// ------------------------------------------------------------------------
public sealed class FakeDesign : IFusionDesign
{
    public string DocumentName { get; set; } = "Design1.fuse";
    public string DocumentIdentity { get; set; } = Guid.NewGuid().ToString("N");
    public string ProductType { get; set; } = "Design";
    public int DesignType { get; set; } = DesignTypes.Parametric;
    public FakeRootComponent RootComponent { get; } = new();
    public List<FakeParameter> UserParameterList { get; } = new();
    public FakeUserParameterCollection UserParameters { get; }
    public FakeAppearanceCollection AppearancesImpl { get; } = new();
    public FakeExportManager ExportManagerImpl { get; } = new();
    public FakeInterferenceInput? LastInterferenceInput { get; private set; }
    public IReadOnlyList<IFusionInterferenceResult>? InterferenceResults { get; set; }
    public FakeTimeline? Timeline { get; set; }

    public FakeDesign()
    {
        UserParameters = new FakeUserParameterCollection(UserParameterList);
    }

    string IFusionDesign.DocumentName => DocumentName;
    string IFusionDesign.DocumentIdentity => DocumentIdentity;
    string IFusionDesign.ProductType => ProductType;
    int IFusionDesign.DesignType
    {
        get => DesignType;
        set => DesignType = value;
    }
    IFusionRootComponent IFusionDesign.RootComponent => RootComponent;
    IFusionUserParameterCollection IFusionDesign.UserParameters => UserParameters;
    IFusionAppearanceCollection IFusionDesign.Appearances => AppearancesImpl;
    IFusionExportManager IFusionDesign.ExportManager => ExportManagerImpl;
    IFusionTimeline? IFusionDesign.Timeline => Timeline;

    IFusionInterferenceInput IFusionDesign.CreateInterferenceInput(IReadOnlyList<IFusionBody> bodies)
    {
        var input = new FakeInterferenceInput(bodies);
        LastInterferenceInput = input;
        return input;
    }

    IReadOnlyList<IFusionInterferenceResult>? IFusionDesign.AnalyzeInterference(IFusionInterferenceInput input)
        => InterferenceResults;
}

public sealed class FakeParameter : IFusionParameter
{
    public string Name { get; set; } = "";
    public double Value { get; set; }
    public string? Expression { get; set; }
    public string? Unit { get; set; }
    public string? Comment { get; set; }
}

public sealed class FakeTimeline : IFusionTimeline
{
    public List<FakeTimelineItem> Items { get; } = new();
    int IFusionTimeline.Count => Items.Count;
    IFusionTimelineItem IFusionTimeline.Item(int index) => Items[index];
}

public sealed class FakeTimelineItem : IFusionTimelineItem
{
    public string Name { get; set; } = "";
    public FakeTimelineEntity? Entity { get; set; }
    public bool IsSuppressed { get; set; }
    string IFusionTimelineItem.Name => Name;
    IFusionTimelineEntity? IFusionTimelineItem.Entity => Entity;
}

public sealed class FakeTimelineEntity : IFusionTimelineEntity
{
    public string Name { get; set; } = "";
    public bool Deleted { get; private set; }
    string IFusionTimelineEntity.Name => Name;
    public void DeleteMe() => Deleted = true;
}

// ------------------------------------------------------------------------
// Component / bodies / occurrences
// ------------------------------------------------------------------------
public sealed class FakeRootComponent : IFusionRootComponent
{
    public string Name { get; set; } = "Design";
    public List<FakeBody> Bodies { get; } = new();
    public List<FakeSketch> Sketches { get; } = new();
    public List<FakeOccurrence> OccurrenceList { get; } = new();
    public FakeOccurrences Occurrences { get; }
    public List<string> Features { get; } = new();

    // Test setup helpers: add an entity and wire its owner so DeleteMe
    // physically removes it (scene-wide commands see live counts).
    public FakeBody AddBody(FakeBody body)
    {
        body.Owner = Bodies;
        Bodies.Add(body);
        return body;
    }

    public FakeSketch AddSketch(FakeSketch sketch)
    {
        sketch.Owner = Sketches;
        Sketches.Add(sketch);
        return sketch;
    }

    public FakeOccurrence AddOccurrence(FakeOccurrence occ)
    {
        occ.Owner = OccurrenceList;
        OccurrenceList.Add(occ);
        return occ;
    }

    public FakeRigidGroupCollection RigidGroups { get; } = new();
    public FakeAsBuiltJointCollection AsBuiltJoints { get; } = new();
    public FakeJointCollection Joints { get; } = new();
    public FakeConstructionPlaneCollection ConstructionPlanes { get; } = new();
    public FakeConstructionAxisCollection ConstructionAxes { get; } = new();
    public FakeConstructionPlaneCollection ConstructionPoints { get; } = new();
    public FakeMeshBodyCollection MeshBodies { get; } = new();
    public FakeAnalysisCollection Analyses { get; } = new();
    public FakeUcsCollection UserCoordinateSystems { get; } = new();
    public FakeConstructionPoint OriginConstructionPoint { get; } = new();
    public IFusionAppearance? Appearance { get; set; }

    public FakeConstructionPlane XY { get; } = new("xy_plane");
    public FakeConstructionPlane YZ { get; } = new("yz_plane");
    public FakeConstructionPlane XZ { get; } = new("xz_plane");

    public FakeFeatures FeaturesImpl { get; } = new();
    public FakeConstructionAxis XAxis { get; } = new("x");
    public FakeConstructionAxis YAxis { get; } = new("y");
    public FakeConstructionAxis ZAxis { get; } = new("z");
    public FakeBRepManager BRepManagerImpl { get; } = new();

    public FakeConstructionPlane CreatedOffsetPlane { get; private set; } = null!;
    public int CreateOffsetPlaneCalls { get; private set; }
    public double? MassKg { get; set; }
    public FusionPoint? BBoxMin { get; set; }
    public FusionPoint? BBoxMax { get; set; }

    public IFusionSketchPoint? LastUcsGeometryPoint { get; private set; }
    public IFusionOccurrence? LastJointGeometryOccurrence { get; private set; }
    public IFusionConstructionPoint? LastJointGeometryPoint { get; private set; }

    public FakeRootComponent()
    {
        Occurrences = new FakeOccurrences(OccurrenceList);
    }

    string IFusionComponent.Name
    {
        get => Name;
        set => Name = value;
    }
    IFusionBRepBodyCollection IFusionComponent.BRepBodies => new FakeBRepBodies(Bodies);
    IFusionOccurrences IFusionComponent.Occurrences => Occurrences;
    IFusionOccurrenceCollection IFusionComponent.AllOccurrences => new FakeOccurrenceCollection(OccurrenceList);
    IFusionAppearance? IFusionComponent.Appearance
    {
        get => Appearance;
        set => Appearance = value;
    }
    IFusionConstructionPoint IFusionComponent.OriginConstructionPoint => OriginConstructionPoint;
    IFusionSketchCollection IFusionComponent.Sketches => new FakeSketchCollection(Sketches);
    IFusionFeatures IFusionComponent.Features => FeaturesImpl;
    IFusionConstructionPlaneCollection IFusionComponent.ConstructionPlanes => ConstructionPlanes;
    IFusionConstructionAxisCollection IFusionComponent.ConstructionAxes => ConstructionAxes;
    IFusionMeshBodyCollection IFusionComponent.MeshBodies => MeshBodies;
    double? IFusionComponent.PhysicalMassKg => MassKg;

    IFusionBoundingBox? IFusionComponent.BoundingBox
    {
        get
        {
            FusionPoint? min = BBoxMin, max = BBoxMax;
            return min is null || max is null ? null : new FakeBBox(min.Value, max.Value);
        }
    }

    int IFusionRootComponent.FeaturesCount => Features.Count;
    IFusionConstructionAxis IFusionRootComponent.XConstructionAxis => XAxis;
    IFusionConstructionAxis IFusionRootComponent.YConstructionAxis => YAxis;
    IFusionConstructionAxis IFusionRootComponent.ZConstructionAxis => ZAxis;
    IFusionBRepManager IFusionRootComponent.BRepManager => BRepManagerImpl;
    IFusionRigidGroupCollection IFusionRootComponent.RigidGroups => RigidGroups;
    IFusionAsBuiltJointCollection IFusionRootComponent.AsBuiltJoints => AsBuiltJoints;
    IFusionJointCollection IFusionRootComponent.Joints => Joints;
    IFusionDeletableCollection IFusionRootComponent.ConstructionPoints => ConstructionPoints;
    IFusionConstructionPlane IFusionRootComponent.XYConstructionPlane => XY;
    IFusionConstructionPlane IFusionRootComponent.YZConstructionPlane => YZ;
    IFusionConstructionPlane IFusionRootComponent.XZConstructionPlane => XZ;
    IFusionAnalysisCollection IFusionRootComponent.Analyses => Analyses;
    IFusionUcsCollection IFusionRootComponent.UserCoordinateSystems => UserCoordinateSystems;

    IFusionUcsGeometry IFusionRootComponent.CreateUcsGeometryAtPoint(IFusionSketchPoint point)
    {
        LastUcsGeometryPoint = point;
        return new FakeUcsGeometry(point);
    }

    IFusionJointGeometry IFusionRootComponent.CreateJointGeometryByPoint(IFusionOccurrence occurrence, IFusionConstructionPoint point)
    {
        LastJointGeometryOccurrence = occurrence;
        LastJointGeometryPoint = point;
        return new FakeJointGeometry { Occurrence = occurrence, Point = point };
    }

    public IFusionConstructionPlane CreateOffsetPlane(IFusionConstructionPlane basePlane, double offsetCm)
    {
        CreateOffsetPlaneCalls++;
        var plane = new FakeConstructionPlane($"{basePlane.Name}_offset_{offsetCm}");
        CreatedOffsetPlane = plane;
        ConstructionPlanes.Planes.Add(plane);
        return plane;
    }
}

public sealed class FakeComponent : IFusionComponent
{
    public string Name { get; set; } = "Component";
    public List<FakeBody> BodyList { get; } = new();
    public List<FakeOccurrence> OccList { get; } = new();
    public List<FakeOccurrence> OccurrencesList { get; } = new();
    public FakeOccurrences Occurrences { get; }
    public List<FakeSketch> SketchList { get; } = new();
    public FakeFeatures FeaturesImpl { get; } = new();
    public FakeConstructionPlaneCollection ConstructionPlanes { get; } = new();
    public FakeConstructionAxisCollection ConstructionAxes { get; } = new();
    public FakeMeshBodyCollection MeshBodies { get; } = new();
    public IFusionAppearance? Appearance { get; set; }
    public FakeConstructionPoint OriginConstructionPoint { get; } = new();

    public FakeComponent()
    {
        Occurrences = new FakeOccurrences(OccurrencesList);
    }

    string IFusionComponent.Name
    {
        get => Name;
        set => Name = value;
    }
    IFusionBRepBodyCollection IFusionComponent.BRepBodies => new FakeBRepBodies(BodyList);
    IFusionOccurrences IFusionComponent.Occurrences => Occurrences;
    IFusionOccurrenceCollection IFusionComponent.AllOccurrences => new FakeOccurrenceCollection(OccList);
    IFusionAppearance? IFusionComponent.Appearance
    {
        get => Appearance;
        set => Appearance = value;
    }
    IFusionConstructionPoint IFusionComponent.OriginConstructionPoint => OriginConstructionPoint;
    IFusionSketchCollection IFusionComponent.Sketches => new FakeSketchCollection(SketchList);
    IFusionFeatures IFusionComponent.Features => FeaturesImpl;
    IFusionConstructionPlaneCollection IFusionComponent.ConstructionPlanes => ConstructionPlanes;
    IFusionConstructionAxisCollection IFusionComponent.ConstructionAxes => ConstructionAxes;
    IFusionMeshBodyCollection IFusionComponent.MeshBodies => MeshBodies;
    double? IFusionComponent.PhysicalMassKg => null;
    IFusionBoundingBox? IFusionComponent.BoundingBox => null;
}

public sealed class FakeBody : IFusionBody
{
    public string Name { get; set; } = "";
    public double Volume { get; set; }
    public double Area { get; set; }
    public string? MaterialName { get; set; }
    public bool IsVisible { get; set; } = true;
    public int FacesCount { get; set; }
    public int EdgesCount { get; set; }
    public int VerticesCount { get; set; }
    public IFusionBoundingBox? BoundingBox { get; set; }
    public string EntityToken { get; } = $"tok-{Guid.NewGuid():N}";
    public IFusionOccurrence? AssemblyContext { get; set; }
    public IFusionAppearance? Appearance { get; set; }
    public List<FakeBodyEdge> EdgeList { get; } = new();
    public List<FakeFace> FaceList { get; } = new();
    public bool AddedUnderBaseFeature { get; set; }
    public bool Deleted { get; private set; }

    /// <summary>When set, <see cref="GetPhysicalProperties"/> returns it; otherwise a
    /// default (MassKg=1, VolumeCm3=Volume, AreaCm2=Area, Density=1, center (0,0,0)).</summary>
    public FakePhysicalProperties? PhysicalPropertiesOverride { get; set; }
    public List<FusionCalculationAccuracy> GetPhysicalPropertiesCalls { get; } = new();

    internal List<FakeBody>? Owner { get; set; }

    string IFusionBody.Name
    {
        get => Name;
        set => Name = value;
    }
    double IFusionBody.Volume => Volume;
    double IFusionBody.Area => Area;
    string? IFusionBody.MaterialName => MaterialName;
    bool IFusionBody.IsVisible
    {
        get => IsVisible;
        set => IsVisible = value;
    }
    int IFusionBody.FacesCount => FacesCount;
    int IFusionBody.EdgesCount => EdgesCount;
    int IFusionBody.VerticesCount => VerticesCount;
    IFusionBoundingBox? IFusionBody.BoundingBox => BoundingBox;
    string IFusionBody.EntityToken => EntityToken;
    IFusionOccurrence? IFusionBody.AssemblyContext => AssemblyContext;
    IFusionAppearance? IFusionBody.Appearance
    {
        get => Appearance;
        set => Appearance = value;
    }
    IReadOnlyList<IFusionBodyEdge> IFusionBody.Edges => EdgeList;
    IReadOnlyList<IFusionFace> IFusionBody.Faces => FaceList;

    public IFusionPhysicalProperties GetPhysicalProperties(FusionCalculationAccuracy accuracy)
    {
        GetPhysicalPropertiesCalls.Add(accuracy);
        if (PhysicalPropertiesOverride is { } @override)
        {
            return @override;
        }

        return new FakePhysicalProperties
        {
            MassKg = 1,
            VolumeCm3 = Volume,
            AreaCm2 = Area,
            Density = 1,
            CenterOfMass = new FusionPoint(0, 0, 0),
        };
    }

    public void DeleteMe()
    {
        if (Deleted)
        {
            return;
        }

        Deleted = true;
        Owner?.Remove(this);
    }
}

public sealed class FakeBodyEdge : IFusionBodyEdge
{
    public string Label { get; set; } = "";
    public FusionPoint PointOnEdge { get; set; }
    public FusionPoint StartVertex { get; set; }
    public FusionPoint EndVertex { get; set; }

    FusionPoint IFusionBodyEdge.PointOnEdge => PointOnEdge;
    FusionPoint IFusionBodyEdge.StartVertex => StartVertex;
    FusionPoint IFusionBodyEdge.EndVertex => EndVertex;
}

public sealed class FakePlane : IFusionPlane
{
    public FusionPoint Normal { get; set; }
    public FusionPoint Origin { get; set; }
}

public sealed class FakeFace : IFusionFace
{
    public string Label { get; set; } = "";
    public double Area { get; set; }
    public IFusionBoundingBox? BoundingBox { get; set; }
    public FusionPoint PointOnFace { get; set; }
    public IFusionPlane? PlaneGeometry { get; set; }
    public IFusionAppearance? Appearance { get; set; }
    public List<FusionPoint> Normals { get; } = new();

    (bool Ok, FusionPoint Normal) IFusionFace.GetNormalAtPoint(FusionPoint point)
        => (Normals.Count > 0, Normals.Count > 0 ? Normals[0] : default);

    double IFusionFace.Area => Area;
    IFusionBoundingBox? IFusionFace.BoundingBox => BoundingBox;
    FusionPoint IFusionFace.PointOnFace => PointOnFace;
    IFusionPlane? IFusionFace.PlaneGeometry => PlaneGeometry;
    IFusionAppearance? IFusionFace.Appearance
    {
        get => Appearance;
        set => Appearance = value;
    }
}

public sealed class FakeOccurrence : IFusionOccurrence
{
    public string Name { get; set; } = "occ";
    public FakeComponent Component { get; set; } = new();
    public bool IsVisible { get; set; } = true;
    public IFusionBoundingBox? BoundingBox { get; set; }
    public bool Deleted { get; private set; }

    /// <summary>Bodies the occurrence exposes (check_interference / export_view_sheet scene bounds).</summary>
    public List<FakeBody> BodyList { get; } = new();

    internal List<FakeOccurrence>? Owner { get; set; }

    internal void MarkDeleted() => Deleted = true;

    IFusionComponent IFusionOccurrence.Component => Component;
    bool IFusionOccurrence.IsVisible => IsVisible;
    IFusionBoundingBox? IFusionOccurrence.BoundingBox => BoundingBox;
    IFusionBRepBodyCollection IFusionOccurrence.BRepBodies => new FakeBRepBodies(BodyList);

    public void DeleteMe()
    {
        if (Deleted)
        {
            return;
        }

        Deleted = true;
        Owner?.Remove(this);
    }
}

// ------------------------------------------------------------------------
// Collections
// ------------------------------------------------------------------------
public sealed class FakeBRepBodies : IFusionBRepBodyCollection
{
    private readonly List<FakeBody> _bodies;
    public FakeBRepBodies(List<FakeBody> bodies) => _bodies = bodies;
    public int Count => _bodies.Count;
    public IFusionBody Item(int index) => _bodies[index];

    public void Add(IFusionBody body, IFusionBaseFeature baseFeature)
    {
        // The wrapper is created per access, so record on the body itself.
        ((FakeBody)body).AddedUnderBaseFeature = true;
        _bodies.Add((FakeBody)body);
    }
}

public sealed class FakeOccurrenceCollection : IFusionOccurrenceCollection
{
    private readonly List<FakeOccurrence> _occs;
    public FakeOccurrenceCollection(List<FakeOccurrence> occs) => _occs = occs;
    public int Count => _occs.Count;
    public IFusionOccurrence Item(int index) => _occs[index];
}

public sealed class FakeDeletableCollection : IFusionDeletableCollection
{
    public List<IFusionDeletable> Items { get; } = new();
    public int Count => Items.Count;
    public IFusionDeletable Item(int index) => Items[index];
}

public sealed class FakeDeletable : IFusionDeletable
{
    public string Name { get; set; } = "";
    public bool Deleted { get; private set; }
    public void DeleteMe() => Deleted = true;
}

/// <summary>Deletable view over the root's occurrence list (occurrences are deletable via deleteMe).</summary>
public sealed class FakeOccurrenceDeletableCollection : IFusionDeletableCollection
{
    private readonly List<FakeOccurrence> _occs;
    public FakeOccurrenceDeletableCollection(List<FakeOccurrence> occs) => _occs = occs;
    public int Count => _occs.Count;
    public IFusionDeletable Item(int index) => new FakeDeletableOccurrence(_occs[index]);
}

public sealed class FakeDeletableOccurrence : IFusionDeletable
{
    private readonly FakeOccurrence _occ;
    public FakeDeletableOccurrence(FakeOccurrence occ) => _occ = occ;
    public string Name => _occ.Name;
    public void DeleteMe() => _occ.DeleteMe();
}

public sealed class FakeConstructionPlaneCollection : IFusionConstructionPlaneCollection
{
    public List<FakeConstructionPlane> Planes { get; } = new();
    private int _addedCount;
    public int Count => Planes.Count;
    public IFusionDeletable Item(int index) => Planes[index];

    public IFusionConstructionPlaneInput CreateInput() => new FakeConstructionPlaneInput();

    public IFusionConstructionPlane Add(IFusionConstructionPlaneInput input)
    {
        _addedCount++;
        var plane = new FakeConstructionPlane($"CPlane{_addedCount}");
        Planes.Add(plane);
        return plane;
    }
}

public sealed class FakeConstructionPlaneInput : IFusionConstructionPlaneInput
{
    /// <summary>Which SetBy* was called ("offset"/"angle"/"two_planes"/"three_points").</summary>
    public string? SetBy { get; private set; }
    public IFusionConstructionPlane? BasePlane { get; private set; }
    public IFusionConstructionAxis? Axis { get; private set; }
    public double? OffsetCm { get; private set; }
    public double? AngleDegrees { get; private set; }
    public IFusionConstructionPlane? PlaneTwo { get; private set; }
    public IReadOnlyList<FusionPoint>? Points { get; private set; }

    public void SetByOffset(IFusionConstructionPlane basePlane, double offsetCm)
    {
        SetBy = "offset";
        BasePlane = basePlane;
        OffsetCm = offsetCm;
    }

    public void SetByAngle(IFusionConstructionAxis axis, double angleDegrees, IFusionConstructionPlane basePlane)
    {
        SetBy = "angle";
        Axis = axis;
        AngleDegrees = angleDegrees;
        BasePlane = basePlane;
    }

    public void SetByTwoPlanes(IFusionConstructionPlane one, IFusionConstructionPlane two)
    {
        SetBy = "two_planes";
        BasePlane = one;
        PlaneTwo = two;
    }

    public void SetByThreePoints(FusionPoint p1, FusionPoint p2, FusionPoint p3)
    {
        SetBy = "three_points";
        Points = new[] { p1, p2, p3 };
    }
}

public sealed class FakeConstructionPlane : IFusionConstructionPlane, IFusionDeletable
{
    public FakeConstructionPlane(string name) => Name = name;
    public string Name { get; }
    public bool Deleted { get; private set; }
    public void DeleteMe() => Deleted = true;
}

public sealed class FakeBBox : IFusionBoundingBox
{
    public FakeBBox(FusionPoint min, FusionPoint max)
    {
        Min = min;
        Max = max;
    }

    public FusionPoint Min { get; }
    public FusionPoint Max { get; }
}

// ------------------------------------------------------------------------
// Sketch
// ------------------------------------------------------------------------
public sealed class FakeSketchCollection : IFusionSketchCollection
{
    private readonly List<FakeSketch> _sketches;
    public FakeSketchCollection(List<FakeSketch> sketches) => _sketches = sketches;
    public int Count => _sketches.Count;
    public IFusionSketch Item(int index) => _sketches[index];

    public IFusionSketch Add(IFusionConstructionPlane plane)
    {
        var sketch = new FakeSketch { Name = "Sketch1", HostPlane = plane.Name };
        sketch.Owner = _sketches;
        _sketches.Add(sketch);
        return sketch;
    }

    public int AddOnFaceCalls { get; private set; }
    public IFusionFace? LastFace { get; private set; }

    public IFusionSketch AddOnFace(IFusionFace face)
    {
        AddOnFaceCalls++;
        LastFace = face;
        var sketch = new FakeSketch { Name = "Sketch1", HostPlane = "face" };
        sketch.Owner = _sketches;
        _sketches.Add(sketch);
        return sketch;
    }
}

public sealed class FakeSketch : IFusionSketch
{
    public string Name { get; set; } = "Sketch1";
    public bool IsVisible { get; set; } = true;
    public int ProfilesCount { get; set; }
    public string? HostPlane { get; set; }

    public List<FakeSketchCurve> CurvesList { get; } = new();
    public FakeSketchCurveCollections CurvesImpl { get; private set; } = null!;
    public FakeConstraints ConstraintsImpl { get; private set; } = null!;
    public FakeDimensions DimensionsImpl { get; private set; } = null!;
    public List<FakeSketchPoint> PointsList { get; } = new();
    public FakeProfileCollection ProfilesImpl { get; } = new();
    public List<IFusionSketchPoint> AddedPoints { get; } = new();

    public FakeSketch()
    {
        CurvesImpl = new FakeSketchCurveCollections(this);
        ConstraintsImpl = new FakeConstraints(this);
        DimensionsImpl = new FakeDimensions(this);
    }

    public int OffsetCalls { get; private set; }
    public IFusionSketchCurve? LastOffsetCurve { get; private set; }
    public double LastOffsetDistance { get; private set; }
    public int ProjectedCount { get; private set; }
    public string? AutoConstrainError { get; set; }
    public IFusionAutoConstrainResult? AutoConstrainResult { get; set; }
    public int AutoConstrainOption { get; private set; }
    public bool Deleted { get; private set; }

    internal List<FakeSketch>? Owner { get; set; }

    string IFusionSketch.Name
    {
        get => Name;
        set => Name = value;
    }
    bool IFusionSketch.IsVisible
    {
        get => IsVisible;
        set => IsVisible = value;
    }
    int IFusionSketch.ProfilesCount => ProfilesCount;
    IFusionSketchCurveCollections IFusionSketch.Curves => CurvesImpl;
    IReadOnlyList<IFusionSketchCurve> IFusionSketch.SketchCurves => CurvesList;
    IFusionGeometricConstraints IFusionSketch.Constraints => ConstraintsImpl;
    IFusionSketchDimensions IFusionSketch.Dimensions => DimensionsImpl;
    IFusionSketchPointCollection IFusionSketch.Points => new FakePointCollection(PointsList);
    IFusionProfileCollection IFusionSketch.Profiles => ProfilesImpl;

    public IFusionSketchPoint AddPointInModelSpace(FusionPoint modelPoint)
    {
        var point = new FakeSketchPoint();
        AddedPoints.Add(point);
        PointsList.Add(point);
        return point;
    }

    public void Offset(IFusionSketchCurve curve, FusionPoint direction, double distanceCm)
    {
        OffsetCalls++;
        LastOffsetCurve = curve;
        LastOffsetDistance = distanceCm;
    }

    public int ProjectEdge(IFusionBodyEdge edge)
    {
        ProjectedCount += 3;
        return 3;
    }

    public IFusionAutoConstrainResult? AutoConstrain(int resultOption)
    {
        AutoConstrainOption = resultOption;
        if (AutoConstrainError is not null)
        {
            throw new FusionCommandException(AutoConstrainError);
        }

        return AutoConstrainResult;
    }

    public void DeleteMe()
    {
        if (Deleted)
        {
            return;
        }

        Deleted = true;
        Owner?.Remove(this);
    }
}

public sealed class FakeSketchPoint : IFusionSketchPoint
{
}

public sealed class FakePointCollection : IFusionSketchPointCollection
{
    private readonly List<FakeSketchPoint> _points;
    public FakePointCollection(List<FakeSketchPoint> points) => _points = points;
    public IFusionSketchPoint Item(int index) => _points[index];
}

public sealed class FakeSketchCurve : IFusionSketchCurve
{
    public string Label { get; set; } = "";
    public FakeSketchPoint StartPoint { get; } = new();
    public FakeSketchPoint EndPoint { get; } = new();
    public int TrimCalls { get; private set; }
    public int ExtendCalls { get; private set; }
    public bool IsConstruction { get; private set; }

    /// <summary>P1 (ADR-0009): curve-kind tag; FakeSketchCurveCollections maps the label onto it.</summary>
    public string CurveKind { get; set; } = "line";

    IFusionSketchPoint IFusionSketchCurve.StartSketchPoint => StartPoint;
    IFusionSketchPoint IFusionSketchCurve.EndSketchPoint => EndPoint;
    public void MarkAsConstruction() => IsConstruction = true;
    public void Trim(FusionPoint point) => TrimCalls++;
    public void Extend(FusionPoint point) => ExtendCalls++;
}

public sealed class FakeSketchCurveCollections : IFusionSketchCurveCollections
{
    private readonly FakeSketch _sketch;
    public FakeLineCollection Lines { get; }
    public FakeCircleCollection Circles { get; }
    public FakeArcCollection Arcs { get; }
    public FakeFittedSplineCollection FittedSplines { get; }
    public FakeControlPointSplineCollection ControlPointSplines { get; }

    public FakeSketchCurveCollections(FakeSketch sketch)
    {
        _sketch = sketch;
        Lines = new FakeLineCollection(this, sketch);
        Circles = new FakeCircleCollection(this, sketch);
        Arcs = new FakeArcCollection(this, sketch);
        FittedSplines = new FakeFittedSplineCollection(this, sketch);
        ControlPointSplines = new FakeControlPointSplineCollection(this, sketch);
    }

    IFusionSketchLineCollection IFusionSketchCurveCollections.Lines => Lines;
    IFusionSketchCircleCollection IFusionSketchCurveCollections.Circles => Circles;
    IFusionSketchArcCollection IFusionSketchCurveCollections.Arcs => Arcs;
    IFusionSketchFittedSplineCollection IFusionSketchCurveCollections.FittedSplines => FittedSplines;
    IFusionSketchControlPointSplineCollection IFusionSketchCurveCollections.ControlPointSplines => ControlPointSplines;

    internal FakeSketchCurve Add(string label)
    {
        var curve = new FakeSketchCurve { Label = label, CurveKind = CurveKindOf(label) };
        _sketch.CurvesList.Add(curve);
        return curve;
    }

    /// <summary>The fake's creation label onto the P1 curve-kind tag (P1 verification profile detection).</summary>
    private static string CurveKindOf(string label) => label switch
    {
        "circle" => "circle",
        "arc" => "arc",
        "fitted_spline" => "spline",
        "control_spline" => "spline",
        _ => "line", // "line", "rectangle", "rectangle_bottom", "rectangle_right"
    };
}

public sealed class FakeLineCollection : IFusionSketchLineCollection
{
    private readonly FakeSketchCurveCollections _parent;
    private readonly FakeSketch _sketch;

    /// <summary>Lines created via <see cref="AddByTwoPoints"/> /
    /// <see cref="AddTwoPointRectangleLines"/>, in creation order.</summary>
    public List<FakeSketchCurve> Created { get; } = new();

    public FakeLineCollection(FakeSketchCurveCollections parent, FakeSketch sketch)
    {
        _parent = parent;
        _sketch = sketch;
    }

    public IFusionSketchCurve AddTwoPointRectangle(FusionPoint p1, FusionPoint p2)
    {
        var curve = _parent.Add("rectangle");
        BumpProfiles();
        return curve;
    }

    public IFusionSketchCurve AddByTwoPoints(FusionPoint p1, FusionPoint p2)
    {
        var line = _parent.Add("line");
        Created.Add(line);
        return line;
    }

    public (IFusionSketchCurve Bottom, IFusionSketchCurve Right) AddTwoPointRectangleLines(FusionPoint p1, FusionPoint p2)
    {
        var bottom = _parent.Add("rectangle_bottom");
        var right = _parent.Add("rectangle_right");
        Created.Add(bottom);
        Created.Add(right);
        BumpProfiles();
        return (bottom, right);
    }

    IFusionSketchCurve IFusionSketchLineCollection.Item(int index) => Created[index];

    /// <summary>A closed two-point rectangle becomes one new profile, like adsk's auto-profiles.</summary>
    private void BumpProfiles()
    {
        var profile = new FakeProfile { Index = _sketch.ProfilesImpl.Profiles.Count };
        _sketch.ProfilesImpl.Profiles.Add(profile);
        _sketch.ProfilesCount = _sketch.ProfilesImpl.Profiles.Count;
    }
}

public sealed class FakeCircleCollection : IFusionSketchCircleCollection
{
    private readonly FakeSketchCurveCollections _parent;
    public FakeCircleCollection(FakeSketchCurveCollections parent, FakeSketch sketch) => _parent = parent;
    public IFusionSketchCurve AddByCenterRadius(FusionPoint center, double radiusCm) => _parent.Add("circle");
}

public sealed class FakeArcCollection : IFusionSketchArcCollection
{
    private readonly FakeSketchCurveCollections _parent;
    public FakeArcCollection(FakeSketchCurveCollections parent, FakeSketch sketch) => _parent = parent;
    public IFusionSketchCurve AddByCenterStartSweep(FusionPoint center, FusionPoint start, double sweepRadians)
        => _parent.Add("arc");
}

public sealed class FakeFittedSplineCollection : IFusionSketchFittedSplineCollection
{
    private readonly FakeSketchCurveCollections _parent;
    public FakeFittedSplineCollection(FakeSketchCurveCollections parent, FakeSketch sketch) => _parent = parent;
    public IFusionSketchCurve Add(IReadOnlyList<FusionPoint> points) => _parent.Add("fitted_spline");
}

public sealed class FakeControlPointSplineCollection : IFusionSketchControlPointSplineCollection
{
    private readonly FakeSketchCurveCollections _parent;
    public FakeControlPointSplineCollection(FakeSketchCurveCollections parent, FakeSketch sketch) => _parent = parent;
    public IFusionSketchCurve Add(IReadOnlyList<FusionPoint> controlPoints, int degree) => _parent.Add("control_spline");
}

public sealed class FakeConstraints : IFusionGeometricConstraints
{
    private readonly FakeSketch _sketch;
    public FakeConstraints(FakeSketch sketch) => _sketch = sketch;
    public List<string> Added { get; } = new();

    public void AddCoincident(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => Added.Add("coincident");
    public void AddParallel(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => Added.Add("parallel");
    public void AddPerpendicular(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => Added.Add("perpendicular");
    public void AddTangent(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => Added.Add("tangent");
    public void AddEqual(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => Added.Add("equal");
    public void AddFix(IFusionSketchCurve? e1) => Added.Add("fix");
    public void AddHorizontal(IFusionSketchCurve? e1) => Added.Add("horizontal");
    public void AddVertical(IFusionSketchCurve? e1) => Added.Add("vertical");
    public void AddConcentric(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => Added.Add("concentric");
    public void AddCollinear(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => Added.Add("collinear");
    public void AddSmooth(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => Added.Add("smooth");
    public void AddMidPoint(IFusionSketchPoint point, IFusionSketchCurve? e2) => Added.Add("midpoint");
    public void AddSymmetry(IFusionSketchCurve? e1, IFusionSketchCurve? e2, IFusionSketchCurve symmetryLine)
        => Added.Add("symmetry");
}

public sealed class FakeDimensions : IFusionSketchDimensions
{
    private readonly FakeSketch _sketch;
    public FakeDimensions(FakeSketch sketch) => _sketch = sketch;
    public List<FakeDimension> Created { get; } = new();

    private FakeDimension Make(string type)
    {
        var d = new FakeDimension { Type = type };
        Created.Add(d);
        return d;
    }

    public IFusionSketchDimension AddDistanceDimension(IFusionSketchCurve e1, IFusionSketchCurve e2, int orientation, FusionPoint textPoint)
        => Make($"distance:{orientation}");
    public IFusionSketchDimension AddEndpointDistanceDimension(IFusionSketchCurve line, int orientation, FusionPoint textPoint)
        => Make($"endpoint_distance:{orientation}");
    public IFusionSketchDimension AddAngularDimension(IFusionSketchCurve e1, IFusionSketchCurve e2, FusionPoint textPoint)
        => Make("angular");
    public IFusionSketchDimension AddRadialDimension(IFusionSketchCurve e1, FusionPoint textPoint)
        => Make("radial");
    public IFusionSketchDimension AddDiameterDimension(IFusionSketchCurve e1, FusionPoint textPoint)
        => Make("diameter");
}

public sealed class FakeDimension : IFusionSketchDimension
{
    public string Type { get; set; } = "";
    public double? ParameterValue { get; private set; }
    public string? ParameterExpression { get; private set; }
    public void SetParameterValue(double valueCm) => ParameterValue = valueCm;
    public void SetParameterExpression(string expression) => ParameterExpression = expression;
}

public sealed class FakeAutoConstrainResult : IFusionAutoConstrainResult
{
    public bool IsFullyConstrained { get; set; }
    public int AddedConstraints { get; set; }
    public int AddedDimensions { get; set; }
    public int MovedGeometry { get; set; }
}

// ------------------------------------------------------------------------
// Viewport / camera
// ------------------------------------------------------------------------
public sealed class FakeViewport : IFusionViewport
{
    public FakeCamera CameraImpl { get; set; } = new();
    public int FitCalls { get; private set; }
    public int RefreshCalls { get; private set; }
    public int SaveAsImageCalls { get; private set; }
    public bool SaveAsImageReturns { get; set; } = true;
    public string? LastSavedPath { get; private set; }

    IFusionCamera IFusionViewport.Camera
    {
        get => CameraImpl;
        set => CameraImpl = value as FakeCamera ?? new FakeCamera { Eye = new FusionPoint(0, 0, 0), Target = new FusionPoint(0, 0, 0), UpVector = new FusionPoint(0, 0, 1) };
    }

    public void Fit() => FitCalls++;
    public void Refresh() => RefreshCalls++;

    public bool SaveAsImageFile(string path, int width, int height)
    {
        SaveAsImageCalls++;
        LastSavedPath = path;
        if (!SaveAsImageReturns)
        {
            return false;
        }

        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return true;
    }
}

public sealed class FakeCamera : IFusionCamera
{
    public FusionPoint Eye { get; set; }
    public FusionPoint Target { get; set; }
    public FusionPoint UpVector { get; set; }
    public int CameraType { get; set; }
    public bool IsSmoothTransition { get; set; }
    public bool IsFitView { get; set; }
}

// ------------------------------------------------------------------------
// UI commands
// ------------------------------------------------------------------------
public sealed class FakeUiCommands : IUiCommands
{
    public Dictionary<string, FakeUiCommand> Commands { get; } = new();
    public IUiCommandDefinition? ItemById(string id) => Commands.TryGetValue(id, out var c) ? c : null;
}

public sealed class FakeUiCommand : IUiCommandDefinition
{
    public int ExecuteCount { get; private set; }
    public Exception? ToThrow { get; set; }
    public void Execute()
    {
        ExecuteCount++;
        if (ToThrow is not null)
        {
            throw ToThrow;
        }
    }
}
