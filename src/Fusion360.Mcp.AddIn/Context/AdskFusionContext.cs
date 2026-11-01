using System.Reflection;
using adsk.core;
using adsk.fusion;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// Thin adsk adapter for the D5 facade: maps <see cref="IFusionContext" />
/// onto the adsk .NET surface (thirdparty/adsk stubs at compile time, the
/// real adsknet in-process inside Fusion). All members are read-through;
/// nothing caches the adsk object graph, so handlers always see the live
/// design (reference semantics: _design() / _root() re-resolve per call).
/// </summary>
public sealed class AdskFusionContext : IFusionContext
{
    private readonly adsk.core.Application _app;

    public AdskFusionContext(adsk.core.Application app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
    }

    public IFusionDesign Design => new AdskDesign(_app);

    public IFusionViewport? Viewport
        => _app.ActiveViewport is { } viewport ? new AdskViewport(viewport) : null;

    public IUiCommands Ui => new AdskUiCommands(_app.UserInterface.CommandDefinitions);

    public void DoEvents() => adsk.core.Environment.DoEvents();

    /// <summary>adsk app.measureManager (T7 measure_distance / measure_angle).</summary>
    public IFusionMeasureManager MeasureManager
        => new AdskMeasureManager(_app.MeasureManager);

    /// <summary>adsk app.materialLibraries (T7 set_appearance library scan).</summary>
    public IFusionMaterialLibraries MaterialLibraries
        => new AdskMaterialLibraries(_app.MaterialLibraries);

    /// <summary>
    /// Port of the reference <c>_get_cam</c>: explicit CAMProductType lookup; on the
    /// old version the product exists only after the CAMEnvironment workspace has
    /// been activated, so activate it on demand. Null when the CAM product is
    /// unavailable — the handlers raise the reference error.
    /// </summary>
    public IFusionCamProduct? CamProduct => ResolveCamProduct();

    private adsk.fusion.Design ActiveDesign()
        => _app.ActiveDocument?.Products.ItemByProductType("DesignProductType") is adsk.fusion.Design design
            ? design
            : throw new FusionCommandException("No active design");

    private IFusionCamProduct? ResolveCamProduct()
    {
        adsk.fusion.Manufacture? Find()
        {
            try
            {
                return _app.ActiveDocument?.Products.ItemByProductType("CAMProductType") as adsk.fusion.Manufacture;
            }
            catch
            {
                // itemByProductType raises when the product does not exist yet (reference).
                return null;
            }
        }

        var cam = Find();
        if (cam is null)
        {
            var workspace = _app.UserInterface.Workspaces.ItemById("CAMEnvironment");
            if (workspace is not null)
            {
                try
                {
                    workspace.Activate();
                    DoEvents();
                }
                catch
                {
                    // Reference logs a warning and retries the lookup below.
                }

                cam = Find();
            }
        }

        return cam is null ? null : new AdskCamProduct(cam);
    }

    /// <summary>
    /// Port of the reference <c>_snapshot()</c>: best-effort; null when the
    /// active product is not a readable design. Mass in grams, bbox in cm.
    /// </summary>
    public SceneSnapshot? TakeSnapshot()
    {
        try
        {
            if (_app.ActiveProduct is not adsk.fusion.Design design)
            {
                return null;
            }

            var root = design.RootComponent;
            var bodyCount = root.BRepBodies.Count;
            for (var i = 0; i < root.AllOccurrences.Count; i++)
            {
                bodyCount += root.AllOccurrences.Item(i).BRepBodies.Count;
            }

            FusionPoint? bboxMin = null;
            FusionPoint? bboxMax = null;
            if (bodyCount > 0 && root.BoundingBox is { } bbox)
            {
                bboxMin = ToPoint(bbox.MinPoint);
                bboxMax = ToPoint(bbox.MaxPoint);
            }

            var massGrams = 0.0;
            if (bodyCount > 0)
            {
                try
                {
                    // physicalProperties.mass is in kg; sum occurrence bodies
                    // explicitly so mass_g stays consistent with body_count.
                    var massKg = root.PhysicalProperties.Mass;
                    for (var i = 0; i < root.AllOccurrences.Count; i++)
                    {
                        try
                        {
                            massKg += root.AllOccurrences.Item(i).PhysicalProperties.Mass;
                        }
                        catch
                        {
                            // A single unreadable occurrence mass is skipped
                            // (reference except-pass).
                        }
                    }

                    massGrams = massKg * 1000.0;
                }
                catch
                {
                    massGrams = 0.0;
                }
            }

            return new SceneSnapshot(bodyCount, massGrams, bboxMin, bboxMax);
        }
        catch
        {
            // Best-effort snapshot (reference: "snapshot failed" -> None).
            return null;
        }
    }

    internal static FusionPoint ToPoint(Point3D p) => new(p.X, p.Y, p.Z);

    internal static FusionPoint ToPoint(Vector3D v) => new(v.X, v.Y, v.Z);

    internal static Point3D ToPoint3D(FusionPoint p) => Point3D.Create(p.X, p.Y, p.Z);

    internal static Vector3D ToVector3D(FusionPoint p) => Vector3D.Create(p.X, p.Y, p.Z);

    // ── design / root component ─────────────────────────────────────

    private sealed class AdskDesign : IFusionDesign
    {
        private readonly adsk.core.Application _app;

        public AdskDesign(adsk.core.Application app) => _app = app;

        /// <summary>Port of the reference <c>_design()</c>: explicit DesignProductType lookup first,
        /// so a Manufacture (CAM) workspace cannot masquerade as the design.</summary>
        private adsk.fusion.Design Resolve()
        {
            var document = _app.ActiveDocument;
            if (document is not null)
            {
                try
                {
                    if (document.Products.ItemByProductType("DesignProductType") is adsk.fusion.Design found)
                    {
                        return found;
                    }
                }
                catch
                {
                    // Fall through to the active-product check (reference).
                }
            }

            if (_app.ActiveProduct is not adsk.fusion.Design design || design.RootComponent is null)
            {
                throw new FusionCommandException("No active design");
            }

            return design;
        }

        public string DocumentName => Resolve().ParentDocument.Name;
        public string DocumentIdentity => Resolve().ParentDocument.CreationId;
        public string ProductType => Resolve().ProductType;

        public int DesignType
        {
            get => (int)Resolve().DesignType;
            set => Resolve().DesignType = (adsk.fusion.DesignTypes)value;
        }

        public IFusionRootComponent RootComponent => new AdskRootComponent(Resolve().RootComponent);

        public IFusionUserParameterCollection UserParameters
            => new AdskUserParameterCollection(Resolve().UserParameters);

        public IFusionAppearanceCollection Appearances
            => new AdskAppearances(Resolve().Appearances);

        public IFusionExportManager ExportManager
            => new AdskExportManager(Resolve().ExportManager);

        /// <summary>Reference check_interference: design.createInterferenceInput(bodies).</summary>
        public IFusionInterferenceInput CreateInterferenceInput(IReadOnlyList<IFusionBody> bodies)
        {
            var collection = AdskColl(bodies.Select(b => (object)((AdskBody)b).Body));
            return new AdskInterferenceInput(Resolve().CreateInterferenceInput(collection));
        }

        /// <summary>Reference check_interference: design.analyzeInterference — null when there is none.</summary>
        public IReadOnlyList<IFusionInterferenceResult>? AnalyzeInterference(IFusionInterferenceInput input)
        {
            var result = Resolve().AnalyzeInterference(((AdskInterferenceInput)input).Input);
            if (result is null)
            {
                return null;
            }

            var list = new List<IFusionInterferenceResult>(result.Count);
            for (var i = 0; i < result.Count; i++)
            {
                var pair = result.Item(i);
                list.Add(new AdskInterferenceResult(pair));
            }

            return list;
        }

        public IFusionTimeline? Timeline => Resolve().Timeline is { } timeline ? new AdskTimeline(timeline) : null;
    }

    private sealed class AdskParameter : IFusionParameter
    {
        internal readonly adsk.fusion.Parameter _p;

        public AdskParameter(adsk.fusion.Parameter p) => _p = p;

        public string Name => _p.Name;
        public double Value => _p.Value;

        /// <summary>Expression read/write (reference set_parameter writes the expression, never .value).</summary>
        public string? Expression
        {
            get => _p.Expression;
            set => _p.Expression = value;
        }

        public string? Unit => _p.Unit;
        public string? Comment => _p.Comment;
    }

    private sealed class AdskTimeline : IFusionTimeline
    {
        private readonly adsk.fusion.Timeline _tl;

        public AdskTimeline(adsk.fusion.Timeline timeline) => _tl = timeline;

        public int Count => _tl.Count;
        public IFusionTimelineItem Item(int index) => new AdskTimelineItem(_tl.Item(index));
    }

    private sealed class AdskTimelineItem : IFusionTimelineItem
    {
        private readonly adsk.fusion.Feature _feature;

        public AdskTimelineItem(adsk.fusion.Feature feature) => _feature = feature;

        public string Name => _feature.Name;
        public IFusionTimelineEntity? Entity => _feature.Entity is { } entity ? new AdskTimelineEntity(entity) : null;
        public bool IsSuppressed
        {
            get => _feature.IsSuppressed;
            set => _feature.IsSuppressed = value;
        }
    }

    private sealed class AdskTimelineEntity : IFusionTimelineEntity
    {
        private readonly adsk.fusion.Entity _entity;

        public AdskTimelineEntity(adsk.fusion.Entity entity) => _entity = entity;

        public string Name => _entity.Name;
        public void DeleteMe() => _entity.DeleteMe();
    }

    private sealed class AdskRootComponent : IFusionRootComponent
    {
        private readonly adsk.fusion.RootComponent _root;

        public AdskRootComponent(adsk.fusion.RootComponent root) => _root = root;

        public string Name
        {
            get => _root.Name;
            set => _root.Name = value;
        }

        public IFusionBRepBodyCollection BRepBodies => new AdskBRepBodies(_root.BRepBodies);
        public IFusionOccurrenceCollection AllOccurrences => new AdskOccurrences(_root.AllOccurrences);
        public double? PhysicalMassKg
        {
            get
            {
                try
                {
                    return _root.PhysicalProperties.Mass;
                }
                catch
                {
                    return null;
                }
            }
        }

        public IFusionBoundingBox? BoundingBox
            => _root.BoundingBox is { } bbox ? new AdskBoundingBox(bbox) : null;

        /// <summary>adsk.fusion.Component.appearance — null when unassigned (set_appearance component target).</summary>
        public IFusionAppearance? Appearance
        {
            get => _root.Appearance is { } a ? new AdskAppearance(a) : null;
            set => _root.Appearance = value is AdskAppearance a ? a.Appearance : null;
        }

        /// <summary>adsk.fusion.Component.originConstructionPoint (joint anchors).</summary>
        public IFusionConstructionPoint OriginConstructionPoint
            => new AdskConstructionPoint(_root.OriginConstructionPoint);

        /// <summary>adsk.fusion.Component.meshBodies (import_mesh / compare_meshes).</summary>
        public IFusionMeshBodyCollection MeshBodies => new AdskMeshBodies(_root.MeshBodies);

        public IFusionSketchCollection Sketches => new AdskSketches(_root.Sketches);
        public IFusionFeatures Features => new AdskFeatures(_root.Features);
        public int FeaturesCount => _root.Features.Count;

        public IFusionConstructionAxis XConstructionAxis => new AdskConstructionAxis(_root.XConstructionAxis);
        public IFusionConstructionAxis YConstructionAxis => new AdskConstructionAxis(_root.YConstructionAxis);
        public IFusionConstructionAxis ZConstructionAxis => new AdskConstructionAxis(_root.ZConstructionAxis);

        public IFusionBRepManager BRepManager => new AdskBRepManager(adsk.fusion.TemporaryBRepManager.get());

        public IFusionOccurrences Occurrences => new AdskComponentOccurrences(_root.Occurrences);
        public IFusionRigidGroupCollection RigidGroups => new AdskRigidGroupCollection(_root.RigidGroups);
        public IFusionAsBuiltJointCollection AsBuiltJoints => new AdskAsBuiltJointCollection(_root.AsBuiltJoints);
        public IFusionJointCollection Joints => new AdskJointCollection(_root.Joints);
        public IFusionConstructionPlaneCollection ConstructionPlanes
            => new AdskConstructionPlaneCollection(_root.ConstructionPlanes);
        public IFusionConstructionAxisCollection ConstructionAxes
            => new AdskConstructionAxisCollection(_root.ConstructionAxes);
        public IFusionDeletableCollection ConstructionPoints => new AdskDeletableCollection(_root.ConstructionPoints);

        public IFusionConstructionPlane XYConstructionPlane => new AdskConstructionPlane(_root.XYConstructionPlane);
        public IFusionConstructionPlane YZConstructionPlane => new AdskConstructionPlane(_root.YZConstructionPlane);
        public IFusionConstructionPlane XZConstructionPlane => new AdskConstructionPlane(_root.XZConstructionPlane);

        /// <summary>Reference create_sketch z-offset path: createInput + setByOffset + add.</summary>
        public IFusionConstructionPlane CreateOffsetPlane(IFusionConstructionPlane basePlane, double offsetCm)
        {
            var planes = _root.ConstructionPlanes;
            var input = planes.CreateInput();
            input.SetByOffset(((AdskConstructionPlane)basePlane).AdskPlane, ValueInput.CreateByReal(offsetCm));
            return new AdskConstructionPlane(planes.Add(input));
        }

        /// <summary>adsk.fusion.RootComponent.analyses (create_section_analysis).</summary>
        public IFusionAnalysisCollection Analyses => new AdskAnalyses(_root.Analyses);

        /// <summary>
        /// adsk.fusion.RootComponent.userCoordinateSystems — preview API
        /// (Fusion 2025.86+); a missing surface surfaces as the classified
        /// missing-API error the same way AutoConstrain does.
        /// </summary>
        public IFusionUcsCollection UserCoordinateSystems
            => new AdskUcsCollection(_root.UserCoordinateSystems);

        /// <summary>Reference create_ucs: UserCoordinateSystemGeometry.createByPoint(sketchPoint).</summary>
        public IFusionUcsGeometry CreateUcsGeometryAtPoint(IFusionSketchPoint point)
            => new AdskUcsGeometry(_root.CreateUcsGeometryAtPoint(((AdskSketchPoint)point)._point));

        /// <summary>Reference add_joint: JointGeometry.createByPoint(occurrence, originConstructionPoint).</summary>
        public IFusionJointGeometry CreateJointGeometryByPoint(IFusionOccurrence occurrence, IFusionConstructionPoint point)
            => new AdskJointGeometry(_root.CreateJointGeometryByPoint(
                ((AdskOccurrence)occurrence).Adsk,
                ((AdskConstructionPoint)point).Point));
    }

    private sealed class AdskBRepBodies : IFusionBRepBodyCollection
    {
        private readonly adsk.fusion.BRepBodyCollection _c;

        public AdskBRepBodies(adsk.fusion.BRepBodyCollection c) => _c = c;

        public int Count => _c.Count;
        public IFusionBody Item(int index) => new AdskBody(_c.Item(index));

        public void Add(IFusionBody body, IFusionBaseFeature baseFeature)
            => _c.Add(((AdskBody)body).Body, ((AdskBaseFeature)baseFeature).AdskBase);
    }

