// T7 stub surface — see AdskCoreStubs.cs / AdskFusionStubs.cs
// conventions: every member throws NotSupportedException("adsknet stub"),
// the real adsk binds at runtime inside Fusion. Members mirror the Python
// reference (reference/fusion360-mcp-server/addon/server/command_handler.py)
// adsk call surface for the T7 tool groups (sheet metal, construction,
// assembly, inspection, mesh, appearance, parameters, export, CAM).
// Documented assumptions (T8 verification against the live adsk API):
//  - enum numeric values (CalculationAccuracy, MeshUnits, OperationTypes,
//    MeshRefinementSettings, PostOutputUnitOptions) are order-only; the
//    handlers map through names, never raw ints.
//  - Manufacture/cam surface shape is inferred from the Python binding
//    (adsk.cam), which reflects the genuine adsk.cam .NET API.
namespace adsk.fusion
{
    using adsk.core;

    // ── inspection: measure manager ─────────────────────────────────

    /// <summary>Calculation accuracy ids (reference adsk.fusion.CalculationAccuracy).</summary>
    public enum CalculationAccuracy
    {
        LowCalculationAccuracy = 0,
        MediumCalculationAccuracy = 1,
        HighCalculationAccuracy = 2,
        VeryHighCalculationAccuracy = 3,
    }

    /// <summary>adsk.fusion.MeasureManager (app.measureManager — measure_distance / measure_angle).</summary>
    public sealed class MeasureManager
    {
        /// <summary>reference measure.measureMinimumDistance(e1, e2) — e1/e2 are bodies or points.</summary>
        public DistanceMeasureResult MeasureMinimumDistance(object entityOne, object entityTwo)
            => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference measure.measureAngle(face1, face2).</summary>
        public AngleMeasureResult MeasureAngle(Face faceOne, Face faceTwo)
            => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>Minimum-distance measure result (values in cm).</summary>
    public sealed class DistanceMeasureResult
    {
        public double Value => throw new System.NotSupportedException("adsknet stub");
        public Point3D PointOnEntityOne => throw new System.NotSupportedException("adsknet stub");
        public Point3D PointOnEntityTwo => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>Angle measure result (value in radians; reference converts to degrees).</summary>
    public sealed class AngleMeasureResult
    {
        public double Value => throw new System.NotSupportedException("adsknet stub");
    }

    // ── inspection: section analysis + interference ─────────────────

