using System;
using System.Collections.Generic;

namespace Fusion360.Mcp.AddIn
{
    // ════════════════════════════════════════════════════════════════
    // T7 group-3 facade interfaces (sheet-metal construction set).
    // Mirrors reference fusion360-mcp-server addon/server/
    // command_handler.py: sheet metal (flange/bend/flat pattern/unfold),
    // construction (plane/axis/UCS), assembly (component/joint/as-built/
    // rigid group), inspection (distance/angle/physical properties/
    // interference/section analysis), mesh (import/compare/bounding
    // box), appearance (material/color), parameters, export, CAM.
    // ════════════════════════════════════════════════════════════════

    // ── Sheet metal ────────────────────────────────────────────────

    /// <summary>root.features.flangeFeatures (adsk.fusion.FlangeFeatureCollection).</summary>
    public interface IFusionFlangeFeatures
    {
        /// <param name="edge">Start-edge geometry to be flanged.</param>
        IFusionFlangeInput CreateInput(IFusionBodyEdge edge);
    }

    /// <summary>Flange input: angle + height (reference uses angleAndHeight=True).</summary>
    public interface IFusionFlangeInput
    {
        void SetAngleDegrees(double angleDegrees);
        void SetHeightCm(double heightCm);

        /// <summary>Optional bend radius (reference: bend_radius param).</summary>
        void SetBendRadiusCm(double bendRadiusCm);

        IFusionFeature Add();
    }

    /// <summary>root.features.bendFeatures (adsk.fusion.BendFeatureCollection).</summary>
    public interface IFusionBendFeatures
    {
        IFusionBendInput CreateInput(IFusionBody body, IFusionSketchCurve bendLine);
    }

    /// <summary>Sheet-metal bend input: angle + optional radius.</summary>
    public interface IFusionBendInput
    {
        void SetBendAngleDegrees(double bendAngleDegrees);
        void SetBendRadiusCm(double bendRadiusCm);

        IFusionFeature Add();
    }

    /// <summary>root.features.flatPatternFeatures (adsk.fusion.FlatPatternFeatureCollection).</summary>
    public interface IFusionFlatPatternFeatures
    {
        IFusionFlatPatternInput CreateInput(IFusionBody body);
        IFusionFeature Add(IFusionFlatPatternInput input);
    }

    public interface IFusionFlatPatternInput
    {
    }

    /// <summary>root.features.unfoldFeatures (adsk.fusion.UnfoldFeatureCollection).</summary>
    public interface IFusionUnfoldFeatures
    {
        IFusionUnfoldInput CreateInput(IReadOnlyList<IFusionFace> bends, IFusionFace stationary);
        IFusionFeature Add(IFusionUnfoldInput input);
    }

    public interface IFusionUnfoldInput
    {
    }

    // ── Construction geometry ───────────────────────────────────────

    /// <summary>Upgraded construction-plane collection (adds createInput/add).</summary>
    public interface IFusionConstructionPlaneCollection : IFusionDeletableCollection
    {
        IFusionConstructionPlaneInput CreateInput();
        IFusionConstructionPlane Add(IFusionConstructionPlaneInput input);
    }

    /// <summary>ConstructionPlaneInput — mirrors adsk.fusion.ConstructionPlaneInput.</summary>
    public interface IFusionConstructionPlaneInput
    {
        void SetByOffset(IFusionConstructionPlane basePlane, double offsetCm);
        void SetByAngle(IFusionConstructionAxis axis, double angleDegrees, IFusionConstructionPlane basePlane);
        void SetByTwoPlanes(IFusionConstructionPlane one, IFusionConstructionPlane two);
        void SetByThreePoints(FusionPoint p1, FusionPoint p2, FusionPoint p3);
    }

    /// <summary>Upgraded construction-axis collection (adds createInput/add).</summary>
    public interface IFusionConstructionAxisCollection : IFusionDeletableCollection
    {
        IFusionConstructionAxisInput CreateInput();
        IFusionConstructionAxis Add(IFusionConstructionAxisInput input);
    }

    /// <summary>ConstructionAxisInput — mirrors adsk.fusion.ConstructionAxisInput.</summary>
    public interface IFusionConstructionAxisInput
    {
        void SetByTwoPoints(FusionPoint p1, FusionPoint p2);
        void SetByTwoPlanes(IFusionConstructionPlane one, IFusionConstructionPlane two);
        void SetByEdge(IFusionBodyEdge edge);
        void SetByPerpendicularAtPoint(IFusionConstructionPlane plane, FusionPoint point);
    }

    /// <summary>Construction point (joint anchor / UCS origin) — marker, no adsk surface needed.</summary>
    public interface IFusionConstructionPoint
    {
    }

    // ── User coordinate systems (preview API — adsk surface verified in T8) ──