    private sealed class AdskBody : IFusionBody
    {
        public AdskBody(adsk.fusion.BRepBody body) => Body = body;

        public adsk.fusion.BRepBody Body { get; }

        public string Name
        {
            get => Body.Name;
            set => Body.Name = value;
        }

        public double Volume => Body.Volume;
        public double Area => Body.Area;
        public string? MaterialName => Body.Material?.Name;

        public bool IsVisible
        {
            get => Body.IsVisible;
            set => Body.IsVisible = value;
        }

        public int FacesCount => Body.Faces.Count;
        public int EdgesCount => Body.Edges.Count;
        public int VerticesCount => Body.Vertices.Count;
        public IFusionBoundingBox? BoundingBox
            => Body.BoundingBox is { } bbox ? new AdskBoundingBox(bbox) : null;

        /// <summary>adsk.fusion.BRepBody.entityToken (export's sibling-hide workaround).</summary>
        public string EntityToken => Body.EntityToken ?? string.Empty;

        /// <summary>adsk.fusion.BRepBody.assemblyContext — null at root level.</summary>
        public IFusionOccurrence? AssemblyContext
            => Body.AssemblyContext is { } occ ? new AdskOccurrence(occ) : null;

        /// <summary>adsk.fusion.BRepBody.appearance — null when unassigned (set_color / set_appearance).</summary>
        public IFusionAppearance? Appearance
        {
            get => Body.Appearance is { } a ? new AdskAppearance(a) : null;
            set => Body.Appearance = value is AdskAppearance a ? a.Appearance : null;
        }

        /// <summary>Reference get_physical_properties: body.getPhysicalProperties(accuracy).</summary>
        public IFusionPhysicalProperties GetPhysicalProperties(FusionCalculationAccuracy accuracy)
            => new AdskPhysicalProperties(Body.GetPhysicalProperties(ToAdskAccuracy(accuracy)));

        public IReadOnlyList<IFusionBodyEdge> Edges
        {
            get
            {
                var source = Body.Edges;
                var list = new List<IFusionBodyEdge>(source.Count);
                for (var i = 0; i < source.Count; i++)
                {
                    list.Add(new AdskBodyEdge(source.Item(i)));
                }

                return list;
            }
        }

        public IReadOnlyList<IFusionFace> Faces
        {
            get
            {
                var source = Body.Faces;
                var list = new List<IFusionFace>(source.Count);
                for (var i = 0; i < source.Count; i++)
                {
                    list.Add(new AdskFace(source.Item(i)));
                }

                return list;
            }
        }

        public void DeleteMe() => Body.DeleteMe();
    }

    private sealed class AdskBodyEdge : IFusionBodyEdge
    {
        public AdskBodyEdge(adsk.fusion.Edge edge) => AdskEdge = edge;

        public adsk.fusion.Edge AdskEdge { get; }

        public FusionPoint PointOnEdge => ToPoint(AdskEdge.PointOnEdge);
        public FusionPoint StartVertex => ToPoint(AdskEdge.StartVertex.Geometry);
        public FusionPoint EndVertex => ToPoint(AdskEdge.EndVertex.Geometry);
    }

    private sealed class AdskFace : IFusionFace
    {
        public AdskFace(adsk.fusion.Face face) => Face = face;

        public adsk.fusion.Face Face { get; }

        public double Area => Face.Area;

        public IFusionBoundingBox? BoundingBox
            => Face.BoundingBox is { } bbox ? new AdskBoundingBox(bbox) : null;

        public FusionPoint PointOnFace => ToPoint(Face.PointOnFace);

        /// <summary>adsk.fusion.Face.appearance (set_appearance face target) — null when unassigned.</summary>
        public IFusionAppearance? Appearance
        {
            get => Face.Appearance is { } a ? new AdskAppearance(a) : null;
            set => Face.Appearance = value is AdskAppearance a ? a.Appearance : null;
        }

        public (bool Ok, FusionPoint Normal) GetNormalAtPoint(FusionPoint point)
        {
            // Reference face.evaluator.getNormalAtPoint(point) — the evaluator
            // reports the outward normal; false when the query fails.
            return Face.Evaluator.GetNormalAtPoint(ToPoint3D(point), out var normal)
                ? (true, ToPoint(normal))
                : (false, default);
        }

        public IFusionPlane? PlaneGeometry
            => adsk.core.Plane.Cast(Face.Geometry) is { } plane ? new AdskPlane(plane) : null;
    }

    private sealed class AdskPlane : IFusionPlane
    {
        private readonly adsk.core.Plane _p;

        public AdskPlane(adsk.core.Plane plane) => _p = plane;

        public FusionPoint Normal => ToPoint(_p.Normal);
        public FusionPoint Origin => ToPoint(_p.Origin);
    }

    private sealed class AdskOccurrences : IFusionOccurrenceCollection
    {
        private readonly adsk.fusion.OccurrenceCollection _c;

        public AdskOccurrences(adsk.fusion.OccurrenceCollection c) => _c = c;

        public int Count => _c.Count;
        public IFusionOccurrence Item(int index) => new AdskOccurrence(_c.Item(index));
    }

    private sealed class AdskComponentView : IFusionComponent
    {
        private readonly adsk.fusion.Component _c;

        public AdskComponentView(adsk.fusion.Component component) => _c = component;

        public string Name
        {
            get => _c.Name;
            set => _c.Name = value;
        }

        public IFusionBRepBodyCollection BRepBodies => new AdskBRepBodies(_c.BRepBodies);
        public IFusionOccurrenceCollection AllOccurrences => new AdskOccurrences(_c.AllOccurrences);

        public double? PhysicalMassKg
        {
            get
            {
                try
                {
                    return _c.PhysicalProperties.Mass;
                }
                catch
                {
                    return null;
                }
            }
        }

        public IFusionBoundingBox? BoundingBox
            => _c.BoundingBox is { } bbox ? new AdskBoundingBox(bbox) : null;

        /// <summary>Occurrences contained in this component (create_component on a child parent).</summary>
        public IFusionOccurrences Occurrences => new AdskComponentOccurrences(_c.Occurrences);

        /// <summary>adsk.fusion.Component.appearance — null when unassigned.</summary>
        public IFusionAppearance? Appearance
        {
            get => _c.Appearance is { } a ? new AdskAppearance(a) : null;
            set => _c.Appearance = value is AdskAppearance a ? a.Appearance : null;
        }

        /// <summary>adsk.fusion.Component.originConstructionPoint (joint anchors).</summary>
        public IFusionConstructionPoint OriginConstructionPoint
            => new AdskConstructionPoint(_c.OriginConstructionPoint);

        /// <summary>Sketches hosted by this component (create_box_parametric component_name path).</summary>
        public IFusionSketchCollection Sketches => new AdskSketches(_c.Sketches);

        /// <summary>Features hosted by this component (create_box_parametric on a child component).</summary>
        public IFusionFeatures Features => new AdskFeatures(_c.Features);

        /// <summary>Construction planes, upgraded for T7 (create_input/add).</summary>
        public IFusionConstructionPlaneCollection ConstructionPlanes
            => new AdskConstructionPlaneCollection(_c.ConstructionPlanes);

        /// <summary>Construction axes, upgraded for T7 (create_input/add).</summary>
        public IFusionConstructionAxisCollection ConstructionAxes
            => new AdskConstructionAxisCollection(_c.ConstructionAxes);

        /// <summary>adsk.fusion.Component.meshBodies (import_mesh on a child component).</summary>
        public IFusionMeshBodyCollection MeshBodies => new AdskMeshBodies(_c.MeshBodies);
    }

    private sealed class AdskOccurrence : IFusionOccurrence
    {
        private readonly adsk.fusion.Occurrence _o;

        public AdskOccurrence(adsk.fusion.Occurrence occ) => _o = occ;

        /// <summary>The wrapped adsk occurrence (joint geometry / rigid-group add).</summary>
        internal adsk.fusion.Occurrence Adsk => _o;

        public IFusionComponent Component => new AdskComponentView(_o.Component);
        public bool IsVisible => _o.IsVisible;

        /// <summary>adsk.fusion.Occurrence.boundingBox (export_view_sheet scene bounds).</summary>
        public IFusionBoundingBox? BoundingBox
            => _o.BoundingBox is { } bbox ? new AdskBoundingBox(bbox) : null;

        public IFusionBRepBodyCollection BRepBodies => new AdskBRepBodies(_o.BRepBodies);
    }

    /// <summary>Generic deletable-collection wrapper (joints, occurrences, construction entities).</summary>
    private sealed class AdskDeletableCollection : IFusionDeletableCollection
    {
        private readonly Func<int, IFusionDeletable> _item;
        private readonly Func<int> _count;

        public AdskDeletableCollection(Func<int, IFusionDeletable> item, Func<int> count)
        {
            _item = item;
            _count = count;
        }

        internal AdskDeletableCollection(adsk.fusion.JointCollection c)
            : this(
                i => { var e = c.Item(i); return new AdskDeletable(() => e.Name, e.DeleteMe); },
                () => c.Count)
        {
        }

        internal AdskDeletableCollection(adsk.fusion.AsBuiltJointCollection c)
            : this(
                i => { var e = c.Item(i); return new AdskDeletable(() => e.Name, e.DeleteMe); },
                () => c.Count)
        {
        }

        internal AdskDeletableCollection(adsk.fusion.RigidGroupCollection c)
            : this(
                i => { var e = c.Item(i); return new AdskDeletable(() => e.Name, e.DeleteMe); },
                () => c.Count)
        {
        }

        internal AdskDeletableCollection(adsk.fusion.ConstructionPlaneCollection c)
            : this(
                i => { var e = c.Item(i); return new AdskDeletable(() => e.Name, e.DeleteMe); },
                () => c.Count)
        {
        }

        internal AdskDeletableCollection(adsk.fusion.ConstructionAxisCollection c)
            : this(
                i => { var e = c.Item(i); return new AdskDeletable(() => e.Name, e.DeleteMe); },
                () => c.Count)
        {
        }

        internal AdskDeletableCollection(adsk.fusion.ConstructionPointCollection c)
            : this(
                i => { var e = c.Item(i); return new AdskDeletable(() => e.Name, e.DeleteMe); },
                () => c.Count)
        {
        }

        internal AdskDeletableCollection(adsk.fusion.OccurrenceCollection c)
            : this(
                i => { var e = c.Item(i); return new AdskDeletable(() => e.Component.Name, e.DeleteMe); },
                () => c.Count)
        {
        }

        public int Count => _count();
        public IFusionDeletable Item(int index) => _item(index);
    }

    /// <summary>One deletable entity of a swept collection.</summary>
    private sealed class AdskDeletable : IFusionDeletable
    {
        private readonly Func<string> _name;
        private readonly Action _delete;

        public AdskDeletable(Func<string> name, Action delete)
        {
            _name = name;
            _delete = delete;
        }

        public string Name => _name();
        public void DeleteMe() => _delete();
    }

    private sealed class AdskConstructionPlane : IFusionConstructionPlane
    {
        public AdskConstructionPlane(adsk.fusion.ConstructionPlane plane) => AdskPlane = plane;

        public adsk.fusion.ConstructionPlane AdskPlane { get; }
        public string Name => AdskPlane.Name;
    }

    private sealed class AdskBoundingBox : IFusionBoundingBox
    {
        public AdskBoundingBox(adsk.fusion.BoundingBox bbox) => _bbox = bbox;

        private readonly adsk.fusion.BoundingBox _bbox;

        public FusionPoint Min => ToPoint(_bbox.MinPoint);
        public FusionPoint Max => ToPoint(_bbox.MaxPoint);
    }

    // ── sketches ────────────────────────────────────────────────────

    private sealed class AdskSketches : IFusionSketchCollection
    {
        private readonly adsk.fusion.SketchCollection _c;

        public AdskSketches(adsk.fusion.SketchCollection c) => _c = c;

        public int Count => _c.Count;
        public IFusionSketch Item(int index) => new AdskSketch(_c.Item(index));
        public IFusionSketch Add(IFusionConstructionPlane plane)
            => new AdskSketch(_c.Add(((AdskConstructionPlane)plane).AdskPlane));

        public IFusionSketch AddOnFace(IFusionFace face)
            => new AdskSketch(_c.Add(((AdskFace)face).Face));
    }

    private sealed class AdskSketch : IFusionSketch
    {
        private readonly adsk.fusion.Sketch _sketch;

        public AdskSketch(adsk.fusion.Sketch sketch) => _sketch = sketch;

        public string Name
        {
            get => _sketch.Name;
            set => _sketch.Name = value;
        }

        public bool IsVisible
        {
            get => _sketch.IsVisible;
            set => _sketch.IsVisible = value;
        }

        public int ProfilesCount => _sketch.Profiles.Count;

        public IFusionProfileCollection Profiles => new AdskProfiles(_sketch.Profiles);

        public IFusionSketchCurveCollections Curves
            => new AdskCurveCollections(_sketch.SketchCurves);

        public IReadOnlyList<IFusionSketchCurve> SketchCurves
        {
            get
            {
                var source = _sketch.SketchCurves;
                var list = new List<IFusionSketchCurve>(source.Count);
                for (var i = 0; i < source.Count; i++)
                {
                    list.Add(new AdskSketchCurve(source.Item(i)));
                }

                return list;
            }
        }

        public IFusionGeometricConstraints Constraints
            => new AdskConstraints(_sketch.GeometricConstraints);

        public IFusionSketchDimensions Dimensions => new AdskDimensions(_sketch.SketchDimensions);

        public IFusionSketchPointCollection Points
            => new AdskPoints(_sketch.SketchPoints);

        public IFusionSketchPoint AddPointInModelSpace(FusionPoint modelPoint)
        {
            // Reference sketch.sketchPoints.add(sketch.modelToSketchSpace(p)):
            // the model-to-sketch mapping is the adapter's concern.
            return new AdskSketchPoint(_sketch.SketchPoints.Add(_sketch.ModelToSketchSpace(ToPoint3D(modelPoint))));
        }

        public void Offset(IFusionSketchCurve curve, FusionPoint direction, double distanceCm)
        {
            var coll = ObjectCollection.Create();
            coll.Add(((AdskSketchCurve)curve).AdskCurve);
            _sketch.Offset(coll, ToPoint3D(direction), distanceCm);
        }

        public int ProjectEdge(IFusionBodyEdge edge)
            => _sketch.Project(((AdskBodyEdge)edge).AdskEdge).Count;

        public IFusionAutoConstrainResult? AutoConstrain(int resultOption)
        {
            try
            {
                var input = _sketch.CreateAutoConstrainInput();
                input.ResultOption = resultOption;
                var result = _sketch.AutoConstrain(input);
                return result is null
                    ? null
                    : new AdskAutoConstrainResult(result);
            }
            catch (Exception ex) when (ex is TypeAccessException or MethodAccessException or MissingMethodException)
            {
                // Preview API (Fusion 2026+) absent from this adsk build —
                // PLAN.md §6: surface a missing-API error, not a crash.
                throw new FusionCommandException(
                    "autoConstrain is unavailable in this Fusion version (preview API, Fusion 2026+). "
                    + "Update Fusion or add constraints manually with add_constraint.");
            }
        }

