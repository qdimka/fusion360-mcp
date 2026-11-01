namespace Fusion360.Mcp.AddIn;

/// <summary>
/// One timeline feature (reference: adsk.fusion.Feature) — the result of a
/// feature collection's <c>add(input)</c>. Handlers only need the name
/// (plus hole-specific measurements, see <see cref="IFusionHoleFeature"/>).
/// </summary>
public interface IFusionFeature
{
    /// <summary>The feature's timeline name (reference feat.name).</summary>
    string Name { get; }

    /// <summary>The feature's first resulting body (reference feat.bodies.item(0)).</summary>
    IFusionBody FirstBody { get; }
}

/// <summary>A hole feature: carries the resolved diameter and affected bodies (reference create_hole).</summary>
public interface IFusionHoleFeature : IFusionFeature
{
    /// <summary>The feature's resolved hole diameter in cm (reference feat.holeDiameter.value).</summary>
    double HoleDiameterCm { get; }

    /// <summary>Names of the bodies the hole cut (reference [b.name for b in feat.bodies]).</summary>
    IReadOnlyList<string> BodyNames { get; }
}

/// <summary>A base feature (direct-primitive wrapper: startEdit / add body / finishEdit).</summary>
public interface IFusionBaseFeature
{
    /// <summary>Begins the base-feature edit (reference base_feat.startEdit()).</summary>
    void StartEdit();

    /// <summary>Finishes the base-feature edit (reference base_feat.finishEdit()).</summary>
    void FinishEdit();
}

/// <summary>A path (sweep reference curve) — reference root.features.createPath.</summary>
public interface IFusionPath
{
}

/// <summary>
/// A sketch profile (reference sketch.profiles.item(i)) — the closed loop a
/// feature extrudes / revolves / sweeps / lofts / patches.
/// </summary>
public interface IFusionProfile
{
}

/// <summary>A sketch's profile collection (reference sketch.profiles).</summary>
public interface IFusionProfileCollection
{
    int Count { get; }
    IFusionProfile Item(int index);
}

/// <summary>A thread-data query (reference threads.threadDataQuery).</summary>
public interface IFusionThreadDataQuery
{
    /// <summary>The thread type string (reference thread_data.threadType).</summary>
    string ThreadType { get; set; }
}

/// <summary>
/// The feature collections of a root component — C# port of the reference
/// <c>root.features.*</c> surface (T6 feature / body-op / primitive /
/// surface handlers). All inputs are adsk-agnostic (facade entities + cm
/// doubles); the adsk adapter maps them onto the real feature inputs.
/// </summary>
public interface IFusionFeatures
{
    IFusionExtrudeFeatures ExtrudeFeatures { get; }
    IFusionRevolveFeatures RevolveFeatures { get; }
    IFusionSweepFeatures SweepFeatures { get; }
    IFusionLoftFeatures LoftFeatures { get; }
    IFusionFilletFeatures FilletFeatures { get; }
    IFusionChamferFeatures ChamferFeatures { get; }
    IFusionShellFeatures ShellFeatures { get; }
    IFusionMirrorFeatures MirrorFeatures { get; }
    IFusionHoleFeatures HoleFeatures { get; }
    IFusionRectPatternFeatures RectangularPatternFeatures { get; }
    IFusionCirclePatternFeatures CircularPatternFeatures { get; }
    IFusionThreadFeatures ThreadFeatures { get; }
    IFusionDraftFeatures DraftFeatures { get; }
    IFusionSplitBodyFeatures SplitBodyFeatures { get; }
    IFusionSplitFaceFeatures SplitFaceFeatures { get; }
    IFusionOffsetFeatures OffsetFeatures { get; }
    IFusionScaleFeatures ScaleFeatures { get; }
    IFusionMoveFeatures MoveFeatures { get; }
    IFusionCombineFeatures CombineFeatures { get; }
    IFusionPatchFeatures PatchFeatures { get; }
    IFusionStitchFeatures StitchFeatures { get; }
    IFusionThickenFeatures ThickenFeatures { get; }
    IFusionRuledSurfaceFeatures RuledSurfaceFeatures { get; }
    IFusionTrimFeatures TrimFeatures { get; }
    IFusionBaseFeatures BaseFeatures { get; }