    /// <summary>rootComponent.userCoordinateSystems (preview API — Fusion 2025.86+).</summary>
    public interface IFusionUcsCollection
    {
        IFusionUcsInput CreateInput(IFusionUcsGeometry geometry);
        IFusionUcs Add(IFusionUcsInput input);
    }

    public interface IFusionUcs
    {
        string Name { get; set; }
    }

    public interface IFusionUcsInput
    {
        void SetAngleXRadians(double radians);
        void SetAngleYRadians(double radians);
        void SetAngleZRadians(double radians);
    }

    /// <summary>UserCoordinateSystemGeometry produced from a sketch point.</summary>
    public interface IFusionUcsGeometry
    {
    }

    // ── Assembly ────────────────────────────────────────────────────

    /// <summary>Upgraded occurrence collection (adds addNewComponent).</summary>
    public interface IFusionOccurrences : IFusionDeletableCollection
    {
        /// <summary>
        /// The typed occurrence at <paramref name="index"/> (reference
        /// <c>root.occurrences.item(i)</c> — the deletable Item above only
        /// exposes name/delete; export_view_sheet's scene bounds need the
        /// occurrence's own visibility/bodies).
        /// </summary>
        IFusionOccurrence OccurrenceAt(int index);

        /// <summary>Reference create_component: parent.occurrences.addNewComponent(identity matrix).</summary>
        IFusionOccurrence AddNewComponent();
    }

    /// <summary>rootComponent.joints (adsk.fusion.JointCollection).</summary>
    public interface IFusionJointCollection : IFusionDeletableCollection
    {
        IFusionJointInput CreateInput(IFusionJointGeometry geometryOne, IFusionJointGeometry geometryTwo);
        IFusionJoint Add(IFusionJointInput input);
    }

    public interface IFusionJointInput
    {
        /// <summary>JointTypes.RigidJointType motion.</summary>
        void SetAsRigidJointMotion();
    }

    public interface IFusionJoint
    {
    }

    /// <summary>Joint geometry built from an occurrence + construction point.</summary>
    public interface IFusionJointGeometry
    {
    }

    /// <summary>rootComponent.asBuiltJoints (adsk.fusion.AsBuiltJointCollection).</summary>
    public interface IFusionAsBuiltJointCollection : IFusionDeletableCollection
    {
        IFusionAsBuiltJointInput CreateInput(IFusionOccurrence componentOne, IFusionOccurrence componentTwo);
        IFusionAsBuiltJoint Add(IFusionAsBuiltJointInput input);
    }

    public interface IFusionAsBuiltJointInput
    {
    }

    public interface IFusionAsBuiltJoint
    {
    }

    /// <summary>rootComponent.rigidGroups (adsk.fusion.RigidGroupCollection).</summary>
    public interface IFusionRigidGroupCollection : IFusionDeletableCollection
    {
        void Add(IReadOnlyList<IFusionOccurrence> occurrences, bool includeChildren);
    }

    // ── Inspection ──────────────────────────────────────────────────

    /// <summary>Marker: anything the measure manager can measure (body or point).</summary>
    public interface IFusionMeasureEntity
    {
    }

    /// <summary>A coordinate point (x, y, z in cm) as a measure entity.</summary>
    public interface IFusionMeasurePoint : IFusionMeasureEntity
    {
        FusionPoint Point { get; }
    }

    /// <summary>adsk.fusion.MeasureManager (root.measureManager).</summary>
    public interface IFusionMeasureManager
    {
        IFusionDistanceMeasureResult MeasureMinimumDistance(IFusionMeasureEntity entityOne, IFusionMeasureEntity entityTwo);
        IFusionAngleMeasureResult MeasureAngle(IFusionFace faceOne, IFusionFace faceTwo);
    }

    /// <summary>Minimum-distance result (values in cm).</summary>
    public interface IFusionDistanceMeasureResult
    {
        double Value { get; }
        FusionPoint PointOnEntityOne { get; }
        FusionPoint PointOnEntityTwo { get; }
    }

    /// <summary>Angle-measure result.</summary>
    public interface IFusionAngleMeasureResult
    {
        double ValueRadians { get; }
    }

    /// <summary>Body physical properties (cm-based: mass kg, volume cm3, area cm2, density kg/cm3).</summary>
    public interface IFusionPhysicalProperties
    {
        double MassKg { get; }
        double VolumeCm3 { get; }
        double AreaCm2 { get; }
        double Density { get; }
        FusionPoint CenterOfMass { get; }
    }

    /// <summary>rootComponent.analyses (adsk.fusion.AnalysisCollection).</summary>
    public interface IFusionAnalysisCollection
    {
        IFusionSectionAnalysisInput CreateInput();
        void Add(IFusionSectionAnalysisInput input);
    }