    /// <summary>adsk.fusion.AnalysisCollection (rootComponent.analyses — create_section_analysis).</summary>
    public sealed class AnalysisCollection
    {
        public SectionAnalysisInput CreateInput() => throw new System.NotSupportedException("adsknet stub");
        public SectionAnalysis Add(SectionAnalysisInput input) => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.fusion.SectionAnalysisInput (reference: inp.plane = plane; inp.distance = valueInput.createByReal(offset)).</summary>
    public sealed class SectionAnalysisInput
    {
        public ConstructionPlane? Plane { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
        public ValueInput? Distance { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
    }

    /// <summary>A section analysis feature (reference feat = analyses.add(inp)).</summary>
    public sealed class SectionAnalysis : Feature
    {
    }

    /// <summary>adsk.fusion.InterferenceInput (design.createInterferenceInput(bodies)).</summary>
    public sealed class InterferenceInput
    {
        /// <summary>reference inp.areCoincidentFacesIncluded = bool.</summary>
        public bool AreCoincidentFacesIncluded { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
    }

    /// <summary>adsk.fusion.Interference result (design.analyzeInterference — null when no interference).</summary>
    public sealed class Interference
    {
        public int Count => throw new System.NotSupportedException("adsknet stub");
        public InterferenceResult Item(int index) => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>One interference pair (reference result.entityOne/entityTwo/interferenceBody).</summary>
    public sealed class InterferenceResult
    {
        public BRepBody EntityOne => throw new System.NotSupportedException("adsknet stub");
        public BRepBody EntityTwo => throw new System.NotSupportedException("adsknet stub");
        public BRepBody InterferenceBody => throw new System.NotSupportedException("adsknet stub");
    }

    // ── mesh ────────────────────────────────────────────────────────

    /// <summary>Mesh unit ids (reference adsk.fusion.MeshUnits).</summary>
    public enum MeshUnits
    {
        Millimeter = 0,
        Centimeter = 1,
        Meter = 2,
        Inch = 3,
    }

    /// <summary>adsk.fusion.MeshBody (rootComponent.meshBodies — import_mesh / compare_meshes).</summary>
    public sealed class MeshBody
    {
        public string Name => throw new System.NotSupportedException("adsknet stub");
        public BoundingBox? BoundingBox => throw new System.NotSupportedException("adsknet stub");

        /// <summary>The body's polygon mesh (reference mb.mesh, null on some builds).</summary>
        public PolygonMesh? Mesh => throw new System.NotSupportedException("adsknet stub");

        /// <summary>Fallback polygon mesh (reference mb.displayMesh).</summary>
        public PolygonMesh? DisplayMesh => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.fusion.MeshBodyCollection.</summary>
    public sealed class MeshBodyCollection
    {
        public int Count => throw new System.NotSupportedException("adsknet stub");
        public MeshBody Item(int index) => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference root.meshBodies.add(file_path, units) → MeshBodyList (old adsk: addByFile).</summary>
        public MeshBodyList Add(string filePath, MeshUnits units) => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>The MeshBodyList returned by import_mesh (reference mb_list.count / item(i)).</summary>
    public sealed class MeshBodyList
    {
        public int Count => throw new System.NotSupportedException("adsknet stub");
        public MeshBody Item(int index) => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.core.PolygonMesh (reference mb.mesh.compareWith(other) — Fusion 2026).</summary>
    public sealed class PolygonMesh
    {
        /// <summary>Per-node signed distances (cm) to the closest point on the other mesh.</summary>
        public System.Collections.Generic.IReadOnlyList<double> CompareWith(PolygonMesh other)
            => throw new System.NotSupportedException("adsknet stub");
    }

    // ── appearance / materials ──────────────────────────────────────

    /// <summary>adsk.fusion.Appearance (body/face/component appearance assignment; set_color).</summary>
    public sealed class Appearance
    {
        public string Name => throw new System.NotSupportedException("adsknet stub");

        /// <summary>The appearance's color (reference appearance.color = adsk.core.Color.create(r, g, b, a)).</summary>
        public Color Color { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
    }

    /// <summary>adsk.fusion.AppearanceCollection (design.appearances).</summary>
    public sealed class AppearanceCollection
    {
        /// <summary>reference appearances.itemByName(name) — null when absent.</summary>
        public Appearance? ItemByName(string name) => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference appearances.addByCopy(source, name).</summary>
        public Appearance AddByCopy(Appearance source, string name) => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference appearances.add(name).</summary>
        public Appearance Add(string name) => throw new System.NotSupportedException("adsknet stub");

        /// <summary>Indexed access (set_appearance library-scan fallback).</summary>
        public int Count => throw new System.NotSupportedException("adsknet stub");
        public Appearance Item(int index) => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.fusion.MaterialLibrary (app.materialLibraries — set_appearance library scan).</summary>
    public sealed class MaterialLibrary
    {
        public string Name => throw new System.NotSupportedException("adsknet stub");
        public AppearanceCollection Appearances => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.fusion.MaterialLibraryCollection (app.materialLibraries).</summary>
    public sealed class MaterialLibraryCollection
    {
        public int Count => throw new System.NotSupportedException("adsknet stub");
        public MaterialLibrary Item(int index) => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference materialLibraries.itemByName(name) — null when absent.</summary>
        public MaterialLibrary? ItemByName(string name) => throw new System.NotSupportedException("adsknet stub");
    }

    // ── user coordinate systems (preview API — Fusion 2025.86+) ─────

    /// <summary>adsk.fusion.UserCoordinateSystem (rootComponent.userCoordinateSystems — create_ucs).</summary>
    public sealed class Ucs
    {
        public string Name { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
    }

    /// <summary>adsk.fusion.UcsCollection.</summary>
    public sealed class UcsCollection
    {
        /// <summary>reference userCoordinateSystems.createInput(geometry).</summary>
        public UcsInput CreateInput(UserCoordinateSystemGeometry geometry)
            => throw new System.NotSupportedException("adsknet stub");

        public Ucs Add(UcsInput input) => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.fusion.UcsInput (reference ucsInput.angleX = valueInput.createByAngle(...)).</summary>
    public sealed class UcsInput
    {
        public ValueInput? AngleX { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
        public ValueInput? AngleY { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
        public ValueInput? AngleZ { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
    }

    /// <summary>
    /// adsk.fusion.UserCoordinateSystemGeometry — built via
    /// <see cref="Component.CreateUcsGeometryAtPoint"/> (documented .NET shape, T8).
    /// </summary>
    public sealed class UserCoordinateSystemGeometry
    {
    }

    // ── export ──────────────────────────────────────────────────────

    /// <summary>STL mesh-refinement settings (reference MeshRefinementSettings.MeshRefinementMedium).</summary>
    public enum MeshRefinementSettings
    {
        LowMeshRefinement = 0,
        MediumMeshRefinement = 1,
        HighMeshRefinement = 2,
    }

    /// <summary>adsk.fusion.ExportManager (design.exportManager — export_stl / export_step / export_fusion_file).</summary>
    public sealed class ExportManager
    {
        /// <summary>reference createSTLExportOptions(entity, file_path); entity is a body or an occurrence.</summary>
        public STLExportOptions CreateSTLExportOptions(object entity, string filePath)
            => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference createSTEPExportOptions(file_path, entity) — note the argument order.</summary>
        public STEPExportOptions CreateSTEPExportOptions(string filePath, object entity)
            => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference createFusionArchiveExportOptions(file_path).</summary>
        public FusionArchiveExportOptions CreateFusionArchiveExportOptions(string filePath)
            => throw new System.NotSupportedException("adsknet stub");

        public bool Execute(object options) => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.fusion.STLExportOptions (reference opts.meshRefinement = MeshRefinementMedium).</summary>
    public sealed class STLExportOptions
    {
        public MeshRefinementSettings MeshRefinement { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
    }

    /// <summary>adsk.fusion.STEPExportOptions.</summary>
    public sealed class STEPExportOptions
    {
    }

    /// <summary>adsk.fusion.FusionArchiveExportOptions.</summary>
    public sealed class FusionArchiveExportOptions
    {
    }

    // ── CAM (Manufacture product + adsk.cam surface) ────────────────

    /// <summary>
    /// adsk.fusion.Manufacture — the document's CAM product
    /// (doc.products.itemByProductType("CAMProductType")). Null/absent
    /// until the Manufacture workspace has been activated.
    /// </summary>
    public sealed class Manufacture : Product
    {
        /// <summary>adsk.cam.CamSetupCollection (cam.setups).</summary>
        public adsk.cam.CamSetupCollection Setups => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference cam.generateAllToolpaths(overwrite) → future.</summary>
        public adsk.cam.CamFuture GenerateAllToolpaths(bool overwrite)
            => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference cam.generateToolpath(target) — target: an operation or an ObjectCollection of operations.</summary>
        public adsk.cam.CamFuture GenerateToolpath(object target)
            => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference cam.postProcess(target, postInput) — target: a setup or an operation.</summary>
        public void PostProcess(object target, adsk.cam.PostProcessInput input)
            => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference getattr(cam, "genericPostFolder", None) — null on cloud-post builds.</summary>
        public string? GenericPostFolder => throw new System.NotSupportedException("adsknet stub");
    }
}