    /// <summary>adsk.fusion.FlangeFeatureCollection (T7 sheet metal: add_flange).</summary>
    IFusionFlangeFeatures FlangeFeatures { get; }

    /// <summary>adsk.fusion.BendFeatureCollection (T7 sheet metal: add_bend).</summary>
    IFusionBendFeatures BendFeatures { get; }

    /// <summary>adsk.fusion.FlatPatternFeatureCollection (T7 sheet metal: create_flat_pattern).</summary>
    IFusionFlatPatternFeatures FlatPatternFeatures { get; }

    /// <summary>adsk.fusion.UnfoldFeatureCollection (T7 sheet metal: unfold_sheet_metal).</summary>
    IFusionUnfoldFeatures UnfoldFeatures { get; }

    /// <summary>Builds a sweep path from a sketch curve (reference root.features.createPath).</summary>
    IFusionPath CreatePath(IFusionSketchCurve curve);
}

// ----------------------------------------------------------------------
// Feature input builders (one per reference feature collection)
// ----------------------------------------------------------------------

public interface IFusionExtrudeFeatures
{
    IFusionExtrudeInput CreateInput(IFusionProfile profile, FusionFeatureOperation operation);
}

public interface IFusionExtrudeInput
{
    /// <summary>Extrudes a distance extent (reference setDistanceExtent(reverse, dist)).</summary>
    void SetDistanceExtent(bool reverse, double distanceCm);

    /// <summary>
    /// Extrudes a distance extent given as a parameter expression
    /// (reference create_by_string — create_box_parametric's parametric height).
    /// </summary>
    void SetDistanceExtentExpression(string expression);

    /// <summary>Extrudes symmetrically about the profile (reference setSymmetricExtent(dist, mirror)).</summary>
    void SetSymmetricExtent(double distanceCm, bool mirror);

    IFusionFeature Add();
}

public interface IFusionRevolveFeatures
{
    /// <summary>The axis may be a construction axis or a sketch construction line (reference revolve axis_entity).</summary>
    IFusionRevolveInput CreateInput(IFusionProfile profile, IFusionRevolveAxis axis, FusionFeatureOperation operation);
}

/// <summary>A revolve axis: a construction axis or a sketch construction line.</summary>
public interface IFusionRevolveAxis
{
}

public interface IFusionRevolveInput
{
    /// <summary>Revolves an angle extent (reference setAngleExtent(False, "{angle} deg")).</summary>
    void SetAngleExtent(bool reverse, double angleDegrees);

    IFusionFeature Add();
}

public interface IFusionSweepFeatures
{
    IFusionSweepInput CreateInput(IFusionProfile profile, IFusionPath path, FusionFeatureOperation operation);
}

public interface IFusionSweepInput
{
    IFusionFeature Add();
}

public interface IFusionLoftFeatures
{
    IFusionLoftInput CreateInput(FusionFeatureOperation operation);
}

public interface IFusionLoftInput
{
    /// <summary>Adds a loft section (reference loft_input.loftSections.add(profile)).</summary>
    void AddSection(IFusionProfile profile);

    IFusionFeature Add();
}

public interface IFusionFilletFeatures
{
    IFusionFilletInput CreateInput();
}

public interface IFusionFilletInput
{
    /// <summary>Reference addConstantRadiusEdgeSet(edges, radius, includeAdjacent).</summary>
    void AddConstantRadiusEdgeSet(IReadOnlyList<IFusionBodyEdge> edges, double radiusCm, bool includeAdjacent);

    IFusionFeature Add();
}

public interface IFusionChamferFeatures
{
    IFusionChamferInput CreateInput(IReadOnlyList<IFusionBodyEdge> edges, bool includeAdjacent);
}