    /// <summary>Section-analysis input.</summary>
    public interface IFusionSectionAnalysisInput
    {
        IFusionConstructionPlane? Plane { set; }
        void SetDistanceCm(double distanceCm);
    }

    /// <summary>Interference-check input (adsk.fusion.InterferenceInput).</summary>
    public interface IFusionInterferenceInput
    {
        bool IncludeCoincidentFaces { set; }
    }

    /// <summary>One interference pair.</summary>
    public interface IFusionInterferenceResult
    {
        string BodyOne { get; }
        string BodyTwo { get; }
        double InterferenceVolumeCm3 { get; }
    }

    // ── Mesh ───────────────────────────────────────────────────────

    /// <summary>component.meshBodies (adsk.fusion.MeshBodyCollection).</summary>
    public interface IFusionMeshBodyCollection
    {
        int Count { get; }
        IFusionMeshBody Item(int index);

        /// <summary>MeshBodyCollection.add (old adsk version: addByFile).</summary>
        IFusionMeshBodyList Add(string filePath, FusionMeshUnits units);
    }

    /// <summary>MeshBodyList (result of import_mesh).</summary>
    public interface IFusionMeshBodyList
    {
        int Count { get; }
        IFusionMeshBody Item(int index);
    }

    public interface IFusionMeshBody
    {
        string Name { get; }
        IFusionBoundingBox? BoundingBox { get; }

        /// <summary>
        /// Per-node signed distances (cm) to the nearest point on <paramref name="other"/>
        /// (compareWith, falling back to displayMesh on the old version).
        /// Null when polygon mesh data is unavailable (reference: throws "Could not obtain polygon mesh data").
        /// </summary>
        IReadOnlyList<double>? CompareWith(IFusionMeshBody other);
    }

    // ── Appearance / materials ──────────────────────────────────────

    public interface IFusionAppearance
    {
        string Name { get; }

        /// <summary>appearance.color.set(R, G, B, A)</summary>
        void SetColor(int red, int green, int blue, int alpha);
    }

    /// <summary>design.appearances (adsk.fusion.AppearanceCollection).</summary>
    public interface IFusionAppearanceCollection
    {
        IFusionAppearance? ItemByName(string name);
        IFusionAppearance AddByCopy(IFusionAppearance source, string name);
        IFusionAppearance Add(string name);
    }

    /// <summary>One material library (appearances of its materials).</summary>
    public interface IFusionMaterialLibrary
    {
        int AppearancesCount { get; }
        IReadOnlyList<IFusionAppearance> Appearances { get; }
    }

    /// <summary>app.materialLibraries (adsk.fusion.MaterialLibraryCollection).</summary>
    public interface IFusionMaterialLibraries
    {
        int Count { get; }
        IFusionMaterialLibrary? Item(int index);
        IFusionMaterialLibrary? ItemByName(string name);
    }

    // ── Parameters ──────────────────────────────────────────────────

    /// <summary>
    /// design.userParameters (adsk.fusion.UserParameterCollection), upgraded for
    /// create_parameter/delete_parameter (add/delete; existing get_parameters reads).
    /// </summary>
    public interface IFusionUserParameterCollection : System.Collections.Generic.IEnumerable<IFusionParameter>
    {
        int Count { get; }
        IFusionParameter Item(int index);
        IFusionParameter? ItemByName(string name);

        /// <summary>
        /// Creates a user parameter. <paramref name="value"/> is in cm (or the value's own
        /// unit when a non-empty <paramref name="unit"/> is given) — the adapter applies the
        /// reference's ValueInput rules: expression "value unit" when a unit is set, raw real otherwise.
        /// </summary>
        IFusionParameter Add(string name, double value, string? unit, string? comment);

        void Delete(IFusionParameter parameter);
    }

    // ── Export ──────────────────────────────────────────────────────

    /// <summary>Export target: a BRep body or an occurrence (STL / STEP / Fusion archive).</summary>
    public interface IFusionExportTarget
    {
    }

    /// <summary>design.exportManager (adsk.fusion.ExportManager; STL refinement medium).</summary>
    public interface IFusionExportManager
    {
        /// <summary>STL export (MeshRefinementSettings.MediumMeshRefinement).</summary>
        void ExportStl(IFusionExportTarget entity, string filePath);

        /// <summary>STEP export (reference arg order: file_path, entity).</summary>
        void ExportStep(string filePath, IFusionExportTarget entity);

        /// <summary>Native Fusion archive (.f3d archive) export.</summary>
        void ExportFusionArchive(string filePath);
    }

    // ── CAM ─────────────────────────────────────────────────────────

