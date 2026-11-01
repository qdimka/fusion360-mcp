namespace Fusion360.Mcp.AddIn;

/// <summary>
/// The active design (reference: adsk.fusion.Design, resolved the way the
/// reference's <c>_design()</c> does — explicit DesignProductType lookup,
/// so a Manufacture workspace cannot break the design handlers).
/// </summary>
public interface IFusionDesign
{
    /// <summary>The parent document's file name.</summary>
    string DocumentName { get; }

    /// <summary>Stable identity for the open document; unlike its display name, it is not user-editable.</summary>
    string DocumentIdentity { get; }

    /// <summary>The product type string (e.g. "Design").</summary>
    string ProductType { get; }

    /// <summary>Current design type; see <see cref="DesignTypes"/> ids.</summary>
    int DesignType { get; set; }

    /// <summary>The root component.</summary>
    IFusionRootComponent RootComponent { get; }

    /// <summary>User parameters (get_parameters / create_parameter / delete_parameter / set_parameter).</summary>
    IFusionUserParameterCollection UserParameters { get; }

    /// <summary>design.appearances (adsk.fusion.AppearanceCollection — set_color / set_appearance).</summary>
    IFusionAppearanceCollection Appearances { get; }

    /// <summary>design.exportManager (export_stl / export_step / export_fusion_file).</summary>
    IFusionExportManager ExportManager { get; }

    /// <summary>InterferenceInput (check_interference).</summary>
    IFusionInterferenceInput CreateInterferenceInput(IReadOnlyList<IFusionBody> bodies);

    /// <summary>
    /// design.analyzeInterference — null when there is no interference
    /// (reference: <c>interference is None</c> path), otherwise the result pairs.
    /// </summary>
    IReadOnlyList<IFusionInterferenceResult>? AnalyzeInterference(IFusionInterferenceInput input);

    /// <summary>
    /// The feature timeline; null for direct designs (the reference's
    /// <c>hasattr(design, "timeline")</c> check).
    /// </summary>
    IFusionTimeline? Timeline { get; }
}

/// <summary>One user parameter (reference: adsk.fusion.Parameter).</summary>
public interface IFusionParameter
{
    string Name { get; }

    /// <summary>The value in Fusion internal units (cm, radians).</summary>
    double Value { get; }

    /// <summary>
    /// The parameter expression (read by get_parameters/set_parameter; the
    /// setter applies the reference's ValueInput.createByString path).
    /// </summary>
    string? Expression { get; set; }
    string? Unit { get; }
    string? Comment { get; }
}

/// <summary>The design's feature timeline (delete_all unwinds it).</summary>
public interface IFusionTimeline
{
    int Count { get; }

    /// <summary>Timeline item i.</summary>
    IFusionTimelineItem Item(int index);
}

/// <summary>
/// One timeline item. The reference deletes the *entity* the item wraps
/// (the TimelineObject itself has no deleteMe).
/// </summary>
public interface IFusionTimelineItem
{
    string Name { get; }

    /// <summary>The wrapped entity; null when the item exposes none.</summary>
    IFusionTimelineEntity? Entity { get; }

    /// <summary>Reference item.isSuppressed — suppress/unsuppress_feature toggles it.</summary>
    bool IsSuppressed { get; set; }
}

/// <summary>The deletable entity behind a timeline item.</summary>
public interface IFusionTimelineEntity
{
    string Name { get; }
    void DeleteMe();
}