public interface IFusionChamferInput
{
    /// <summary>Reference setToEqualDistance(distance).</summary>
    void SetToEqualDistance(double distanceCm);

    IFusionFeature Add();
}

public interface IFusionShellFeatures
{
    IFusionShellInput CreateInput(IReadOnlyList<IFusionBody> bodies);
}

public interface IFusionShellInput
{
    /// <summary>Faces to remove (reference inp.facesToRemove).</summary>
    IReadOnlyList<IFusionFace> FacesToRemove { set; }

    /// <summary>Reference inp.insideThickness.</summary>
    double InsideThicknessCm { set; }

    IFusionFeature Add();
}

public interface IFusionMirrorFeatures
{
    IFusionMirrorInput CreateInput(IReadOnlyList<IFusionBody> bodies, IFusionConstructionPlane plane);
}

public interface IFusionMirrorInput
{
    IFusionFeature Add();
}

public interface IFusionHoleFeatures
{
    /// <summary>Reference createSimpleInput(diameter): takes the DIAMETER, not the radius.</summary>
    IFusionHoleInput CreateSimpleInput(double diameterCm);
}

public interface IFusionHoleInput
{
    /// <summary>Reference setPositionBySketchPoint(sketch_pt).</summary>
    void SetPositionBySketchPoint(IFusionSketchPoint point);

    /// <summary>Reference setDistanceExtent(depth).</summary>
    void SetDistanceExtent(double depthCm);

    /// <summary>Reference participantBodies — restricts the cut (a plain list, not a collection).</summary>
    IReadOnlyList<IFusionBody> ParticipantBodies { set; }

    IFusionHoleFeature Add();
}

public interface IFusionRectPatternFeatures
{
    /// <summary>Reference createInput(bodies, xaxis, xcount, xspacing, SpacingPatternDistanceType).</summary>
    IFusionRectPatternInput CreateInput(IReadOnlyList<IFusionBody> bodies, IFusionConstructionAxis xAxis, int xCount, double xSpacingCm);
}

public interface IFusionRectPatternInput
{
    /// <summary>Reference setDirectionTwo(yaxis, ycount, yspacing).</summary>
    void SetDirectionTwo(IFusionConstructionAxis yAxis, int yCount, double ySpacingCm);

    /// <summary>Reference patterns.add(inp).</summary>
    IFusionFeature Add();
}

public interface IFusionCirclePatternFeatures
{
    IFusionCirclePatternInput CreateInput(IReadOnlyList<IFusionBody> bodies, IFusionConstructionAxis axis);
}

public interface IFusionCirclePatternInput
{
    /// <summary>Reference inp.quantity = ValueInput(count).</summary>
    int Quantity { set; }

    /// <summary>Reference inp.totalAngle = ValueInput("{total_angle} deg").</summary>
    double TotalAngleDegrees { set; }

    IFusionFeature Add();
}

public interface IFusionThreadFeatures
{
    /// <summary>Reference threads.threadDataQuery.</summary>
    IFusionThreadDataQuery ThreadDataQuery { get; }

    IFusionThreadInput CreateInput(IFusionFace face, IFusionThreadDataQuery data);
}

public interface IFusionThreadInput
{
    bool IsModeled { set; }
    bool IsFullLength { set; }

    /// <summary>Reference threadLength (only when not full length).</summary>
    double ThreadLengthCm { set; }

    IFusionFeature Add();
}

public interface IFusionDraftFeatures
{
    /// <summary>Reference createInput(face_array, pull_plane, is_tangent_chain).</summary>
    IFusionDraftInput CreateInput(IReadOnlyList<IFusionFace> faces, IFusionConstructionPlane pullPlane, bool isTangentChain);
}

public interface IFusionDraftInput
{
    /// <summary>Reference setSingleAngle(False, "{angle} deg").</summary>
    void SetSingleAngle(bool twoSided, double angleDegrees);

    IFusionFeature Add();
}