    /// <summary>
    /// CAM product resolved via doc.products.itemByProductType("CAMProductType")
    /// (+ CAMEnvironment workspace activation on the old version). Null when the
    /// CAM product is unavailable — handlers raise the reference error.
    /// </summary>
    public interface IFusionCamProduct
    {
        IFusionCamSetupCollection Setups { get; }

        /// <summary>cam.generateAllToolpaths(overwrite) — async future.</summary>
        IFusionCamFuture GenerateAllToolpaths(bool overwrite);

        /// <summary>cam.generateToolpath(target) for a single setup/operation/operation-set.</summary>
        IFusionCamFuture GenerateToolpath(IFusionCamToolpathTarget target);

        /// <summary>cam.postProcess(target, PostProcessInput.create(...)).</summary>
        void PostProcess(IFusionCamPostTarget target, IFusionCamPostInput input);

        /// <summary>PostProcessInput.create(setup_name, post_config, output_folder, units).</summary>
        IFusionCamPostInput CreatePostProcessInput(string setupName, string postConfigPath, string outputFolder, FusionCamOutputUnits units);

        /// <summary>
        /// The build's local generic post-processor folder (reference
        /// <c>getattr(cam, "genericPostFolder", None)</c>); null/empty when the
        /// build's posts are cloud-library based.
        /// </summary>
        string? GenericPostFolder { get; }
    }

    /// <summary>Async CAM future (reference: bounded wait, 0.1s polling, 25s deadline).</summary>
    public interface IFusionCamFuture
    {
        bool IsCompleted { get; }
        void Wait();
    }

    /// <summary>Target of GenerateToolpath: a setup or an operation.</summary>
    public interface IFusionCamToolpathTarget
    {
    }

    /// <summary>Target of PostProcess: a setup or an operation.</summary>
    public interface IFusionCamPostTarget
    {
    }

    public interface IFusionCamPostInput
    {
        bool OpenInEditor { set; }
    }

    /// <summary>One CAM setup (adsk.cam CamSetup) — also a toolpath/post-process target.</summary>
    public interface IFusionCamSetup : IFusionCamToolpathTarget, IFusionCamPostTarget
    {
        string Name { get; }
        bool IsValid { get; }
        IFusionCamOperationCollection Operations { get; }

        /// <summary>Set a setup-level parameter; false when the parameter doesn't exist (reference: False).</summary>
        bool SetParameter(string parameterName, string expression);
    }

    public interface IFusionCamSetupInput
    {
        /// <summary>Input.models = ObjectCollection of bodies.</summary>
        void SetModels(IReadOnlyList<IFusionBody> bodies);
        string? Name { set; }
    }

    /// <summary>cam setups collection (adsk.cam CamSetupCollection).</summary>
    public interface IFusionCamSetupCollection
    {
        int Count { get; }
        IFusionCamSetup Item(int index);

        /// <summary>CreateInput(operationTypes)</summary>
        IFusionCamSetupInput CreateInput(FusionCamOperationType operationType);

        /// <summary>Setups.add(input)</summary>
        IFusionCamSetup Add(IFusionCamSetupInput input);
    }

    /// <summary>One CAM operation (adsk.cam CamOperation) — also a toolpath/post-process target.</summary>
    public interface IFusionCamOperation : IFusionCamToolpathTarget, IFusionCamPostTarget
    {
        string Name { get; }
        bool HasToolpath { get; }
        bool IsValid { get; }
        IFusionCamToolInfo? Tool { get; }

        /// <summary>Parameters readable as name/expression pairs (reference iterates op.parameters).</summary>
        IReadOnlyList<IFusionCamParameter> Parameters { get; }

        bool SetParameter(string parameterName, string expression);
    }

    public interface IFusionCamOperationInput
    {
        string? Name { set; }
    }

    /// <summary>cam operations collection (adsk.cam CamOperationCollection) — also a whole-setup toolpath target.</summary>
    public interface IFusionCamOperationCollection : IFusionCamToolpathTarget
    {
        int Count { get; }
        IFusionCamOperation Item(int index);

        /// <summary>CreateInput(strategy, ...)</summary>
        IFusionCamOperationInput CreateInput(string strategy);

        /// <summary>Operations.add(input) — may return null on the old version (reference: op is None).</summary>
        IFusionCamOperation? Add(IFusionCamOperationInput input);
    }

    /// <summary>One CAM parameter (reference reads name/expression; sets expression by string).</summary>
    public interface IFusionCamParameter
    {
        string Name { get; }

        /// <summary>Expression as text, null when it cannot be read (reference: except pass).</summary>
        string? Expression { get; set; }

        bool TryGetExpression(out string expression);
    }

    /// <summary>CAM tool info (reference: op.tool.description).</summary>
    public interface IFusionCamToolInfo
    {
        string Description { get; }
    }
}
