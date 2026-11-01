namespace Fusion360.Mcp.AddIn;

/// <summary>A component (root or child) of the design.</summary>
public interface IFusionComponent
{
    string Name { get; set; }
    IFusionBRepBodyCollection BRepBodies { get; }
    IFusionOccurrenceCollection AllOccurrences { get; }

    /// <summary>Total physical mass of this component (kg); null when unreadable.</summary>
    double? PhysicalMassKg { get; }

    /// <summary>Axis-aligned bounding box in world space; null when empty.</summary>
    IFusionBoundingBox? BoundingBox { get; }

    /// <summary>
    /// Occurrences contained in this component (adsk.fusion.OccurrenceCollection),
    /// upgraded for T7: addNewComponent (create_component) + delete_all sweeps.
    /// </summary>
    IFusionOccurrences Occurrences { get; }

    /// <summary>adsk.fusion.Component.appearance — null when no appearance is assigned.</summary>
    IFusionAppearance? Appearance { get; set; }

    /// <summary>adsk.fusion.Component.originConstructionPoint (joint anchors).</summary>
    IFusionConstructionPoint OriginConstructionPoint { get; }

    /// <summary>
    /// Sketches hosted by this component (root or child — create_box_parametric's
    /// component_name path uses a child component's sketches).
    /// </summary>
    IFusionSketchCollection Sketches { get; }

    /// <summary>Features hosted by this component (create_box_parametric on a child component).</summary>
    IFusionFeatures Features { get; }

    /// <summary>adsk.fusion.Component.constructionPlanes, upgraded for T7 (create_input/add).</summary>
    IFusionConstructionPlaneCollection ConstructionPlanes { get; }

    /// <summary>adsk.fusion.Component.constructionAxes, upgraded for T7 (create_input/add).</summary>
    IFusionConstructionAxisCollection ConstructionAxes { get; }

    /// <summary>adsk.fusion.Component.meshBodies (adsk.fusion.MeshBodyCollection).</summary>
    IFusionMeshBodyCollection MeshBodies { get; }
}

/// <summary>
/// The root component of the design — everything handlers need for
/// scene queries, sketches and delete_all sweeps.
/// </summary>
public interface IFusionRootComponent : IFusionComponent
{
    /// <summary>Number of features (get_scene_info).</summary>
    int FeaturesCount { get; }

    /// <summary>The three default construction axes ("x", "y", "z").</summary>
    IFusionConstructionAxis XConstructionAxis { get; }
    IFusionConstructionAxis YConstructionAxis { get; }
    IFusionConstructionAxis ZConstructionAxis { get; }

    /// <summary>The temporary-BRep factory (direct primitives).</summary>
    IFusionBRepManager BRepManager { get; }

    /// <summary>Upgraded for T7: rigidGroups (create_rigid_group adds via it).</summary>
    IFusionRigidGroupCollection RigidGroups { get; }

    /// <summary>Upgraded for T7: asBuiltJoints (create_as_built_joint).</summary>
    IFusionAsBuiltJointCollection AsBuiltJoints { get; }

    /// <summary>Upgraded for T7: joints (add_joint).</summary>
    IFusionJointCollection Joints { get; }

    /// <summary>adsk.fusion.Component.constructionPoints (delete_all sweeps).</summary>
    IFusionDeletableCollection ConstructionPoints { get; }

    /// <summary>The three default construction planes ("xy", "yz", "xz").</summary>
    IFusionConstructionPlane XYConstructionPlane { get; }
    IFusionConstructionPlane YZConstructionPlane { get; }
    IFusionConstructionPlane XZConstructionPlane { get; }

    /// <summary>
    /// Creates an offset construction plane (reference create_sketch z-offset
    /// path: planes.createInput() + setByOffset + add).
    /// </summary>
    IFusionConstructionPlane CreateOffsetPlane(IFusionConstructionPlane basePlane, double offsetCm);