public interface IFusionSplitBodyFeatures
{
    /// <summary>The tool is a body or a construction plane (reference splitting_body vs splitting_plane).</summary>
    IFusionSplitBodyInput CreateBodyInput(IFusionBody body, IFusionBody tool, bool extendTool);
    IFusionSplitBodyInput CreatePlaneInput(IFusionBody body, IFusionConstructionPlane plane, bool extendTool);
}

public interface IFusionSplitBodyInput
{
    IFusionFeature Add();
}

public interface IFusionSplitFaceFeatures
{
    IFusionSplitFaceInput CreateInput(IReadOnlyList<IFusionFace> faces, IFusionConstructionPlane plane, bool extendTool);
}

public interface IFusionSplitFaceInput
{
    IFusionFeature Add();
}

public interface IFusionOffsetFeatures
{
    IFusionOffsetInput CreateInput(IReadOnlyList<IFusionFace> faces, double distanceCm, FusionFeatureOperation operation);
}

public interface IFusionOffsetInput
{
    IFusionFeature Add();
}

public interface IFusionScaleFeatures
{
    /// <summary>Uniform scale (reference single-scale createInput).</summary>
    IFusionScaleInput CreateUniformInput(IReadOnlyList<IFusionBody> bodies, FusionPoint anchor, double scale);

    /// <summary>Non-uniform scale (reference three-axis createInput).</summary>
    IFusionScaleInput CreateAxisInput(IReadOnlyList<IFusionBody> bodies, FusionPoint anchor, double scaleX, double scaleY, double scaleZ);
}

public interface IFusionScaleInput
{
    IFusionFeature Add();
}

public interface IFusionMoveFeatures
{
    /// <summary>Reference createInput(bodies, translation-matrix).</summary>
    IFusionMoveInput CreateInput(IReadOnlyList<IFusionBody> bodies, FusionPoint translationCm);
}

public interface IFusionMoveInput
{
    IFusionFeature Add();
}

public interface IFusionCombineFeatures
{
    /// <summary>Reference combineFeatures.createInput(target, tool_coll).</summary>
    IFusionCombineInput CreateInput(IFusionBody target, IReadOnlyList<IFusionBody> tools);
}

public interface IFusionCombineInput
{
    /// <summary>Reference inp.operation = op.</summary>
    FusionFeatureOperation Operation { set; }

    IFusionFeature Add();
}

public interface IFusionPatchFeatures
{
    IFusionPatchInput CreateInput(IFusionProfile profile, FusionFeatureOperation operation);
}

public interface IFusionPatchInput
{
    /// <summary>Reference inp.boundaryContinuity = cont.</summary>
    FusionSurfaceContinuity BoundaryContinuity { set; }

    IFusionFeature Add();
}

public interface IFusionStitchFeatures
{
    IFusionStitchInput CreateInput(IReadOnlyList<IFusionBody> bodies, double toleranceCm, FusionFeatureOperation operation);
}

public interface IFusionStitchInput
{
    IFusionFeature Add();
}

public interface IFusionThickenFeatures
{
    /// <summary>Reference thickenFeatures.createInput(faces, thickness, False, op, symmetric).</summary>
    IFusionThickenInput CreateInput(IReadOnlyList<IFusionFace> faces, double thicknessCm, bool symmetric, FusionFeatureOperation operation);
}

public interface IFusionThickenInput
{
    IFusionFeature Add();
}

public interface IFusionRuledSurfaceFeatures
{
    IFusionRuledInput CreateInput(IFusionBodyEdge edge, double distanceCm);
}

public interface IFusionRuledInput
{
    IFusionFeature Add();
}

public interface IFusionTrimFeatures
{
    IFusionTrimInput CreateInput(IFusionBody body, IFusionBody tool);
}

public interface IFusionTrimInput
{
    IFusionFeature Add();
}

public interface IFusionBaseFeatures
{
    /// <summary>Reference baseFeatures.add().</summary>
    IFusionBaseFeature Add();
}
