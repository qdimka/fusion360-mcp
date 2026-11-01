namespace Fusion360.Mcp.AddIn;

/// <summary>
/// The D5 facade: the whole adsk surface the command handlers may touch,
/// in one thin abstraction. Handlers run against <see cref="IFusionContext" />;
/// the thin adsk adapter (<see cref="AdskFusionContext" />) implements it for
/// the real Fusion, and the test fakes implement it without Fusion.
/// </summary>
public interface IFusionContext
{
    /// <summary>The active design (throws <see cref="FusionCommandException"/> when there is no active design).</summary>
    IFusionDesign Design { get; }

    /// <summary>The active viewport, null when the product has none.</summary>
    IFusionViewport? Viewport { get; }

    /// <summary>UI command definitions (undo/redo).</summary>
    IUiCommands Ui { get; }

    /// <summary>Message-pump equivalent of the reference's adsk.doEvents().</summary>
    void DoEvents();

    /// <summary>
    /// Best-effort scene snapshot for mutation deltas (body count, mass in
    /// grams, overall bbox). Returns null when the scene is not readable
    /// (reference command_handler._snapshot semantics).
    /// </summary>
    SceneSnapshot? TakeSnapshot();

    /// <summary>adsk.app.measureManager (measure_distance / measure_angle — T7).</summary>
    IFusionMeasureManager MeasureManager { get; }

    /// <summary>adsk.app.materialLibraries (set_appearance — T7).</summary>
    IFusionMaterialLibraries MaterialLibraries { get; }

    /// <summary>
    /// The document's CAM product (T7 CAM handlers), resolved the way the
    /// reference's <c>_get_cam</c> does: explicit CAMProductType lookup,
    /// falling back to activating the CAMEnvironment workspace on the
    /// old version. Null when the CAM product is unavailable — handlers
    /// raise the reference error.
    /// </summary>
    IFusionCamProduct? CamProduct { get; }
}

/// <summary>One mutation snapshot: body count, total mass (g), overall bbox.</summary>
public sealed class SceneSnapshot
{
    public SceneSnapshot(int bodyCount, double massGrams, FusionPoint? bboxMin, FusionPoint? bboxMax)
    {
        BodyCount = bodyCount;
        MassGrams = massGrams;
        BBoxMin = bboxMin;
        BBoxMax = bboxMax;
    }

    public int BodyCount { get; }
    public double MassGrams { get; }
    public FusionPoint? BBoxMin { get; }
    public FusionPoint? BBoxMax { get; }
}

/// <summary>UI command lookup (reference: ui.commandDefinitions.itemById).</summary>
public interface IUiCommands
{
    /// <summary>Looks up a command by id; null when the workspace has no such command.</summary>
    IUiCommandDefinition? ItemById(string id);
}

/// <summary>One UI command (undo/redo).</summary>
public interface IUiCommandDefinition
{
    void Execute();
}