        public void DeleteMe() => _sketch.DeleteMe();
    }

    private sealed class AdskSketchCurve : IFusionSketchCurve
    {
        public AdskSketchCurve(adsk.fusion.SketchCurve curve) => AdskCurve = curve;

        public adsk.fusion.SketchCurve AdskCurve { get; }

        public IFusionSketchPoint StartSketchPoint
            => new AdskSketchPoint(AdskCurve.StartSketchPoint);

        /// <summary>The curve's end sketch point (create_box_parametric dimensioning of a line's own span).</summary>
        public IFusionSketchPoint EndSketchPoint
            => new AdskSketchPoint(AdskCurve.EndSketchPoint);

        public void Trim(FusionPoint point) => AdskCurve.Trim(ToPoint3D(point));
        public void Extend(FusionPoint point) => AdskCurve.Extend(ToPoint3D(point));

        public void MarkAsConstruction()
        {
            // Reference line.isConstruction = True (the revolve-axis fallback
            // only ever creates a line, so the cast is the full surface).
            if (AdskCurve is adsk.fusion.SketchLine line)
            {
                line.IsConstruction = true;
            }
        }

        /// <summary>P1 (ADR-0009): curve-kind tag for the verification builder's profile-shape detection.</summary>
        public string CurveKind
            => AdskCurve switch
            {
                adsk.fusion.SketchLine => "line",
                adsk.fusion.SketchCircle => "circle",
                adsk.fusion.SketchArc => "arc",
                _ => "spline",
            };
    }

    private sealed class AdskSketchPoint : IFusionSketchPoint
    {
        public AdskSketchPoint(adsk.fusion.SketchPoint point) => _point = point;

        internal readonly adsk.fusion.SketchPoint _point;
    }

    private sealed class AdskPoints : IFusionSketchPointCollection
    {
        private readonly adsk.fusion.SketchPointCollection _c;

        public AdskPoints(adsk.fusion.SketchPointCollection c) => _c = c;

        public IFusionSketchPoint Item(int index) => new AdskSketchPoint(_c.Item(index));
    }

    private sealed class AdskCurveCollections : IFusionSketchCurveCollections
    {
        private readonly adsk.fusion.SketchCurveCollection _c;

        public AdskCurveCollections(adsk.fusion.SketchCurveCollection c) => _c = c;

        public IFusionSketchLineCollection Lines => new AdskLines(_c.SketchLines);
        public IFusionSketchCircleCollection Circles => new AdskCircles(_c.SketchCircles);
        public IFusionSketchArcCollection Arcs => new AdskArcs(_c.SketchArcs);
        public IFusionSketchFittedSplineCollection FittedSplines => new AdskFittedSplines(_c.SketchFittedSplines);
        public IFusionSketchControlPointSplineCollection ControlPointSplines
            => new AdskControlPointSplines(_c.SketchControlPointSplines);
    }

    private sealed class AdskLines : IFusionSketchLineCollection
    {
        private readonly adsk.fusion.SketchLineCollection _c;

        public AdskLines(adsk.fusion.SketchLineCollection c) => _c = c;

        public IFusionSketchCurve AddTwoPointRectangle(FusionPoint p1, FusionPoint p2)
            => new AdskSketchCurve(_c.AddTwoPointRectangle(ToPoint3D(p1), ToPoint3D(p2)));

        public IFusionSketchCurve AddByTwoPoints(FusionPoint p1, FusionPoint p2)
            => new AdskSketchCurve(_c.AddByTwoPoints(ToPoint3D(p1), ToPoint3D(p2)));

        /// <summary>add_bend's <c>sketchLines.item(0)</c>.</summary>
        public IFusionSketchCurve Item(int index)
            => new AdskSketchCurve(_c.Item(index));

        /// <summary>create_box_parametric: the two dimensioning lines (bottom = item 0, right = item 1).</summary>
        public (IFusionSketchCurve Bottom, IFusionSketchCurve Right) AddTwoPointRectangleLines(FusionPoint p1, FusionPoint p2)
        {
            var lines = _c.AddTwoPointRectangleLines(ToPoint3D(p1), ToPoint3D(p2));
            return (new AdskSketchCurve(lines.Item(0)), new AdskSketchCurve(lines.Item(1)));
        }
    }

    private sealed class AdskCircles : IFusionSketchCircleCollection
    {
        private readonly adsk.fusion.SketchCircleCollection _c;

        public AdskCircles(adsk.fusion.SketchCircleCollection c) => _c = c;

        public IFusionSketchCurve AddByCenterRadius(FusionPoint center, double radiusCm)
            => new AdskSketchCurve(_c.AddByCenterRadius(ToPoint3D(center), radiusCm));
    }

    private sealed class AdskArcs : IFusionSketchArcCollection
    {
        private readonly adsk.fusion.SketchArcCollection _c;

        public AdskArcs(adsk.fusion.SketchArcCollection c) => _c = c;

        public IFusionSketchCurve AddByCenterStartSweep(FusionPoint center, FusionPoint start, double sweepRadians)
            => new AdskSketchCurve(_c.AddByCenterStartSweep(ToPoint3D(center), ToPoint3D(start), sweepRadians));
    }

    private sealed class AdskFittedSplines : IFusionSketchFittedSplineCollection
    {
        private readonly adsk.fusion.SketchFittedSplineCollection _c;

        public AdskFittedSplines(adsk.fusion.SketchFittedSplineCollection c) => _c = c;

        public IFusionSketchCurve Add(IReadOnlyList<FusionPoint> points)
        {
            var coll = ObjectCollection.Create();
            foreach (var p in points)
            {
                coll.Add(ToPoint3D(p));
            }

            return new AdskSketchCurve(_c.Add(coll));
        }
    }

    private sealed class AdskControlPointSplines : IFusionSketchControlPointSplineCollection
    {
        private readonly adsk.fusion.SketchControlPointSplineCollection _c;

        public AdskControlPointSplines(adsk.fusion.SketchControlPointSplineCollection c) => _c = c;

        public IFusionSketchCurve Add(IReadOnlyList<FusionPoint> controlPoints, int degree)
        {
            var coll = ObjectCollection.Create();
            foreach (var p in controlPoints)
            {
                coll.Add(ToPoint3D(p));
            }

            return new AdskSketchCurve(_c.Add(coll, degree));
        }
    }

    private sealed class AdskConstraints : IFusionGeometricConstraints
    {
        private readonly adsk.fusion.GeometricConstraints _g;

        public AdskConstraints(adsk.fusion.GeometricConstraints g) => _g = g;

        private static adsk.fusion.SketchCurve? Adsk(IFusionSketchCurve? c)
            => c is AdskSketchCurve s ? s.AdskCurve : null;