    /// <summary>adsk.fusion.RootComponent.analyses (create_section_analysis).</summary>
    IFusionAnalysisCollection Analyses { get; }

    /// <summary>
    /// adsk.fusion.RootComponent.userCoordinateSystems — preview API
    /// (Fusion 2025.86+); the adapter raises a FusionCommandException with a
    /// repair hint when the version lacks it.
    /// </summary>
    IFusionUcsCollection UserCoordinateSystems { get; }

    /// <summary>UserCoordinateSystemGeometry.createByPoint (preview API).</summary>
    IFusionUcsGeometry CreateUcsGeometryAtPoint(IFusionSketchPoint point);

    /// <summary>JointGeometry.createByPoint(occurrence, constructionPoint) (add_joint).</summary>
    IFusionJointGeometry CreateJointGeometryByPoint(IFusionOccurrence occurrence, IFusionConstructionPoint point);
}

/// <summary>A B-rep body.</summary>
public interface IFusionBody : IFusionMeasureEntity, IFusionExportTarget
{
    string Name { get; set; }

    /// <summary>Volume in cm^3.</summary>
    double Volume { get; }

    /// <summary>Surface area in cm^2.</summary>
    double Area { get; }

    /// <summary>The assigned material name; null when unassigned.</summary>
    string? MaterialName { get; }

    bool IsVisible { get; set; }
    int FacesCount { get; }
    int EdgesCount { get; }
    int VerticesCount { get; }

    /// <summary>The body's bounding box; null when empty.</summary>
    IFusionBoundingBox? BoundingBox { get; }

    /// <summary>adsk.fusion.BRepBody.entityToken (export sibling-hide workaround).</summary>
    string EntityToken { get; }

    /// <summary>
    /// The owning occurrence when the body lives in a child component
    /// (adsk.fusion.BRepBody.assemblyContext); null at root level.
    /// </summary>
    IFusionOccurrence? AssemblyContext { get; }

    /// <summary>adsk.fusion.BRepBody.appearance — null when unassigned.</summary>
    IFusionAppearance? Appearance { get; set; }

    /// <summary>
    /// adsk.fusion.BRepBody.getPhysicalProperties(accuracy) —
    /// mass kg / volume cm3 / area cm2 / density kg/cm3 / center of mass.
    /// </summary>
    IFusionPhysicalProperties GetPhysicalProperties(FusionCalculationAccuracy accuracy);

    /// <summary>Body edges (project_geometry iterates them).</summary>
    IReadOnlyList<IFusionBodyEdge> Edges { get; }

    /// <summary>Body faces (fillet/shell/hole/draft face selection).</summary>
    IReadOnlyList<IFusionFace> Faces { get; }

    void DeleteMe();
}

/// <summary>A body edge (opaque; passed to sketch projection / fillet / chamfer).</summary>
public interface IFusionBodyEdge
{
    /// <summary>A representative point on the edge (top/bottom edge selection).</summary>
    FusionPoint PointOnEdge { get; }

    /// <summary>Geometry of the edge's start vertex (vertical-edge selection).</summary>
    FusionPoint StartVertex { get; }

    /// <summary>Geometry of the edge's end vertex (vertical-edge selection).</summary>
    FusionPoint EndVertex { get; }
}

/// <summary>A body face (shell / hole / draft / thicken face selection).</summary>
public interface IFusionFace
{
    /// <summary>Face area in cm^2 (hole face-ranking tie-break).</summary>
    double Area { get; }

    /// <summary>The face's bounding box; null when unreadable.</summary>
    IFusionBoundingBox? BoundingBox { get; }

    /// <summary>An arbitrary point on the face (reference face.pointOnFace).</summary>
    FusionPoint PointOnFace { get; }

    /// <summary>
    /// The face's outward normal at a point (reference
    /// <c>face.evaluator.getNormalAtPoint(face.pointOnFace)</c>):
    /// (ok, normal).
    /// </summary>
    (bool Ok, FusionPoint Normal) GetNormalAtPoint(FusionPoint point);