        public void AddCoincident(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => _g.AddCoincident(Adsk(e1), Adsk(e2));
        public void AddParallel(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => _g.AddParallel(Adsk(e1), Adsk(e2));
        public void AddPerpendicular(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => _g.AddPerpendicular(Adsk(e1), Adsk(e2));
        public void AddTangent(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => _g.AddTangent(Adsk(e1), Adsk(e2));
        public void AddEqual(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => _g.AddEqual(Adsk(e1), Adsk(e2));
        public void AddFix(IFusionSketchCurve? e1) => _g.AddFix(Adsk(e1));
        public void AddHorizontal(IFusionSketchCurve? e1) => _g.AddHorizontal(Adsk(e1));
        public void AddVertical(IFusionSketchCurve? e1) => _g.AddVertical(Adsk(e1));
        public void AddConcentric(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => _g.AddConcentric(Adsk(e1), Adsk(e2));
        public void AddCollinear(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => _g.AddCollinear(Adsk(e1), Adsk(e2));
        public void AddSmooth(IFusionSketchCurve? e1, IFusionSketchCurve? e2) => _g.AddSmooth(Adsk(e1), Adsk(e2));

        public void AddMidPoint(IFusionSketchPoint point, IFusionSketchCurve? e2)
            => _g.AddMidPoint(((AdskSketchPoint)point)._point, Adsk(e2));

        public void AddSymmetry(IFusionSketchCurve? e1, IFusionSketchCurve? e2, IFusionSketchCurve symmetryLine)
            => _g.AddSymmetry(Adsk(e1), Adsk(e2), ((AdskSketchCurve)symmetryLine).AdskCurve);
    }

    private sealed class AdskDimensions : IFusionSketchDimensions
    {
        private readonly adsk.fusion.SketchDimensions _d;

        public AdskDimensions(adsk.fusion.SketchDimensions d) => _d = d;

        public IFusionSketchDimension AddDistanceDimension(IFusionSketchCurve e1, IFusionSketchCurve e2, int orientation, FusionPoint textPoint)
            => new AdskDimension(
                _d.AddDistanceDimension(
                    ((AdskSketchCurve)e1).AdskCurve.StartSketchPoint,
                    ((AdskSketchCurve)e2).AdskCurve.StartSketchPoint,
                    orientation,
                    ToPoint3D(textPoint)));

        /// <summary>create_box_parametric: distance dimension between a line's own endpoints.</summary>
        public IFusionSketchDimension AddEndpointDistanceDimension(IFusionSketchCurve line, int orientation, FusionPoint textPoint)
            => new AdskDimension(
                _d.AddEndpointDistanceDimension(((AdskSketchCurve)line).AdskCurve, orientation, ToPoint3D(textPoint)));

        public IFusionSketchDimension AddAngularDimension(IFusionSketchCurve e1, IFusionSketchCurve e2, FusionPoint textPoint)
            => new AdskDimension(
                _d.AddAngularDimension(((AdskSketchCurve)e1).AdskCurve, ((AdskSketchCurve)e2).AdskCurve, ToPoint3D(textPoint)));

        public IFusionSketchDimension AddRadialDimension(IFusionSketchCurve e1, FusionPoint textPoint)
            => new AdskDimension(
                _d.AddRadialDimension(((AdskSketchCurve)e1).AdskCurve, ToPoint3D(textPoint)));

        public IFusionSketchDimension AddDiameterDimension(IFusionSketchCurve e1, FusionPoint textPoint)
            => new AdskDimension(
                _d.AddDiameterDimension(((AdskSketchCurve)e1).AdskCurve, ToPoint3D(textPoint)));
    }

    private sealed class AdskDimension : IFusionSketchDimension
    {
        private readonly adsk.fusion.SketchDimension _d;

        public AdskDimension(adsk.fusion.SketchDimension dimension) => _d = dimension;

        public void SetParameterValue(double valueCm) => _d.Parameter.Value = valueCm;

        /// <summary>create_box_parametric parametric dims — reference <c>dim.parameter.expression = value</c>.</summary>
        public void SetParameterExpression(string expression) => _d.Parameter.Expression = expression;
    }

    private sealed class AdskAutoConstrainResult : IFusionAutoConstrainResult
    {
        private readonly adsk.fusion.AutoConstrainResult _r;

        public AdskAutoConstrainResult(adsk.fusion.AutoConstrainResult r) => _r = r;

        public bool IsFullyConstrained => _r.IsFullyConstrained;
        public int AddedConstraints => _r.AddedConstraints;
        public int AddedDimensions => _r.AddedDimensions;
        public int MovedGeometry => _r.MovedGeometry;
    }

    // ── T6: features / body ops / direct primitives / surfaces ─────

    /// <summary>Enum values mirror the adsknet ids, so a numeric cast is the mapping.</summary>
    private static adsk.fusion.FeatureOperations ToAdskOp(FusionFeatureOperation op)
        => (adsk.fusion.FeatureOperations)(int)op;

    private static adsk.fusion.SurfaceContinuityTypes ToAdskCont(FusionSurfaceContinuity cont)
        => (adsk.fusion.SurfaceContinuityTypes)(int)cont;

    /// <summary>Builds the adsknet object container (reference ObjectCollection usage in feature inputs).</summary>
    private static adsk.core.ObjectCollection AdskColl(IEnumerable<object> items)
    {
        var coll = adsk.core.ObjectCollection.Create();
        foreach (var item in items)
        {
            coll.Add(item);
        }

        return coll;
    }

    /// <summary>Unwraps a revolve axis: construction axis or sketch curve (reference axis_entity union).</summary>
    private static object AdskRevolveAxis(IFusionRevolveAxis axis) => axis switch
    {
        AdskConstructionAxis a => a.AdskAxis,
        AdskSketchCurve s => s.AdskCurve,
        _ => throw new FusionCommandException("Revolve axis must be a construction axis or a sketch curve"),
    };

    private sealed class AdskProfiles : IFusionProfileCollection
    {
        private readonly adsk.fusion.ProfileCollection _c;

        public AdskProfiles(adsk.fusion.ProfileCollection c) => _c = c;

        public int Count => _c.Count;
        public IFusionProfile Item(int index) => new AdskProfile(_c.Item(index));
    }

    private sealed class AdskProfile : IFusionProfile
    {
        public AdskProfile(adsk.fusion.Profile profile) => Profile = profile;

        public adsk.fusion.Profile Profile { get; }
    }

    private sealed class AdskPath : IFusionPath
    {
        public AdskPath(adsk.fusion.Path path) => Path = path;

        public adsk.fusion.Path Path { get; }
    }

    private sealed class AdskConstructionAxis : IFusionConstructionAxis
    {
        public AdskConstructionAxis(adsk.fusion.ConstructionAxis axis) => AdskAxis = axis;

        public adsk.fusion.ConstructionAxis AdskAxis { get; }

        public string Name => AdskAxis.Name;
    }

    private sealed class AdskBaseFeature : IFusionBaseFeature
    {
        public AdskBaseFeature(adsk.fusion.BaseFeature feature) => AdskBase = feature;

        public adsk.fusion.BaseFeature AdskBase { get; }

        public void StartEdit() => AdskBase.StartEdit();
        public void FinishEdit() => AdskBase.FinishEdit();
    }

    private sealed class AdskBRepManager : IFusionBRepManager
    {
        private readonly adsk.fusion.TemporaryBRepManager _m;

        public AdskBRepManager(adsk.fusion.TemporaryBRepManager m) => _m = m;

        public IFusionBody CreateBox(FusionPoint center, FusionPoint xAxis, FusionPoint yAxis, double lengthCm, double widthCm, double heightCm)
            => new AdskBody(_m.CreateBox(adsk.core.OrientedBoundingBox3D.Create(
                ToPoint3D(center), ToVector3D(xAxis), ToVector3D(yAxis), lengthCm, widthCm, heightCm)));

        public IFusionBody CreateCylinderOrCone(FusionPoint basePoint, double baseRadiusCm, FusionPoint topPoint, double topRadiusCm)
            => new AdskBody(_m.CreateCylinderOrCone(ToPoint3D(basePoint), baseRadiusCm, ToPoint3D(topPoint), topRadiusCm));

        public IFusionBody CreateSphere(FusionPoint center, double radiusCm)
            => new AdskBody(_m.CreateSphere(ToPoint3D(center), radiusCm));

        public IFusionBody CreateTorus(FusionPoint center, FusionPoint axis, double majorRadiusCm, double minorRadiusCm)
            => new AdskBody(_m.CreateTorus(ToPoint3D(center), ToVector3D(axis), majorRadiusCm, minorRadiusCm));
    }

    private sealed class AdskFeature : IFusionFeature
    {
        private readonly adsk.fusion.Feature _f;

        public AdskFeature(adsk.fusion.Feature feature) => _f = feature;

        public string Name => _f.Name;

        /// <summary>Reference feat.bodies.item(0) (create_box_parametric body rename, flange/bend results).</summary>
        public IFusionBody FirstBody => new AdskBody(_f.Bodies.Item(0));
    }

    private sealed class AdskHoleFeature : IFusionHoleFeature
    {
        private readonly adsk.fusion.HoleFeature _f;

        public AdskHoleFeature(adsk.fusion.HoleFeature feature) => _f = feature;

        public string Name => _f.Name;
        public double HoleDiameterCm => _f.HoleDiameter.Value;
        public IFusionBody FirstBody => new AdskBody(_f.Bodies.Item(0));

        public IReadOnlyList<string> BodyNames
        {
            get
            {
                var source = _f.Bodies;
                var names = new List<string>(source.Count);
                for (var i = 0; i < source.Count; i++)
                {
                    names.Add(source.Item(i).Name);
                }

                return names;
            }
        }
    }

    private sealed class AdskThreadDataQuery : IFusionThreadDataQuery
    {
        public AdskThreadDataQuery(adsk.fusion.ThreadDataQuery query) => AdskQuery = query;

        public adsk.fusion.ThreadDataQuery AdskQuery { get; }

        public string ThreadType
        {
            get => AdskQuery.ThreadType;
            set => AdskQuery.ThreadType = value;
        }
    }

    private sealed class AdskFeatures : IFusionFeatures
    {
        private readonly adsk.fusion.FeatureCollection _c;

        public AdskFeatures(adsk.fusion.FeatureCollection c) => _c = c;

        public IFusionExtrudeFeatures ExtrudeFeatures => new AdskExtrudeFeatures(_c.ExtrudeFeatures);
        public IFusionRevolveFeatures RevolveFeatures => new AdskRevolveFeatures(_c.RevolveFeatures);
        public IFusionSweepFeatures SweepFeatures => new AdskSweepFeatures(_c.SweepFeatures);
        public IFusionLoftFeatures LoftFeatures => new AdskLoftFeatures(_c.LoftFeatures);
        public IFusionFilletFeatures FilletFeatures => new AdskFilletFeatures(_c.FilletFeatures);
        public IFusionChamferFeatures ChamferFeatures => new AdskChamferFeatures(_c.ChamferFeatures);
        public IFusionShellFeatures ShellFeatures => new AdskShellFeatures(_c.ShellFeatures);
        public IFusionMirrorFeatures MirrorFeatures => new AdskMirrorFeatures(_c.MirrorFeatures);
        public IFusionHoleFeatures HoleFeatures => new AdskHoleFeatures(_c.HoleFeatures);
        public IFusionRectPatternFeatures RectangularPatternFeatures => new AdskRectPatternFeatures(_c.RectangularPatternFeatures);
        public IFusionCirclePatternFeatures CircularPatternFeatures => new AdskCirclePatternFeatures(_c.CircularPatternFeatures);
        public IFusionThreadFeatures ThreadFeatures => new AdskThreadFeatures(_c.ThreadFeatures);
        public IFusionDraftFeatures DraftFeatures => new AdskDraftFeatures(_c.DraftFeatures);
        public IFusionSplitBodyFeatures SplitBodyFeatures => new AdskSplitBodyFeatures(_c.SplitBodyFeatures);
        public IFusionSplitFaceFeatures SplitFaceFeatures => new AdskSplitFaceFeatures(_c.SplitFaceFeatures);
        public IFusionOffsetFeatures OffsetFeatures => new AdskOffsetFeatures(_c.OffsetFeatures);
        public IFusionScaleFeatures ScaleFeatures => new AdskScaleFeatures(_c.ScaleFeatures);
        public IFusionMoveFeatures MoveFeatures => new AdskMoveFeatures(_c.MoveFeatures);
        public IFusionCombineFeatures CombineFeatures => new AdskCombineFeatures(_c.CombineFeatures);
        public IFusionPatchFeatures PatchFeatures => new AdskPatchFeatures(_c.PatchFeatures);
        public IFusionStitchFeatures StitchFeatures => new AdskStitchFeatures(_c.StitchFeatures);
        public IFusionThickenFeatures ThickenFeatures => new AdskThickenFeatures(_c.ThickenFeatures);
        public IFusionRuledSurfaceFeatures RuledSurfaceFeatures => new AdskRuledSurfaceFeatures(_c.RuledSurfaceFeatures);
        public IFusionTrimFeatures TrimFeatures => new AdskTrimFeatures(_c.TrimFeatures);
        public IFusionBaseFeatures BaseFeatures => new AdskBaseFeatures(_c.BaseFeatures);

        // T7 sheet metal (create_flange / create_bend / flat_pattern / unfold).
        public IFusionFlangeFeatures FlangeFeatures => new AdskFlangeFeatures(_c.FlangeFeatures);
        public IFusionBendFeatures BendFeatures => new AdskBendFeatures(_c.BendFeatures);
        public IFusionFlatPatternFeatures FlatPatternFeatures => new AdskFlatPatternFeatures(_c.FlatPatternFeatures);
        public IFusionUnfoldFeatures UnfoldFeatures => new AdskUnfoldFeatures(_c.UnfoldFeatures);

        public IFusionPath CreatePath(IFusionSketchCurve curve)
            => new AdskPath(_c.CreatePath(((AdskSketchCurve)curve).AdskCurve));
    }

    private sealed class AdskFlangeFeatures : IFusionFlangeFeatures
    {
        private readonly adsk.fusion.FlangeFeatureCollection _c;

        public AdskFlangeFeatures(adsk.fusion.FlangeFeatureCollection c) => _c = c;

        /// <summary>Reference create_flange: flangeFeatures.createInput(edge, True).</summary>
        public IFusionFlangeInput CreateInput(IFusionBodyEdge edge)
            => new AdskFlangeInput(_c, _c.CreateInput(((AdskBodyEdge)edge).AdskEdge, true));
    }

    private sealed class AdskFlangeInput : IFusionFlangeInput
    {
        private readonly adsk.fusion.FlangeFeatureCollection _c;
        private readonly adsk.fusion.FlangeFeatureInput _i;

        public AdskFlangeInput(adsk.fusion.FlangeFeatureCollection c, adsk.fusion.FlangeFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        /// <summary>Reference inp.angle = valueInput.createByString($"{angle} deg").</summary>
        public void SetAngleDegrees(double angleDegrees)
            => _i.Angle = ValueInput.CreateByString(FormattedAngle(angleDegrees));

        /// <summary>Reference inp.height = valueInput.createByReal(height) (cm).</summary>
        public void SetHeightCm(double heightCm)
            => _i.Height = ValueInput.CreateByReal(heightCm);

        public void SetBendRadiusCm(double bendRadiusCm)
            => _i.BendRadius = ValueInput.CreateByReal(bendRadiusCm);

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskBendFeatures : IFusionBendFeatures
    {
        private readonly adsk.fusion.BendFeatureCollection _c;

        public AdskBendFeatures(adsk.fusion.BendFeatureCollection c) => _c = c;

        /// <summary>Reference create_bend: bendFeatures.createInput(body, bendLine, True).</summary>
        public IFusionBendInput CreateInput(IFusionBody body, IFusionSketchCurve bendLine)
            => new AdskBendInput(_c, _c.CreateInput(
                ((AdskBody)body).Body,
                (adsk.fusion.SketchLine)((AdskSketchCurve)bendLine).AdskCurve,
                true));
    }

    private sealed class AdskBendInput : IFusionBendInput
    {
        private readonly adsk.fusion.BendFeatureCollection _c;
        private readonly adsk.fusion.BendFeatureInput _i;

        public AdskBendInput(adsk.fusion.BendFeatureCollection c, adsk.fusion.BendFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        /// <summary>Reference inp.bendAngle = valueInput.createByString($"{angle} deg").</summary>
        public void SetBendAngleDegrees(double bendAngleDegrees)
            => _i.BendAngle = ValueInput.CreateByString(FormattedAngle(bendAngleDegrees));

        public void SetBendRadiusCm(double bendRadiusCm)
            => _i.BendRadius = ValueInput.CreateByReal(bendRadiusCm);

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskFlatPatternFeatures : IFusionFlatPatternFeatures
    {
        private readonly adsk.fusion.FlatPatternFeatureCollection _c;

        public AdskFlatPatternFeatures(adsk.fusion.FlatPatternFeatureCollection c) => _c = c;

        /// <summary>Reference flat_pattern: flatPatternFeatures.createInput(body).</summary>
        public IFusionFlatPatternInput CreateInput(IFusionBody body)
            => new AdskFlatPatternInput(_c, _c.CreateInput(((AdskBody)body).Body));

        public IFusionFeature Add(IFusionFlatPatternInput input)
        {
            var concrete = (AdskFlatPatternInput)input;
            return new AdskFeature(concrete._c.Add(concrete._i));
        }
    }

    private sealed class AdskFlatPatternInput : IFusionFlatPatternInput
    {
        internal readonly adsk.fusion.FlatPatternFeatureCollection _c;
        internal readonly adsk.fusion.FlatPatternFeatureInput _i;

        public AdskFlatPatternInput(adsk.fusion.FlatPatternFeatureCollection c, adsk.fusion.FlatPatternFeatureInput i)
        {
            _c = c;
            _i = i;
        }
    }

    private sealed class AdskUnfoldFeatures : IFusionUnfoldFeatures
    {
        private readonly adsk.fusion.UnfoldFeatureCollection _c;

        public AdskUnfoldFeatures(adsk.fusion.UnfoldFeatureCollection c) => _c = c;

        /// <summary>Reference unfold: unfoldFeatures.createInput(bends, stationaryFace).</summary>
        public IFusionUnfoldInput CreateInput(IReadOnlyList<IFusionFace> bends, IFusionFace stationary)
            => new AdskUnfoldInput(
                _c,
                _c.CreateInput(
                    AdskColl(bends.Select(f => (object)((AdskFace)f).Face)),
                    ((AdskFace)stationary).Face));

        public IFusionFeature Add(IFusionUnfoldInput input)
        {
            var concrete = (AdskUnfoldInput)input;
            return new AdskFeature(concrete._c.Add(concrete._i));
        }
    }

    private sealed class AdskUnfoldInput : IFusionUnfoldInput
    {
        internal readonly adsk.fusion.UnfoldFeatureCollection _c;
        internal readonly adsk.fusion.UnfoldFeatureInput _i;

        public AdskUnfoldInput(adsk.fusion.UnfoldFeatureCollection c, adsk.fusion.UnfoldFeatureInput i)
        {
            _c = c;
            _i = i;
        }
    }

    /// <summary>Reference degree strings go through <c>createByString("{deg} deg")</c> — invariant culture.</summary>
    private static string FormattedAngle(double degrees)
        => degrees.ToString(System.Globalization.CultureInfo.InvariantCulture) + " deg";

    private sealed class AdskExtrudeFeatures : IFusionExtrudeFeatures
    {
        private readonly adsk.fusion.ExtrudeFeatureCollection _c;

        public AdskExtrudeFeatures(adsk.fusion.ExtrudeFeatureCollection c) => _c = c;

        public IFusionExtrudeInput CreateInput(IFusionProfile profile, FusionFeatureOperation operation)
            => new AdskExtrudeInput(_c, _c.CreateInput(((AdskProfile)profile).Profile, ToAdskOp(operation)));
    }

    private sealed class AdskExtrudeInput : IFusionExtrudeInput
    {
        private readonly adsk.fusion.ExtrudeFeatureCollection _c;
        private readonly adsk.fusion.ExtrudeFeatureInput _i;

        public AdskExtrudeInput(adsk.fusion.ExtrudeFeatureCollection c, adsk.fusion.ExtrudeFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public void SetDistanceExtent(bool reverse, double distanceCm)
            => _i.SetDistanceExtent(reverse, ValueInput.CreateByReal(distanceCm));

        /// <summary>
        /// Reference create_box_parametric's parametric height:
        /// <c>setDistanceExtent(False, valueInput.createByString(expr))</c>.
        /// </summary>
        public void SetDistanceExtentExpression(string expression)
            => _i.SetDistanceExtent(false, ValueInput.CreateByString(expression));

        public void SetSymmetricExtent(double distanceCm, bool mirror)
            => _i.SetSymmetricExtent(ValueInput.CreateByReal(distanceCm), mirror);

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskRevolveFeatures : IFusionRevolveFeatures
    {
        private readonly adsk.fusion.RevolveFeatureCollection _c;

        public AdskRevolveFeatures(adsk.fusion.RevolveFeatureCollection c) => _c = c;

        public IFusionRevolveInput CreateInput(IFusionProfile profile, IFusionRevolveAxis axis, FusionFeatureOperation operation)
            => new AdskRevolveInput(_c, _c.CreateInput(
                ((AdskProfile)profile).Profile, AdskRevolveAxis(axis), ToAdskOp(operation)));
    }

    private sealed class AdskRevolveInput : IFusionRevolveInput
    {
        private readonly adsk.fusion.RevolveFeatureCollection _c;
        private readonly adsk.fusion.RevolveFeatureInput _i;

        public AdskRevolveInput(adsk.fusion.RevolveFeatureCollection c, adsk.fusion.RevolveFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public void SetAngleExtent(bool reverse, double angleDegrees)
            => _i.SetAngleExtent(reverse, ValueInput.CreateByReal(angleDegrees));

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskSweepFeatures : IFusionSweepFeatures
    {
        private readonly adsk.fusion.SweepFeatureCollection _c;

        public AdskSweepFeatures(adsk.fusion.SweepFeatureCollection c) => _c = c;

        public IFusionSweepInput CreateInput(IFusionProfile profile, IFusionPath path, FusionFeatureOperation operation)
            => new AdskSweepInput(_c, _c.CreateInput(
                ((AdskProfile)profile).Profile, ((AdskPath)path).Path, ToAdskOp(operation)));
    }

    private sealed class AdskSweepInput : IFusionSweepInput
    {
        private readonly adsk.fusion.SweepFeatureCollection _c;
        private readonly adsk.fusion.SweepFeatureInput _i;

        public AdskSweepInput(adsk.fusion.SweepFeatureCollection c, adsk.fusion.SweepFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskLoftFeatures : IFusionLoftFeatures
    {
        private readonly adsk.fusion.LoftFeatureCollection _c;

        public AdskLoftFeatures(adsk.fusion.LoftFeatureCollection c) => _c = c;

        public IFusionLoftInput CreateInput(FusionFeatureOperation operation)
            => new AdskLoftInput(_c, _c.CreateInput(ToAdskOp(operation)));
    }

    private sealed class AdskLoftInput : IFusionLoftInput
    {
        private readonly adsk.fusion.LoftFeatureCollection _c;
        private readonly adsk.fusion.LoftFeatureInput _i;

        public AdskLoftInput(adsk.fusion.LoftFeatureCollection c, adsk.fusion.LoftFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public void AddSection(IFusionProfile profile)
            => _i.LoftSections.Add(((AdskProfile)profile).Profile);

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskFilletFeatures : IFusionFilletFeatures
    {
        private readonly adsk.fusion.FilletFeatureCollection _c;

        public AdskFilletFeatures(adsk.fusion.FilletFeatureCollection c) => _c = c;

        public IFusionFilletInput CreateInput() => new AdskFilletInput(_c, _c.CreateInput());
    }

    private sealed class AdskFilletInput : IFusionFilletInput
    {
        private readonly adsk.fusion.FilletFeatureCollection _c;
        private readonly adsk.fusion.FilletFeatureInput _i;

        public AdskFilletInput(adsk.fusion.FilletFeatureCollection c, adsk.fusion.FilletFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public void AddConstantRadiusEdgeSet(IReadOnlyList<IFusionBodyEdge> edges, double radiusCm, bool includeAdjacent)
            => _i.AddConstantRadiusEdgeSet(
                AdskColl(edges.Select(e => (object)((AdskBodyEdge)e).AdskEdge)),
                ValueInput.CreateByReal(radiusCm),
                includeAdjacent);

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskChamferFeatures : IFusionChamferFeatures
    {
        private readonly adsk.fusion.ChamferFeatureCollection _c;

        public AdskChamferFeatures(adsk.fusion.ChamferFeatureCollection c) => _c = c;

        public IFusionChamferInput CreateInput(IReadOnlyList<IFusionBodyEdge> edges, bool includeAdjacent)
            => new AdskChamferInput(_c, _c.CreateInput(
                AdskColl(edges.Select(e => (object)((AdskBodyEdge)e).AdskEdge)), includeAdjacent));
    }

    private sealed class AdskChamferInput : IFusionChamferInput
    {
        private readonly adsk.fusion.ChamferFeatureCollection _c;
        private readonly adsk.fusion.ChamferFeatureInput _i;

        public AdskChamferInput(adsk.fusion.ChamferFeatureCollection c, adsk.fusion.ChamferFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public void SetToEqualDistance(double distanceCm)
            => _i.SetToEqualDistance(ValueInput.CreateByReal(distanceCm));

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskShellFeatures : IFusionShellFeatures
    {
        private readonly adsk.fusion.ShellFeatureCollection _c;

        public AdskShellFeatures(adsk.fusion.ShellFeatureCollection c) => _c = c;

        public IFusionShellInput CreateInput(IReadOnlyList<IFusionBody> bodies)
            => new AdskShellInput(_c, _c.CreateInput(
                AdskColl(bodies.Select(b => (object)((AdskBody)b).Body))));
    }

    private sealed class AdskShellInput : IFusionShellInput
    {
        private readonly adsk.fusion.ShellFeatureCollection _c;
        private readonly adsk.fusion.ShellFeatureInput _i;

        public AdskShellInput(adsk.fusion.ShellFeatureCollection c, adsk.fusion.ShellFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public IReadOnlyList<IFusionFace> FacesToRemove
        {
            set => _i.FacesToRemove = AdskColl(value.Select(f => (object)((AdskFace)f).Face));
        }

        public double InsideThicknessCm
        {
            set => _i.InsideThickness = ValueInput.CreateByReal(value);
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskMirrorFeatures : IFusionMirrorFeatures
    {
        private readonly adsk.fusion.MirrorFeatureCollection _c;

        public AdskMirrorFeatures(adsk.fusion.MirrorFeatureCollection c) => _c = c;

        public IFusionMirrorInput CreateInput(IReadOnlyList<IFusionBody> bodies, IFusionConstructionPlane plane)
            => new AdskMirrorInput(_c, _c.CreateInput(
                AdskColl(bodies.Select(b => (object)((AdskBody)b).Body)),
                ((AdskConstructionPlane)plane).AdskPlane));
    }

    private sealed class AdskMirrorInput : IFusionMirrorInput
    {
        private readonly adsk.fusion.MirrorFeatureCollection _c;
        private readonly adsk.fusion.MirrorFeatureInput _i;

        public AdskMirrorInput(adsk.fusion.MirrorFeatureCollection c, adsk.fusion.MirrorFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskHoleFeatures : IFusionHoleFeatures
    {
        private readonly adsk.fusion.HoleFeatureCollection _c;

        public AdskHoleFeatures(adsk.fusion.HoleFeatureCollection c) => _c = c;

        public IFusionHoleInput CreateSimpleInput(double diameterCm)
            => new AdskHoleInput(_c, _c.CreateSimpleInput(ValueInput.CreateByReal(diameterCm)));
    }

    private sealed class AdskHoleInput : IFusionHoleInput
    {
        private readonly adsk.fusion.HoleFeatureCollection _c;
        private readonly adsk.fusion.HoleFeatureInput _i;

        public AdskHoleInput(adsk.fusion.HoleFeatureCollection c, adsk.fusion.HoleFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public void SetPositionBySketchPoint(IFusionSketchPoint point)
            => _i.SetPositionBySketchPoint(((AdskSketchPoint)point)._point);

        public void SetDistanceExtent(double depthCm)
            => _i.SetDistanceExtent(ValueInput.CreateByReal(depthCm));

        public IReadOnlyList<IFusionBody> ParticipantBodies
        {
            set
            {
                _i.ParticipantBodies = value
                    .Select(b => ((AdskBody)b).Body)
                    .ToList();
            }
        }

        public IFusionHoleFeature Add() => new AdskHoleFeature(_c.Add(_i));
    }

    private sealed class AdskRectPatternFeatures : IFusionRectPatternFeatures
    {
        private readonly adsk.fusion.RectangularPatternFeatureCollection _c;

        public AdskRectPatternFeatures(adsk.fusion.RectangularPatternFeatureCollection c) => _c = c;

        public IFusionRectPatternInput CreateInput(IReadOnlyList<IFusionBody> bodies, IFusionConstructionAxis xAxis, int xCount, double xSpacingCm)
            => new AdskRectPatternInput(_c, _c.CreateInput(
                AdskColl(bodies.Select(b => (object)((AdskBody)b).Body)),
                ((AdskConstructionAxis)xAxis).AdskAxis,
                ValueInput.CreateByReal(xCount),
                ValueInput.CreateByReal(xSpacingCm),
                adsk.fusion.PatternDistanceType.SpacingPatternDistanceType));
    }

    private sealed class AdskRectPatternInput : IFusionRectPatternInput
    {
        private readonly adsk.fusion.RectangularPatternFeatureCollection _c;
        private readonly adsk.fusion.RectangularPatternFeatureInput _i;

        public AdskRectPatternInput(adsk.fusion.RectangularPatternFeatureCollection c, adsk.fusion.RectangularPatternFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public void SetDirectionTwo(IFusionConstructionAxis yAxis, int yCount, double ySpacingCm)
            => _i.SetDirectionTwo(
                ((AdskConstructionAxis)yAxis).AdskAxis,
                ValueInput.CreateByReal(yCount),
                ValueInput.CreateByReal(ySpacingCm));

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskCirclePatternFeatures : IFusionCirclePatternFeatures
    {
        private readonly adsk.fusion.CircularPatternFeatureCollection _c;

        public AdskCirclePatternFeatures(adsk.fusion.CircularPatternFeatureCollection c) => _c = c;

        public IFusionCirclePatternInput CreateInput(IReadOnlyList<IFusionBody> bodies, IFusionConstructionAxis axis)
            => new AdskCirclePatternInput(_c, _c.CreateInput(
                AdskColl(bodies.Select(b => (object)((AdskBody)b).Body)),
                ((AdskConstructionAxis)axis).AdskAxis));
    }

    private sealed class AdskCirclePatternInput : IFusionCirclePatternInput
    {
        private readonly adsk.fusion.CircularPatternFeatureCollection _c;
        private readonly adsk.fusion.CircularPatternFeatureInput _i;

        public AdskCirclePatternInput(adsk.fusion.CircularPatternFeatureCollection c, adsk.fusion.CircularPatternFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public int Quantity
        {
            set => _i.Quantity = ValueInput.CreateByReal(value);
        }

        public double TotalAngleDegrees
        {
            set => _i.TotalAngle = ValueInput.CreateByString($"{value} deg");
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskThreadFeatures : IFusionThreadFeatures
    {
        private readonly adsk.fusion.ThreadFeatureCollection _c;

        public AdskThreadFeatures(adsk.fusion.ThreadFeatureCollection c) => _c = c;

        public IFusionThreadDataQuery ThreadDataQuery => new AdskThreadDataQuery(_c.ThreadDataQuery);

        public IFusionThreadInput CreateInput(IFusionFace face, IFusionThreadDataQuery data)
            => new AdskThreadInput(_c, _c.CreateInput(
                ((AdskFace)face).Face, ((AdskThreadDataQuery)data).AdskQuery));
    }

    private sealed class AdskThreadInput : IFusionThreadInput
    {
        private readonly adsk.fusion.ThreadFeatureCollection _c;
        private readonly adsk.fusion.ThreadFeatureInput _i;

        public AdskThreadInput(adsk.fusion.ThreadFeatureCollection c, adsk.fusion.ThreadFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public bool IsModeled
        {
            set => _i.IsModeled = value;
        }

        public bool IsFullLength
        {
            set => _i.IsFullLength = value;
        }

        public double ThreadLengthCm
        {
            set => _i.ThreadLength = ValueInput.CreateByReal(value);
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskDraftFeatures : IFusionDraftFeatures
    {
        private readonly adsk.fusion.DraftFeatureCollection _c;

        public AdskDraftFeatures(adsk.fusion.DraftFeatureCollection c) => _c = c;

        public IFusionDraftInput CreateInput(IReadOnlyList<IFusionFace> faces, IFusionConstructionPlane pullPlane, bool isTangentChain)
            => new AdskDraftInput(_c, _c.CreateInput(
                faces.Select(f => ((AdskFace)f).Face).ToList(),
                ((AdskConstructionPlane)pullPlane).AdskPlane,
                isTangentChain));
    }

    private sealed class AdskDraftInput : IFusionDraftInput
    {
        private readonly adsk.fusion.DraftFeatureCollection _c;
        private readonly adsk.fusion.DraftFeatureInput _i;

        public AdskDraftInput(adsk.fusion.DraftFeatureCollection c, adsk.fusion.DraftFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public void SetSingleAngle(bool twoSided, double angleDegrees)
            => _i.SetSingleAngle(twoSided, ValueInput.CreateByString($"{angleDegrees} deg"));

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskSplitBodyFeatures : IFusionSplitBodyFeatures
    {
        private readonly adsk.fusion.SplitBodyFeatureCollection _c;

        public AdskSplitBodyFeatures(adsk.fusion.SplitBodyFeatureCollection c) => _c = c;

        public IFusionSplitBodyInput CreateBodyInput(IFusionBody body, IFusionBody tool, bool extendTool)
            => new AdskSplitBodyInput(_c, _c.CreateInput(
                ((AdskBody)body).Body, ((AdskBody)tool).Body, extendTool));

        public IFusionSplitBodyInput CreatePlaneInput(IFusionBody body, IFusionConstructionPlane plane, bool extendTool)
            => new AdskSplitBodyInput(_c, _c.CreateInput(
                ((AdskBody)body).Body, ((AdskConstructionPlane)plane).AdskPlane, extendTool));
    }

    private sealed class AdskSplitBodyInput : IFusionSplitBodyInput
    {
        private readonly adsk.fusion.SplitBodyFeatureCollection _c;
        private readonly adsk.fusion.SplitBodyFeatureInput _i;

        public AdskSplitBodyInput(adsk.fusion.SplitBodyFeatureCollection c, adsk.fusion.SplitBodyFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskSplitFaceFeatures : IFusionSplitFaceFeatures
    {
        private readonly adsk.fusion.SplitFaceFeatureCollection _c;

        public AdskSplitFaceFeatures(adsk.fusion.SplitFaceFeatureCollection c) => _c = c;

        public IFusionSplitFaceInput CreateInput(IReadOnlyList<IFusionFace> faces, IFusionConstructionPlane plane, bool extendTool)
            => new AdskSplitFaceInput(_c, _c.CreateInput(
                AdskColl(faces.Select(f => (object)((AdskFace)f).Face)),
                ((AdskConstructionPlane)plane).AdskPlane,
                extendTool));
    }

    private sealed class AdskSplitFaceInput : IFusionSplitFaceInput
    {
        private readonly adsk.fusion.SplitFaceFeatureCollection _c;
        private readonly adsk.fusion.SplitFaceFeatureInput _i;

        public AdskSplitFaceInput(adsk.fusion.SplitFaceFeatureCollection c, adsk.fusion.SplitFaceFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskOffsetFeatures : IFusionOffsetFeatures
    {
        private readonly adsk.fusion.OffsetFeatureCollection _c;

        public AdskOffsetFeatures(adsk.fusion.OffsetFeatureCollection c) => _c = c;

        public IFusionOffsetInput CreateInput(IReadOnlyList<IFusionFace> faces, double distanceCm, FusionFeatureOperation operation)
            => new AdskOffsetInput(_c, _c.CreateInput(
                AdskColl(faces.Select(f => (object)((AdskFace)f).Face)),
                ValueInput.CreateByReal(distanceCm),
                ToAdskOp(operation)));
    }

    private sealed class AdskOffsetInput : IFusionOffsetInput
    {
        private readonly adsk.fusion.OffsetFeatureCollection _c;
        private readonly adsk.fusion.OffsetFeatureInput _i;

        public AdskOffsetInput(adsk.fusion.OffsetFeatureCollection c, adsk.fusion.OffsetFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskScaleFeatures : IFusionScaleFeatures
    {
        private readonly adsk.fusion.ScaleFeatureCollection _c;

        public AdskScaleFeatures(adsk.fusion.ScaleFeatureCollection c) => _c = c;

        public IFusionScaleInput CreateUniformInput(IReadOnlyList<IFusionBody> bodies, FusionPoint anchor, double scale)
            => new AdskScaleInput(_c, _c.CreateInput(
                AdskColl(bodies.Select(b => (object)((AdskBody)b).Body)),
                ToPoint3D(anchor),
                ValueInput.CreateByReal(scale)));

        public IFusionScaleInput CreateAxisInput(IReadOnlyList<IFusionBody> bodies, FusionPoint anchor, double scaleX, double scaleY, double scaleZ)
            => new AdskScaleInput(_c, _c.CreateInput(
                AdskColl(bodies.Select(b => (object)((AdskBody)b).Body)),
                ToPoint3D(anchor),
                ValueInput.CreateByReal(scaleX),
                ValueInput.CreateByReal(scaleY),
                ValueInput.CreateByReal(scaleZ)));
    }

    private sealed class AdskScaleInput : IFusionScaleInput
    {
        private readonly adsk.fusion.ScaleFeatureCollection _c;
        private readonly adsk.fusion.ScaleFeatureInput _i;

        public AdskScaleInput(adsk.fusion.ScaleFeatureCollection c, adsk.fusion.ScaleFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskMoveFeatures : IFusionMoveFeatures
    {
        private readonly adsk.fusion.MoveFeatureCollection _c;

        public AdskMoveFeatures(adsk.fusion.MoveFeatureCollection c) => _c = c;

        public IFusionMoveInput CreateInput(IReadOnlyList<IFusionBody> bodies, FusionPoint translationCm)
        {
            // Reference move_body: a translation-only matrix (adsk.core.Matrix3D).
            var transform = adsk.core.Matrix3D.Create();
            transform.Translation = adsk.core.Vector3D.Create(translationCm.X, translationCm.Y, translationCm.Z);
            return new AdskMoveInput(_c, _c.CreateInput(
                AdskColl(bodies.Select(b => (object)((AdskBody)b).Body)), transform));
        }
    }

    private sealed class AdskMoveInput : IFusionMoveInput
    {
        private readonly adsk.fusion.MoveFeatureCollection _c;
        private readonly adsk.fusion.MoveFeatureInput _i;

        public AdskMoveInput(adsk.fusion.MoveFeatureCollection c, adsk.fusion.MoveFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskCombineFeatures : IFusionCombineFeatures
    {
        private readonly adsk.fusion.CombineFeatureCollection _c;

        public AdskCombineFeatures(adsk.fusion.CombineFeatureCollection c) => _c = c;

        public IFusionCombineInput CreateInput(IFusionBody target, IReadOnlyList<IFusionBody> tools)
            => new AdskCombineInput(_c, _c.CreateInput(
                ((AdskBody)target).Body,
                AdskColl(tools.Select(b => (object)((AdskBody)b).Body))));
    }

    private sealed class AdskCombineInput : IFusionCombineInput
    {
        private readonly adsk.fusion.CombineFeatureCollection _c;
        private readonly adsk.fusion.CombineFeatureInput _i;

        public AdskCombineInput(adsk.fusion.CombineFeatureCollection c, adsk.fusion.CombineFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public FusionFeatureOperation Operation
        {
            set => _i.Operation = ToAdskOp(value);
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskPatchFeatures : IFusionPatchFeatures
    {
        private readonly adsk.fusion.PatchFeatureCollection _c;

        public AdskPatchFeatures(adsk.fusion.PatchFeatureCollection c) => _c = c;

        public IFusionPatchInput CreateInput(IFusionProfile profile, FusionFeatureOperation operation)
            => new AdskPatchInput(_c, _c.CreateInput(((AdskProfile)profile).Profile, ToAdskOp(operation)));
    }

    private sealed class AdskPatchInput : IFusionPatchInput
    {
        private readonly adsk.fusion.PatchFeatureCollection _c;
        private readonly adsk.fusion.PatchFeatureInput _i;

        public AdskPatchInput(adsk.fusion.PatchFeatureCollection c, adsk.fusion.PatchFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public FusionSurfaceContinuity BoundaryContinuity
        {
            set => _i.BoundaryContinuity = ToAdskCont(value);
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskStitchFeatures : IFusionStitchFeatures
    {
        private readonly adsk.fusion.StitchFeatureCollection _c;

        public AdskStitchFeatures(adsk.fusion.StitchFeatureCollection c) => _c = c;

        public IFusionStitchInput CreateInput(IReadOnlyList<IFusionBody> bodies, double toleranceCm, FusionFeatureOperation operation)
            => new AdskStitchInput(_c, _c.CreateInput(
                AdskColl(bodies.Select(b => (object)((AdskBody)b).Body)),
                ValueInput.CreateByReal(toleranceCm),
                ToAdskOp(operation)));
    }

    private sealed class AdskStitchInput : IFusionStitchInput
    {
        private readonly adsk.fusion.StitchFeatureCollection _c;
        private readonly adsk.fusion.StitchFeatureInput _i;

        public AdskStitchInput(adsk.fusion.StitchFeatureCollection c, adsk.fusion.StitchFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskThickenFeatures : IFusionThickenFeatures
    {
        private readonly adsk.fusion.ThickenFeatureCollection _c;

        public AdskThickenFeatures(adsk.fusion.ThickenFeatureCollection c) => _c = c;

        public IFusionThickenInput CreateInput(IReadOnlyList<IFusionFace> faces, double thicknessCm, bool symmetric, FusionFeatureOperation operation)
            => new AdskThickenInput(_c, _c.CreateInput(
                AdskColl(faces.Select(f => (object)((AdskFace)f).Face)),
                ValueInput.CreateByReal(thicknessCm),
                false,
                ToAdskOp(operation),
                symmetric));
    }

    private sealed class AdskThickenInput : IFusionThickenInput
    {
        private readonly adsk.fusion.ThickenFeatureCollection _c;
        private readonly adsk.fusion.ThickenFeatureInput _i;

        public AdskThickenInput(adsk.fusion.ThickenFeatureCollection c, adsk.fusion.ThickenFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskRuledSurfaceFeatures : IFusionRuledSurfaceFeatures
    {
        private readonly adsk.fusion.RuledSurfaceFeatureCollection _c;

        public AdskRuledSurfaceFeatures(adsk.fusion.RuledSurfaceFeatureCollection c) => _c = c;

        public IFusionRuledInput CreateInput(IFusionBodyEdge edge, double distanceCm)
            => new AdskRuledInput(_c, _c.CreateInput(((AdskBodyEdge)edge).AdskEdge, ValueInput.CreateByReal(distanceCm)));
    }

    private sealed class AdskRuledInput : IFusionRuledInput
    {
        private readonly adsk.fusion.RuledSurfaceFeatureCollection _c;
        private readonly adsk.fusion.RuledSurfaceFeatureInput _i;

        public AdskRuledInput(adsk.fusion.RuledSurfaceFeatureCollection c, adsk.fusion.RuledSurfaceFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskTrimFeatures : IFusionTrimFeatures
    {
        private readonly adsk.fusion.TrimFeatureCollection _c;

        public AdskTrimFeatures(adsk.fusion.TrimFeatureCollection c) => _c = c;

        public IFusionTrimInput CreateInput(IFusionBody body, IFusionBody tool)
            => new AdskTrimInput(_c, _c.CreateInput(((AdskBody)body).Body, ((AdskBody)tool).Body));
    }

    private sealed class AdskTrimInput : IFusionTrimInput
    {
        private readonly adsk.fusion.TrimFeatureCollection _c;
        private readonly adsk.fusion.TrimFeatureInput _i;

        public AdskTrimInput(adsk.fusion.TrimFeatureCollection c, adsk.fusion.TrimFeatureInput i)
        {
            _c = c;
            _i = i;
        }

        public IFusionFeature Add() => new AdskFeature(_c.Add(_i));
    }

    private sealed class AdskBaseFeatures : IFusionBaseFeatures
    {
        private readonly adsk.fusion.BaseFeatureCollection _c;

        public AdskBaseFeatures(adsk.fusion.BaseFeatureCollection c) => _c = c;

        public IFusionBaseFeature Add() => new AdskBaseFeature(_c.Add());
    }

    // ── viewport / ui / ui commands ─────────────────────────────────

    private sealed class AdskViewport : IFusionViewport
    {
        private readonly adsk.core.Viewport _vp;

        public AdskViewport(adsk.core.Viewport viewport) => _vp = viewport;

        public IFusionCamera Camera
        {
            get => new AdskCamera(_vp.Camera);
            set
            {
                if (value is not AdskCamera camera)
                {
                    throw new ArgumentException("Camera must be the adapter's own camera wrapper.", nameof(value));
                }

                _vp.Camera = camera.Camera;
            }
        }

        public void Fit() => _vp.Fit();
        public void Refresh() => _vp.Refresh();
        public bool SaveAsImageFile(string path, int width, int height)
            => _vp.SaveAsImageFile(path, width, height);
    }

    private sealed class AdskCamera : IFusionCamera
    {
        public AdskCamera(adsk.core.Camera camera) => Camera = camera;

        public adsk.core.Camera Camera { get; }

        public FusionPoint Eye
        {
            get => ToPoint(Camera.Eye);
            set => Camera.Eye = ToPoint3D(value);
        }

        public FusionPoint Target
        {
            get => ToPoint(Camera.Target);
            set => Camera.Target = ToPoint3D(value);
        }

        public FusionPoint UpVector
        {
            get => ToPoint(Camera.UpVector);
            set => Camera.UpVector = ToVector3D(value);
        }

        public int CameraType
        {
            get => Camera.CameraType;
            set => Camera.CameraType = value;
        }

        public bool IsSmoothTransition
        {
            get => Camera.IsSmoothTransition;
            set => Camera.IsSmoothTransition = value;
        }

        /// <summary>Reference set_view: camera.isFitView = fit_view (T7 view fit).</summary>
        public bool IsFitView
        {
            get => Camera.IsFitView;
            set => Camera.IsFitView = value;
        }
    }

    private sealed class AdskUiCommands : IUiCommands
    {
        private readonly adsk.core.CommandDefinitions _commands;

        public AdskUiCommands(adsk.core.CommandDefinitions commands) => _commands = commands;

        public IUiCommandDefinition? ItemById(string id)
            => _commands.ItemById(id) is { } definition ? new AdskUiCommandDefinition(definition) : null;
    }

    private sealed class AdskUiCommandDefinition : IUiCommandDefinition
    {
        private readonly adsk.core.CommandDefinition _definition;

        public AdskUiCommandDefinition(adsk.core.CommandDefinition definition) => _definition = definition;

        public void Execute() => _definition.Execute();
    }

    // ── T7 inspection: measure manager ───────────────────────────────

    private sealed class AdskMeasureManager : IFusionMeasureManager
    {
        private readonly adsk.fusion.MeasureManager _m;

        public AdskMeasureManager(adsk.fusion.MeasureManager m) => _m = m;

        /// <summary>reference measure_distance: measureMinimumDistance(body|point, body|point).</summary>
        public IFusionDistanceMeasureResult MeasureMinimumDistance(
            IFusionMeasureEntity entityOne, IFusionMeasureEntity entityTwo)
            => new AdskDistanceMeasureResult(_m.MeasureMinimumDistance(MeasureEntity(entityOne), MeasureEntity(entityTwo)));

        /// <summary>reference measure_angle: measureAngle(face, face) — radians.</summary>
        public IFusionAngleMeasureResult MeasureAngle(IFusionFace faceOne, IFusionFace faceTwo)
            => new AdskAngleMeasureResult(_m.MeasureAngle(((AdskFace)faceOne).Face, ((AdskFace)faceTwo).Face));

        /// <summary>Unwrap a measure entity: an adsk body or a Point3D (reference get_entity).</summary>
        private static object MeasureEntity(IFusionMeasureEntity entity) => entity switch
        {
            AdskBody b => b.Body,
            FusionMeasurePoint p => adsk.core.Point3D.Create(p.Point.X, p.Point.Y, p.Point.Z),
            _ => throw new FusionCommandException("Measure entity must be a body or a point"),
        };
    }

    private sealed class AdskDistanceMeasureResult : IFusionDistanceMeasureResult
    {
        private readonly adsk.fusion.DistanceMeasureResult _r;

        public AdskDistanceMeasureResult(adsk.fusion.DistanceMeasureResult r) => _r = r;

        public double Value => _r.Value;
        public FusionPoint PointOnEntityOne => ToPoint(_r.PointOnEntityOne);
        public FusionPoint PointOnEntityTwo => ToPoint(_r.PointOnEntityTwo);
    }

    private sealed class AdskAngleMeasureResult : IFusionAngleMeasureResult
    {
        private readonly adsk.fusion.AngleMeasureResult _r;

        public AdskAngleMeasureResult(adsk.fusion.AngleMeasureResult r) => _r = r;

        /// <summary>Reference returns radians; the handler converts to degrees.</summary>
        public double ValueRadians => _r.Value;
    }

    private sealed class AdskPhysicalProperties : IFusionPhysicalProperties
    {
        private readonly adsk.fusion.PhysicalProperties _p;

        public AdskPhysicalProperties(adsk.fusion.PhysicalProperties p) => _p = p;

        public double MassKg => _p.Mass;
        public double VolumeCm3 => _p.Volume;
        public double AreaCm2 => _p.Area;
        public double Density => _p.Density;
        public FusionPoint CenterOfMass => ToPoint(_p.CenterOfMass);
    }

    // ── T7 inspection: section analysis + interference ───────────────

    private sealed class AdskAnalyses : IFusionAnalysisCollection
    {
        private readonly adsk.fusion.AnalysisCollection _c;

        public AdskAnalyses(adsk.fusion.AnalysisCollection c) => _c = c;

        /// <summary>Reference create_section_analysis: analyses.createInput().</summary>
        public IFusionSectionAnalysisInput CreateInput() => new AdskSectionAnalysisInput(_c.CreateInput());

        /// <summary>Reference create_section_analysis: analyses.add(input).</summary>
        public void Add(IFusionSectionAnalysisInput input)
            => _c.Add(((AdskSectionAnalysisInput)input).Input);
    }

    private sealed class AdskSectionAnalysisInput : IFusionSectionAnalysisInput
    {
        public AdskSectionAnalysisInput(adsk.fusion.SectionAnalysisInput input) => Input = input;

        public adsk.fusion.SectionAnalysisInput Input { get; }

        /// <summary>Reference: inp.plane = plane.</summary>
        public IFusionConstructionPlane? Plane
        {
            set => Input.Plane = value is AdskConstructionPlane p ? p.AdskPlane : null;
        }

        /// <summary>Reference: inp.distance = valueInput.createByReal(offset).</summary>
        public void SetDistanceCm(double distanceCm) => Input.Distance = ValueInput.CreateByReal(distanceCm);
    }

    private sealed class AdskInterferenceInput : IFusionInterferenceInput
    {
        public AdskInterferenceInput(adsk.fusion.InterferenceInput input) => Input = input;

        public adsk.fusion.InterferenceInput Input { get; }

        /// <summary>Reference check_interference: inp.areCoincidentFacesIncluded = bool.</summary>
        public bool IncludeCoincidentFaces
        {
            set => Input.AreCoincidentFacesIncluded = value;
        }
    }

    private sealed class AdskInterferenceResult : IFusionInterferenceResult
    {
        private readonly adsk.fusion.InterferenceResult _r;

        public AdskInterferenceResult(adsk.fusion.InterferenceResult r) => _r = r;

        /// <summary>Reference: result.entityOne.name.</summary>
        public string BodyOne => _r.EntityOne.Name;
        public string BodyTwo => _r.EntityTwo.Name;

        /// <summary>Reference: result.interferenceBody.volume (cm^3).</summary>
        public double InterferenceVolumeCm3 => _r.InterferenceBody.Volume;
    }

    // ── T7 mesh ──────────────────────────────────────────────────────

    private sealed class AdskMeshBodies : IFusionMeshBodyCollection
    {
        private readonly adsk.fusion.MeshBodyCollection _c;

        public AdskMeshBodies(adsk.fusion.MeshBodyCollection c) => _c = c;

        public int Count => _c.Count;
        public IFusionMeshBody Item(int index) => new AdskMeshBody(_c.Item(index));

        /// <summary>Reference import_mesh: root.meshBodies.add(file_path, units) (old version: addByFile).</summary>
        public IFusionMeshBodyList Add(string filePath, FusionMeshUnits units)
            => new AdskMeshBodyList(_c.Add(filePath, ToAdskMeshUnits(units)));
    }

    private sealed class AdskMeshBodyList : IFusionMeshBodyList
    {
        private readonly adsk.fusion.MeshBodyList _l;

        public AdskMeshBodyList(adsk.fusion.MeshBodyList list) => _l = list;

        public int Count => _l.Count;
        public IFusionMeshBody Item(int index) => new AdskMeshBody(_l.Item(index));
    }

    private sealed class AdskMeshBody : IFusionMeshBody
    {
        private readonly adsk.fusion.MeshBody _mb;

        public AdskMeshBody(adsk.fusion.MeshBody mb) => _mb = mb;

        public string Name => _mb.Name;

        public IFusionBoundingBox? BoundingBox
            => _mb.BoundingBox is { } bbox ? new AdskBoundingBox(bbox) : null;

        /// <summary>
        /// Reference compare_meshes: mesh = mb.mesh or mb.displayMesh; null when
        /// no polygon data (reference throws "Could not obtain polygon mesh data").
        /// </summary>
        public IReadOnlyList<double>? CompareWith(IFusionMeshBody other)
        {
            var meshA = _mb.Mesh ?? _mb.DisplayMesh;
            var meshB = ((AdskMeshBody)other)._mb.Mesh ?? ((AdskMeshBody)other)._mb.DisplayMesh;
            if (meshA is null || meshB is null)
            {
                return null;
            }

            return meshA.CompareWith(meshB);
        }
    }

    // ── T7 appearance / materials ────────────────────────────────────

    private sealed class AdskAppearance : IFusionAppearance
    {
        public AdskAppearance(adsk.fusion.Appearance appearance) => Appearance = appearance;

        public adsk.fusion.Appearance Appearance { get; }

        public string Name => Appearance.Name;

        /// <summary>Reference set_color: appearance.color = adsk.core.Color.create(r,g,b,a).</summary>
        public void SetColor(int red, int green, int blue, int alpha)
            => Appearance.Color = adsk.core.Color.Create(red, green, blue, alpha);
    }

    private sealed class AdskAppearances : IFusionAppearanceCollection
    {
        private readonly adsk.fusion.AppearanceCollection _c;

        public AdskAppearances(adsk.fusion.AppearanceCollection c) => _c = c;

        /// <summary>Reference: appearances.itemByName(name) — null when absent.</summary>
        public IFusionAppearance? ItemByName(string name)
            => _c.ItemByName(name) is { } a ? new AdskAppearance(a) : null;

        /// <summary>Reference set_appearance: appearances.addByCopy(source, name).</summary>
        public IFusionAppearance AddByCopy(IFusionAppearance source, string name)
            => new AdskAppearance(_c.AddByCopy(((AdskAppearance)source).Appearance, name));

        /// <summary>Reference set_color: appearances.add(name).</summary>
        public IFusionAppearance Add(string name) => new AdskAppearance(_c.Add(name));
    }

    private sealed class AdskMaterialLibraries : IFusionMaterialLibraries
    {
        private readonly adsk.fusion.MaterialLibraryCollection _c;

        public AdskMaterialLibraries(adsk.fusion.MaterialLibraryCollection c) => _c = c;

        public int Count => _c.Count;

        /// <summary>Reference set_appearance library scan: item(i) / itemByName(name).</summary>
        public IFusionMaterialLibrary? Item(int index)
            => _c.Item(index) is { } l ? new AdskMaterialLibrary(l) : null;

        public IFusionMaterialLibrary? ItemByName(string name)
            => _c.ItemByName(name) is { } l ? new AdskMaterialLibrary(l) : null;
    }

    private sealed class AdskMaterialLibrary : IFusionMaterialLibrary
    {
        private readonly adsk.fusion.MaterialLibrary _l;

        public AdskMaterialLibrary(adsk.fusion.MaterialLibrary l) => _l = l;

        public int AppearancesCount => _l.Appearances.Count;

        /// <summary>Reference set_appearance: iterates lib.appearances to find the match.</summary>
        public IReadOnlyList<IFusionAppearance> Appearances
        {
            get
            {
                var src = _l.Appearances;
                var list = new List<IFusionAppearance>(src.Count);
                for (var i = 0; i < src.Count; i++)
                {
                    list.Add(new AdskAppearance(src.Item(i)));
                }

                return list;
            }
        }
    }

    // ── T7 parameters ────────────────────────────────────────────────

    private sealed class AdskUserParameterCollection : IFusionUserParameterCollection
    {
        private readonly adsk.fusion.ParameterCollection _c;

        public AdskUserParameterCollection(adsk.fusion.ParameterCollection c) => _c = c;

        public int Count => _c.Count;
        public IFusionParameter Item(int index) => new AdskParameter(_c.Item(index));

        /// <summary>Reference set_parameter / delete_parameter: itemByName (null when absent).</summary>
        public IFusionParameter? ItemByName(string name)
            => _c.ItemByName(name) is { } p ? new AdskParameter(p) : null;

        /// <summary>
        /// Reference create_parameter: valueInput = build_value_input(value, unit,
        /// createByString, createByReal) — "value unit" expression when a unit is set,
        /// a raw real otherwise — then params.add(name, vi, unit, comment).
        /// </summary>
        public IFusionParameter Add(string name, double value, string? unit, string? comment)
        {
            var normalizedUnit = (unit ?? string.Empty).Trim();
            var valueInput = normalizedUnit.Length > 0
                ? ValueInput.CreateByString(FormattedParameterExpression(value, normalizedUnit))
                : ValueInput.CreateByReal(value);
            return new AdskParameter(_c.Add(name, valueInput, normalizedUnit, comment ?? string.Empty));
        }

        /// <summary>Reference delete_parameter: param.deleteMe().</summary>
        public void Delete(IFusionParameter parameter) => ((AdskParameter)parameter)._p.DeleteMe();

        /// <summary>Reference get_parameters iterates the collection (foreach param in userParameters).</summary>
        public System.Collections.Generic.IEnumerator<IFusionParameter> GetEnumerator()
        {
            for (var i = 0; i < _c.Count; i++)
            {
                yield return new AdskParameter(_c.Item(i));
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Reference expression_for(value, unit): "value unit" when a unit is set, the bare value otherwise.</summary>
    private static string FormattedParameterExpression(double value, string unit)
        => value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + unit;

    // ── T7 export ────────────────────────────────────────────────────

    private sealed class AdskExportManager : IFusionExportManager
    {
        private readonly adsk.fusion.ExportManager _m;

        public AdskExportManager(adsk.fusion.ExportManager m) => _m = m;

        /// <summary>Reference export_stl: createSTLExportOptions(entity, path) + MediumMeshRefinement + execute.</summary>
        public void ExportStl(IFusionExportTarget entity, string filePath)
        {
            var options = _m.CreateSTLExportOptions(ExportObject(entity), filePath);
            options.MeshRefinement = adsk.fusion.MeshRefinementSettings.MediumMeshRefinement;
            _m.Execute(options);
        }

        /// <summary>Reference export_step: createSTEPExportOptions(file_path, entity) — note the argument order.</summary>
        public void ExportStep(string filePath, IFusionExportTarget entity)
        {
            _m.Execute(_m.CreateSTEPExportOptions(filePath, ExportObject(entity)));
        }

        /// <summary>Reference export_f3d: createFusionArchiveExportOptions(file_path) + execute.</summary>
        public void ExportFusionArchive(string filePath)
            => _m.Execute(_m.CreateFusionArchiveExportOptions(filePath));

        /// <summary>Export targets are adsk bodies or occurrences (reference sibling-hide path).</summary>
        private static object ExportObject(IFusionExportTarget target) => target switch
        {
            AdskBody b => b.Body,
            AdskOccurrence o => o.Adsk,
            _ => throw new FusionCommandException("Export target must be a body or an occurrence"),
        };
    }

    // ── T7 assembly: upgraded collections ────────────────────────────

    private sealed class AdskComponentOccurrences : IFusionOccurrences
    {
        private readonly adsk.fusion.OccurrenceCollection _c;

        public AdskComponentOccurrences(adsk.fusion.OccurrenceCollection c) => _c = c;

        public int Count => _c.Count;

        public IFusionDeletable Item(int index)
        {
            var e = _c.Item(index);
            return new AdskDeletable(() => e.Component.Name, e.DeleteMe);
        }

        public IFusionOccurrence OccurrenceAt(int index)
            => new AdskOccurrence(_c.Item(index));

        /// <summary>Reference create_component: parent.occurrences.addNewComponent(matrix3D.create()).</summary>
        public IFusionOccurrence AddNewComponent()
            => new AdskOccurrence(_c.AddNewComponent(adsk.core.Matrix3D.Create()));
    }

    private sealed class AdskRigidGroupCollection : IFusionRigidGroupCollection
    {
        private readonly adsk.fusion.RigidGroupCollection _c;

        public AdskRigidGroupCollection(adsk.fusion.RigidGroupCollection c) => _c = c;

        public int Count => _c.Count;

        public IFusionDeletable Item(int index)
        {
            var e = _c.Item(index);
            return new AdskDeletable(() => e.Name, e.DeleteMe);
        }

        /// <summary>Reference create_rigid_group: rigidGroups.add(objectCollection, includeChildren).</summary>
        public void Add(IReadOnlyList<IFusionOccurrence> occurrences, bool includeChildren)
        {
            var coll = AdskColl(occurrences.Select(o => (object)((AdskOccurrence)o).Adsk));
            _c.Add(coll, includeChildren);
        }
    }

    private sealed class AdskAsBuiltJointCollection : IFusionAsBuiltJointCollection
    {
        private readonly adsk.fusion.AsBuiltJointCollection _c;

        public AdskAsBuiltJointCollection(adsk.fusion.AsBuiltJointCollection c) => _c = c;

        public int Count => _c.Count;

        public IFusionDeletable Item(int index)
        {
            var e = _c.Item(index);
            return new AdskDeletable(() => e.Name, e.DeleteMe);
        }

        /// <summary>Reference create_as_built_joint: asBuiltJoints.createInput(occ1, occ2, None).</summary>
        public IFusionAsBuiltJointInput CreateInput(IFusionOccurrence componentOne, IFusionOccurrence componentTwo)
            => new AdskAsBuiltJointInput(_c.CreateInput(((AdskOccurrence)componentOne).Adsk, ((AdskOccurrence)componentTwo).Adsk, null));

        public IFusionAsBuiltJoint Add(IFusionAsBuiltJointInput input)
            => new AdskAsBuiltJoint(_c.Add(((AdskAsBuiltJointInput)input).Input));
    }

    private sealed class AdskAsBuiltJointInput : IFusionAsBuiltJointInput
    {
        public AdskAsBuiltJointInput(adsk.fusion.AsBuiltJointInput input) => Input = input;

        public adsk.fusion.AsBuiltJointInput Input { get; }
    }

    private sealed class AdskAsBuiltJoint : IFusionAsBuiltJoint
    {
        public AdskAsBuiltJoint(adsk.fusion.AsBuiltJoint joint) => Joint = joint;

        public adsk.fusion.AsBuiltJoint Joint { get; }
    }

    private sealed class AdskJointCollection : IFusionJointCollection
    {
        private readonly adsk.fusion.JointCollection _c;

        public AdskJointCollection(adsk.fusion.JointCollection c) => _c = c;

        public int Count => _c.Count;

        public IFusionDeletable Item(int index)
        {
            var e = _c.Item(index);
            return new AdskDeletable(() => e.Name, e.DeleteMe);
        }

        /// <summary>Reference add_joint: joints.createInput(geo1, geo2).</summary>
        public IFusionJointInput CreateInput(IFusionJointGeometry geometryOne, IFusionJointGeometry geometryTwo)
            => new AdskJointInput(
                _c.CreateInput(((AdskJointGeometry)geometryOne).Geometry, ((AdskJointGeometry)geometryTwo).Geometry));

        /// <summary>Reference add_joint: joints.add(input) after setAsRigidJointMotion for rigid.</summary>
        public IFusionJoint Add(IFusionJointInput input)
            => new AdskJoint(_c.Add(((AdskJointInput)input).Input));
    }

    private sealed class AdskJointInput : IFusionJointInput
    {
        public AdskJointInput(adsk.fusion.JointInput input) => Input = input;

        public adsk.fusion.JointInput Input { get; }

        /// <summary>Reference add_joint: inp.setAsRigidJointMotion() when joint_type is rigid.</summary>
        public void SetAsRigidJointMotion() => Input.SetAsRigidJointMotion();
    }

    private sealed class AdskJoint : IFusionJoint
    {
        public AdskJoint(adsk.fusion.Joint joint) => Joint = joint;

        public adsk.fusion.Joint Joint { get; }
    }

    private sealed class AdskJointGeometry : IFusionJointGeometry
    {
        public AdskJointGeometry(adsk.fusion.JointGeometry geometry) => Geometry = geometry;

        public adsk.fusion.JointGeometry Geometry { get; }
    }

    // ── T7 construction: upgraded collections ────────────────────────

    private sealed class AdskConstructionPlaneCollection : IFusionConstructionPlaneCollection
    {
        private readonly adsk.fusion.ConstructionPlaneCollection _c;

        public AdskConstructionPlaneCollection(adsk.fusion.ConstructionPlaneCollection c) => _c = c;

        public int Count => _c.Count;

        public IFusionDeletable Item(int index)
        {
            var e = _c.Item(index);
            return new AdskDeletable(() => e.Name, e.DeleteMe);
        }

        /// <summary>Reference create_construction_plane: planes.createInput() + setter + add.</summary>
        public IFusionConstructionPlaneInput CreateInput() => new AdskConstructionPlaneInput(_c.CreateInput());

        public IFusionConstructionPlane Add(IFusionConstructionPlaneInput input)
            => new AdskConstructionPlane(_c.Add(((AdskConstructionPlaneInput)input).Input));
    }

    private sealed class AdskConstructionPlaneInput : IFusionConstructionPlaneInput
    {
        public AdskConstructionPlaneInput(adsk.fusion.ConstructionPlaneInput input) => Input = input;

        public adsk.fusion.ConstructionPlaneInput Input { get; }

        /// <summary>Reference method "offset": inp.setByOffset(plane, createByReal(offset)).</summary>
        public void SetByOffset(IFusionConstructionPlane basePlane, double offsetCm)
            => Input.SetByOffset(((AdskConstructionPlane)basePlane).AdskPlane, ValueInput.CreateByReal(offsetCm));

        /// <summary>Reference method "angle": inp.setByAngle(axis, createByString("{angle} deg"), plane).</summary>
        public void SetByAngle(IFusionConstructionAxis axis, double angleDegrees, IFusionConstructionPlane basePlane)
            => Input.SetByAngle(
                ((AdskConstructionAxis)axis).AdskAxis,
                ValueInput.CreateByString(FormattedAngle(angleDegrees)),
                ((AdskConstructionPlane)basePlane).AdskPlane);

        /// <summary>Reference method "midplane": inp.setByTwoPlanes(one, two).</summary>
        public void SetByTwoPlanes(IFusionConstructionPlane one, IFusionConstructionPlane two)
            => Input.SetByTwoPlanes(((AdskConstructionPlane)one).AdskPlane, ((AdskConstructionPlane)two).AdskPlane);

        /// <summary>Reference method "three_points": inp.setByThreePoints(p1, p2, p3).</summary>
        public void SetByThreePoints(FusionPoint p1, FusionPoint p2, FusionPoint p3)
            => Input.SetByThreePoints(ToPoint3D(p1), ToPoint3D(p2), ToPoint3D(p3));
    }

    private sealed class AdskConstructionAxisCollection : IFusionConstructionAxisCollection
    {
        private readonly adsk.fusion.ConstructionAxisCollection _c;

        public AdskConstructionAxisCollection(adsk.fusion.ConstructionAxisCollection c) => _c = c;

        public int Count => _c.Count;

        public IFusionDeletable Item(int index)
        {
            var e = _c.Item(index);
            return new AdskDeletable(() => e.Name, e.DeleteMe);
        }

        /// <summary>Reference create_construction_axis: axes.createInput() + setter + add.</summary>
        public IFusionConstructionAxisInput CreateInput() => new AdskConstructionAxisInput(_c.CreateInput());

        public IFusionConstructionAxis Add(IFusionConstructionAxisInput input)
            => new AdskConstructionAxis(_c.Add(((AdskConstructionAxisInput)input).Input));
    }

    private sealed class AdskConstructionAxisInput : IFusionConstructionAxisInput
    {
        public AdskConstructionAxisInput(adsk.fusion.ConstructionAxisInput input) => Input = input;

        public adsk.fusion.ConstructionAxisInput Input { get; }

        /// <summary>Reference method "two_points": inp.setByTwoPoints(p1, p2).</summary>
        public void SetByTwoPoints(FusionPoint p1, FusionPoint p2)
            => Input.SetByTwoPoints(ToPoint3D(p1), ToPoint3D(p2));

        /// <summary>Reference method "intersection": inp.setByTwoPlanes(one, two).</summary>
        public void SetByTwoPlanes(IFusionConstructionPlane one, IFusionConstructionPlane two)
            => Input.SetByTwoPlanes(((AdskConstructionPlane)one).AdskPlane, ((AdskConstructionPlane)two).AdskPlane);

        /// <summary>Reference method "edge": inp.setByEdge(body.edges.item(i)).</summary>
        public void SetByEdge(IFusionBodyEdge edge) => Input.SetByEdge(((AdskBodyEdge)edge).AdskEdge);

        /// <summary>Reference method "perpendicular_at_point": inp.setByPerpendicularAtPoint(plane, point).</summary>
        public void SetByPerpendicularAtPoint(IFusionConstructionPlane plane, FusionPoint point)
            => Input.SetByPerpendicularAtPoint(((AdskConstructionPlane)plane).AdskPlane, ToPoint3D(point));
    }

    private sealed class AdskConstructionPoint : IFusionConstructionPoint
    {
        public AdskConstructionPoint(adsk.fusion.ConstructionPoint point) => Point = point;

        public adsk.fusion.ConstructionPoint Point { get; }
    }

    // ── T7 UCS (preview API) ─────────────────────────────────────────

    private sealed class AdskUcsCollection : IFusionUcsCollection
    {
        private readonly adsk.fusion.UcsCollection _c;

        public AdskUcsCollection(adsk.fusion.UcsCollection c) => _c = c;

        /// <summary>Reference create_ucs: userCoordinateSystems.createInput(geom).</summary>
        public IFusionUcsInput CreateInput(IFusionUcsGeometry geometry)
            => new AdskUcsInput(_c.CreateInput(((AdskUcsGeometry)geometry).Geometry));

        /// <summary>Reference create_ucs: userCoordinateSystems.add(input).</summary>
        public IFusionUcs Add(IFusionUcsInput input) => new AdskUcs(_c.Add(((AdskUcsInput)input).Input));
    }

    private sealed class AdskUcs : IFusionUcs
    {
        public AdskUcs(adsk.fusion.Ucs ucs) => Ucs = ucs;

        public adsk.fusion.Ucs Ucs { get; }

        /// <summary>Reference create_ucs: ucs.name = name (swallowing a failure on some builds).</summary>
        public string Name
        {
            get => Ucs.Name;
            set => Ucs.Name = value;
        }
    }

    private sealed class AdskUcsInput : IFusionUcsInput
    {
        public AdskUcsInput(adsk.fusion.UcsInput input) => Input = input;

        public adsk.fusion.UcsInput Input { get; }

        /// <summary>Reference create_ucs: inp.angleX = createByReal(radians) — only when non-zero (handler).</summary>
        public void SetAngleXRadians(double radians) => Input.AngleX = ValueInput.CreateByReal(radians);
        public void SetAngleYRadians(double radians) => Input.AngleY = ValueInput.CreateByReal(radians);
        public void SetAngleZRadians(double radians) => Input.AngleZ = ValueInput.CreateByReal(radians);
    }

    private sealed class AdskUcsGeometry : IFusionUcsGeometry
    {
        public AdskUcsGeometry(adsk.fusion.UserCoordinateSystemGeometry geometry) => Geometry = geometry;

        public adsk.fusion.UserCoordinateSystemGeometry Geometry { get; }
    }

    // ── T7 CAM (Manufacture product + adsk.cam) ──────────────────────

    private sealed class AdskCamProduct : IFusionCamProduct
    {
        private readonly adsk.fusion.Manufacture _cam;

        public AdskCamProduct(adsk.fusion.Manufacture cam) => _cam = cam;

        public IFusionCamSetupCollection Setups => new AdskCamSetupCollection(_cam.Setups);

        /// <summary>Reference cam_generate_toolpath: cam.generateAllToolpaths(overwrite) — async future.</summary>
        public IFusionCamFuture GenerateAllToolpaths(bool overwrite)
            => new AdskCamFuture(_cam.GenerateAllToolpaths(overwrite));

        /// <summary>Reference cam_generate_toolpath: cam.generateToolpath(op | ObjectCollection-of-ops).</summary>
        public IFusionCamFuture GenerateToolpath(IFusionCamToolpathTarget target)
            => new AdskCamFuture(_cam.GenerateToolpath(ToolpathObject(target)));

        /// <summary>Reference cam_post_process: cam.postProcess(setup | op, postInput).</summary>
        public void PostProcess(IFusionCamPostTarget target, IFusionCamPostInput input)
        {
            var post = target switch
            {
                AdskCamSetup s => (object)s.AdskSetup,
                AdskCamOperation o => o.AdskOperation,
                _ => throw new FusionCommandException("CAM post target must be a setup or an operation"),
            };
            _cam.PostProcess(post, ((AdskCamPostInput)input).Input);
        }

        /// <summary>Reference cam_post_process: PostProcessInput.create(setup_name, post_config, output_folder, units).</summary>
        public IFusionCamPostInput CreatePostProcessInput(string setupName, string postConfigPath, string outputFolder, FusionCamOutputUnits units)
            => new AdskCamPostInput(
                adsk.cam.PostProcessInput.Create(setupName, postConfigPath, outputFolder, ToAdskPostUnits(units)));

        /// <summary>Reference: getattr(cam, "genericPostFolder", None) — null on cloud-post builds.</summary>
        public string? GenericPostFolder => _cam.GenericPostFolder;

        /// <summary>
        /// Unwrap a toolpath target: an operation, a whole-setup operations
        /// collection, or a setup (the reference builds an ObjectCollection for the latter two).
        /// </summary>
        private object ToolpathObject(IFusionCamToolpathTarget target) => target switch
        {
            AdskCamOperation o => (object)o.AdskOperation,
            AdskCamOperationCollection c => OperationCollection(c.AdskCollection),
            AdskCamSetup s => OperationCollection(s.AdskSetup.Operations),
            _ => throw new FusionCommandException("CAM toolpath target must be an operation or a setup"),
        };

        private static adsk.core.ObjectCollection OperationCollection(adsk.cam.CamOperationCollection ops)
            => AdskColl(Enumerable.Range(0, ops.Count).Select(i => (object)ops.Item(i)));
    }

    private sealed class AdskCamFuture : IFusionCamFuture
    {
        private readonly adsk.cam.CamFuture _f;

        public AdskCamFuture(adsk.cam.CamFuture future) => _f = future;

        /// <summary>Reference _wait_future: future.isCompleted / future.wait() with a bounded poll.</summary>
        public bool IsCompleted => _f.IsCompleted;
        public void Wait() => _f.Wait();
    }

    private sealed class AdskCamSetup : IFusionCamSetup
    {
        public AdskCamSetup(adsk.cam.CamSetup setup) => AdskSetup = setup;

        public adsk.cam.CamSetup AdskSetup { get; }

        public string Name => AdskSetup.Name;
        public bool IsValid => AdskSetup.IsValid;
        public IFusionCamOperationCollection Operations => new AdskCamOperationCollection(AdskSetup.Operations);

        /// <summary>Reference _set_cam_parameter: params.itemByName(name); p.expression = value.</summary>
        public bool SetParameter(string parameterName, string expression)
        {
            try
            {
                var p = AdskSetup.Parameters.ItemByName(parameterName);
                if (p is null)
                {
                    return false;
                }

                p.Expression = expression;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed class AdskCamSetupInput : IFusionCamSetupInput
    {
        public AdskCamSetupInput(adsk.cam.CamSetupInput input) => Input = input;

        public adsk.cam.CamSetupInput Input { get; }

        /// <summary>Reference cam_create_setup: setup_input.models = [body].</summary>
        public void SetModels(IReadOnlyList<IFusionBody> bodies)
            => Input.Models = AdskColl(bodies.Select(b => (object)((AdskBody)b).Body));

        /// <summary>Reference cam_create_setup: setup_input.name = name.</summary>
        public string? Name
        {
            set => Input.Name = value;
        }
    }

    private sealed class AdskCamSetupCollection : IFusionCamSetupCollection
    {
        private readonly adsk.cam.CamSetupCollection _c;

        public AdskCamSetupCollection(adsk.cam.CamSetupCollection c) => _c = c;

        public int Count => _c.Count;
        public IFusionCamSetup Item(int index) => new AdskCamSetup(_c.Item(index));

        /// <summary>Reference cam_create_setup: cam.setups.createInput(op_type).</summary>
        public IFusionCamSetupInput CreateInput(FusionCamOperationType operationType)
            => new AdskCamSetupInput(_c.CreateInput(ToAdskCamOperationType(operationType)));

        /// <summary>Reference cam_create_setup: setup = cam.setups.add(setup_input).</summary>
        public IFusionCamSetup Add(IFusionCamSetupInput input)
            => new AdskCamSetup(_c.Add(((AdskCamSetupInput)input).Input));
    }

    private sealed class AdskCamOperation : IFusionCamOperation
    {
        public AdskCamOperation(adsk.cam.CamOperation operation) => AdskOperation = operation;

        public adsk.cam.CamOperation AdskOperation { get; }

        public string Name => AdskOperation.Name;
        public bool HasToolpath => AdskOperation.HasToolpath;
        public bool IsValid => AdskOperation.IsValid;

        public IFusionCamToolInfo? Tool
            => AdskOperation.Tool is { } t ? new AdskCamToolInfo(t) : null;

        /// <summary>Reference cam_get_operation_info: iterates op.parameters (name/expression).</summary>
        public IReadOnlyList<IFusionCamParameter> Parameters
        {
            get
            {
                var list = new List<IFusionCamParameter>();
                foreach (var p in AdskOperation.Parameters)
                {
                    list.Add(new AdskCamParameter(p));
                }

                return list;
            }
        }

        /// <summary>Reference _set_cam_parameter on an operation.</summary>
        public bool SetParameter(string parameterName, string expression)
        {
            try
            {
                var p = AdskOperation.Parameters.ItemByName(parameterName);
                if (p is null)
                {
                    return false;
                }

                p.Expression = expression;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed class AdskCamOperationInput : IFusionCamOperationInput
    {
        public AdskCamOperationInput(adsk.cam.CamOperationInput input) => Input = input;

        public adsk.cam.CamOperationInput Input { get; }

        /// <summary>Reference cam_create_operation: op_input.name = name.</summary>
        public string? Name
        {
            set => Input.Name = value;
        }
    }

    private sealed class AdskCamOperationCollection : IFusionCamOperationCollection
    {
        public AdskCamOperationCollection(adsk.cam.CamOperationCollection c) => AdskCollection = c;

        public adsk.cam.CamOperationCollection AdskCollection { get; }

        public int Count => AdskCollection.Count;
        public IFusionCamOperation Item(int index) => new AdskCamOperation(AdskCollection.Item(index));

        /// <summary>Reference cam_create_operation: op_input = setup.operations.createInput(strategy).</summary>
        public IFusionCamOperationInput CreateInput(string strategy)
            => new AdskCamOperationInput(AdskCollection.CreateInput(strategy));

        /// <summary>Reference cam_create_operation: op = setup.operations.add(op_input) — None when rejected.</summary>
        public IFusionCamOperation? Add(IFusionCamOperationInput input)
            => AdskCollection.Add(((AdskCamOperationInput)input).Input) is { } op
                ? new AdskCamOperation(op)
                : null;
    }

    private sealed class AdskCamParameter : IFusionCamParameter
    {
        private readonly adsk.cam.CamParameter _p;

        public AdskCamParameter(adsk.cam.CamParameter p) => _p = p;

        public string Name => _p.Name;

        public string? Expression
        {
            get => _p.Expression;
            set => _p.Expression = value;
        }

        /// <summary>Reference cam_get_operation_info: wraps param.expression in try/except pass.</summary>
        public bool TryGetExpression(out string expression)
        {
            try
            {
                expression = _p.Expression ?? string.Empty;
                return true;
            }
            catch
            {
                expression = string.Empty;
                return false;
            }
        }
    }

    private sealed class AdskCamToolInfo : IFusionCamToolInfo
    {
        private readonly adsk.cam.CamTool _t;

        public AdskCamToolInfo(adsk.cam.CamTool tool) => _t = tool;

        /// <summary>Reference: tool.description (may be null — empty string keeps the JSON stable).</summary>
        public string Description => _t.Description ?? string.Empty;
    }

    private sealed class AdskCamPostInput : IFusionCamPostInput
    {
        public AdskCamPostInput(adsk.cam.PostProcessInput input) => Input = input;

        public adsk.cam.PostProcessInput Input { get; }

        /// <summary>Reference cam_post_process: post_input.isOpenInEditor = False.</summary>
        public bool OpenInEditor
        {
            set => Input.IsOpenInEditor = value;
        }
    }

    // ── T7 enum / unit mappers ───────────────────────────────────────

    private static adsk.fusion.CalculationAccuracy ToAdskAccuracy(FusionCalculationAccuracy a)
        => (adsk.fusion.CalculationAccuracy)(int)a;

    private static adsk.fusion.MeshUnits ToAdskMeshUnits(FusionMeshUnits u)
        => u switch
        {
            FusionMeshUnits.Millimeter => adsk.fusion.MeshUnits.Millimeter,
            FusionMeshUnits.Centimeter => adsk.fusion.MeshUnits.Centimeter,
            FusionMeshUnits.Meter => adsk.fusion.MeshUnits.Meter,
            FusionMeshUnits.Inch => adsk.fusion.MeshUnits.Inch,
            // The reference exposes ft too; map to the nearest adsk unit the stub provides.
            _ => adsk.fusion.MeshUnits.Inch,
        };

    private static adsk.cam.OperationTypes ToAdskCamOperationType(FusionCamOperationType t)
        => (adsk.cam.OperationTypes)(int)t;

    private static adsk.cam.PostOutputUnitOptions ToAdskPostUnits(FusionCamOutputUnits u)
        => (adsk.cam.PostOutputUnitOptions)(int)u;
}