    /// <summary>
    /// The underlying planar geometry when the face is planar (reference
    /// <c>adsk.core.Plane.cast(face.geometry)</c>); null when not planar.
    /// </summary>
    IFusionPlane? PlaneGeometry { get; }

    /// <summary>adsk.fusion.Face.appearance (set_appearance face target) — null when unassigned.</summary>
    IFusionAppearance? Appearance { get; set; }
}

/// <summary>A plane (normal + origin in cm) — the cast of a planar face's geometry.</summary>
public interface IFusionPlane
{
    FusionPoint Normal { get; }
    FusionPoint Origin { get; }
}

/// <summary>A bounding box (min/max in cm).</summary>
public interface IFusionBoundingBox
{
    FusionPoint Min { get; }
    FusionPoint Max { get; }
}

/// <summary>A component occurrence.</summary>
public interface IFusionOccurrence : IFusionExportTarget
{
    /// <summary>The component this occurrence references.</summary>
    IFusionComponent Component { get; }

    bool IsVisible { get; }

    /// <summary>adsk.fusion.Occurrence.boundingBox (export_view_sheet scene bounds).</summary>
    IFusionBoundingBox? BoundingBox { get; }

    /// <summary>Body proxies of the occurrence in root coordinate space.</summary>
    IFusionBRepBodyCollection BRepBodies { get; }
}

/// <summary>Indexable body/occurrence collections (reference: count/item loop).</summary>
public interface IFusionBRepBodyCollection
{
    int Count { get; }
    IFusionBody Item(int index);

    /// <summary>
    /// Adds a body under a base feature (reference
    /// <c>root.bRepBodies.add(body, base_feat)</c> — the direct-primitive path).
    /// </summary>
    void Add(IFusionBody body, IFusionBaseFeature baseFeature);
}

/// <summary>A construction axis ("x"/"y"/"z") — revolve / circular-pattern / rectangular-pattern direction.</summary>
public interface IFusionConstructionAxis : IFusionRevolveAxis
{
    string Name { get; }
}

/// <summary>
/// The temporary-BRep factory (reference adsk.fusion.TemporaryBRepManager):
/// direct primitives that bypass the timeline (create_box / cylinder /
/// sphere / torus).
/// </summary>
public interface IFusionBRepManager
{
    /// <summary>A box centred at <paramref name="center"/> with the given orientation axes (reference OrientedBoundingBox3D path).</summary>
    IFusionBody CreateBox(FusionPoint center, FusionPoint xAxis, FusionPoint yAxis, double lengthCm, double widthCm, double heightCm);

    /// <summary>A cylinder/cone from base point+radius to top point+radius (radius 0 end = cone).</summary>
    IFusionBody CreateCylinderOrCone(FusionPoint basePoint, double baseRadiusCm, FusionPoint topPoint, double topRadiusCm);

    /// <summary>A sphere centred at <paramref name="center"/>.</summary>
    IFusionBody CreateSphere(FusionPoint center, double radiusCm);

    /// <summary>A torus centred at <paramref name="center"/> around <paramref name="axis"/> (major/minor radius in cm).</summary>
    IFusionBody CreateTorus(FusionPoint center, FusionPoint axis, double majorRadiusCm, double minorRadiusCm);
}

public interface IFusionOccurrenceCollection
{
    int Count { get; }
    IFusionOccurrence Item(int index);
}

/// <summary>A deletable collection item (joints, occurrences, construction entities).</summary>
public interface IFusionDeletable
{
    string Name { get; }
    void DeleteMe();
}

/// <summary>A deletable entity collection (delete_all sweeps newest-first).</summary>
public interface IFusionDeletableCollection
{
    int Count { get; }
    IFusionDeletable Item(int index);
}

/// <summary>A construction plane (sketch host / offset-plane base).</summary>
public interface IFusionConstructionPlane
{
    string Name { get; }
}
