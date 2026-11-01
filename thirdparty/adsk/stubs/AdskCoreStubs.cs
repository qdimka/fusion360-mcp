// Compile-time stub of the Autodesk .NET API surface used by
// Fusion360.Mcp.AddIn (tasks T3-T5; adsk.fusion surface in
// AdskFusionStubs.cs). See thirdparty/adsk/README.md.
//
// This is NOT the real adsk stack: every member body throws at runtime.
// It exists so the add-in project (and its tests) compile against the
// documented C# adsk surface on hosts without a Fusion install
// (PLAN.md decision D3). The stub assembly is excluded from the add-in
// build output (CopyLocal=false), so it never ships into the AddIns
// folder; at runtime the add-in resolves adsknet in-process from the
// Fusion runtime.
//
// Signature notes: the C# adsk API mixes a camelCase static factory
// (Application.get()) with PascalCase members (RegisterCustomEvent,
// FireCustomEvent, UnregisterCustomEvent, CustomEvent.Add/Remove,
// CustomEventHandler.Notify) — as in Autodesk's C# add-in templates.
// When the genuine adsk assemblies are dropped into thirdparty/adsk/,
// recompile the AddIn against them and reconcile any case differences.

namespace adsk.core
{
    using System;

    /// <summary>Autodesk add-in entry interface; Fusion instantiates the implementing class.</summary>
    public interface AddIn
    {
        void Run();
        void Stop();
    }

    /// <summary>Fusion 360 application object (real: adsknet.dll).</summary>
    public sealed class Application
    {
        private const string StubError =
            "adsknet.dll is a compile-time stub (thirdparty/adsk/stubs). Load the add-in inside Fusion 360.";

        /// <summary>Static factory for the running application (camelCase in the real API).</summary>
        public static Application get() => throw new NotSupportedException(StubError);

        /// <summary>The user interface (message boxes, etc.).</summary>
        public UserInterface UserInterface => throw new NotSupportedException(StubError);

        /// <summary>Registers a user-defined event that can be fired from any thread.</summary>
        public CustomEvent RegisterCustomEvent(string eventId) => throw new NotSupportedException(StubError);

        /// <summary>Fires a registered custom event; handlers run on Fusion's main thread.</summary>
        public int FireCustomEvent(string eventId) => throw new NotSupportedException(StubError);

        /// <summary>Unregisters a previously registered custom event.</summary>
        public bool UnregisterCustomEvent(string eventId) => throw new NotSupportedException(StubError);

        /// <summary>The active document (null when none is open).</summary>
        public Document? ActiveDocument => throw new NotSupportedException(StubError);

        /// <summary>The active product (Design, Manufacture, ...).</summary>
        public adsk.fusion.Product? ActiveProduct => throw new NotSupportedException(StubError);

        /// <summary>The active viewport (null when the product has none).</summary>
        public Viewport? ActiveViewport => throw new NotSupportedException(StubError);

        /// <summary>Distance/angle measurements (reference app.measureManager — T7 inspection).</summary>
        public adsk.fusion.MeasureManager MeasureManager => throw new NotSupportedException(StubError);

        /// <summary>Material/appearance libraries (reference app.materialLibraries — set_appearance).</summary>
        public adsk.fusion.MaterialLibraryCollection MaterialLibraries => throw new NotSupportedException(StubError);
    }

    /// <summary>UI entry points (subset used by the add-in).</summary>
    public sealed class UserInterface
    {
        /// <summary>Shows a modal message box; returns a button id.</summary>
        public int MessageBox(string caption, string text) => throw new NotSupportedException("adsknet stub");

        /// <summary>Command definitions (undo/redo and other UI commands).</summary>
        public CommandDefinitions CommandDefinitions => throw new NotSupportedException("adsknet stub");

        /// <summary>Workspaces (reference ui.workspaces — T7 CAMEnvironment activation fallback).</summary>
        public WorkspaceCollection Workspaces => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>Workspaces of the user interface (reference: adsk.core.WorkspaceCollection).</summary>
    public sealed class WorkspaceCollection
    {
        /// <summary>Looks up a workspace by id (e.g. "CAMEnvironment"); null when absent.</summary>
        public Workspace? ItemById(string id) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>One workspace (reference: adsk.core.Workspace).</summary>
    public sealed class Workspace
    {
        /// <summary>Activates the workspace (reference ws.activate()).</summary>
        public void Activate() => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>The active document (reference: adsk.core.Document).</summary>
    public sealed class Document
    {
        /// <summary>Document file name.</summary>
        public string Name => throw new NotSupportedException("adsknet stub");

        /// <summary>Unique creation ID for the open document's lifetime.</summary>
        public string CreationId => throw new NotSupportedException("adsknet stub");

        /// <summary>The products that belong to this document.</summary>
        public adsk.fusion.ProductCollection Products => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>Viewport of the active product (reference: adsk.core.Viewport).</summary>
    public sealed class Viewport
    {
        /// <summary>The viewport camera (get/set to reposition).</summary>
        public Camera Camera { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>Fits the model into the viewport.</summary>
        public void Fit() => throw new NotSupportedException("adsknet stub");

        /// <summary>Redraws the viewport.</summary>
        public void Refresh() => throw new NotSupportedException("adsknet stub");

        /// <summary>Saves the viewport to a PNG file; false when it fails.</summary>
        public bool SaveAsImageFile(string path, int width, int height) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>Camera of a viewport (eye/target/up in world coordinates).</summary>
    public sealed class Camera
    {
        public Point3D Eye { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
        public Point3D Target { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
        public Vector3D UpVector { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>Camera model (0 = perspective, 1 = parallel, 2 = dimetric).</summary>
        public int CameraType { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>Animates transitions; false snaps the view (used by render_view).</summary>
        public bool IsSmoothTransition { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }

        /// <summary>Fits the view to the active document bounds (reference set_view.isFitView — T7).</summary>
        public bool IsFitView { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    /// <summary>A 3-D point (cm, Fusion internal units).</summary>
    public sealed class Point3D
    {
        public double X => throw new NotSupportedException("adsknet stub");
        public double Y => throw new NotSupportedException("adsknet stub");
        public double Z => throw new NotSupportedException("adsknet stub");

        public static Point3D Create(double x, double y, double z) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A 3-D vector.</summary>
    public sealed class Vector3D
    {
        public double X => throw new NotSupportedException("adsknet stub");
        public double Y => throw new NotSupportedException("adsknet stub");
        public double Z => throw new NotSupportedException("adsknet stub");

        public static Vector3D Create(double x, double y, double z) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A collection of objects (reference: adsk.core.ObjectCollection).</summary>
    public sealed class ObjectCollection
    {
        public static ObjectCollection Create() => throw new NotSupportedException("adsknet stub");
        public void Add(object entity) => throw new NotSupportedException("adsknet stub");
        public int Count => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A typed numeric input (reference: adsk.core.ValueInput).</summary>
    public sealed class ValueInput
    {
        public static ValueInput CreateByReal(double value) => throw new NotSupportedException("adsknet stub");
        public static ValueInput CreateByString(string value) => throw new NotSupportedException("adsknet stub");

        /// <summary>The numeric value of the input (reference vi.value).</summary>
        public double Value => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A translation/rotation transform (move_body matrix; reference adsk.core.Matrix3D).</summary>
    public sealed class Matrix3D
    {
        public static Matrix3D Create() => throw new NotSupportedException("adsknet stub");

        /// <summary>The translation component (reference transform.translation).</summary>
        public Vector3D Translation { get => throw new NotSupportedException("adsknet stub"); set => throw new NotSupportedException("adsknet stub"); }
    }

    /// <summary>
    /// An oriented bounding box (reference create_box:
    /// <c>OrientedBoundingBox3D.create(center, xAxis, yAxis, l, w, h)</c>).
    /// </summary>
    public sealed class OrientedBoundingBox3D
    {
        public static OrientedBoundingBox3D Create(Point3D center, Vector3D xAxis, Vector3D yAxis, double length, double width, double height)
            => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>Geometry base (reference adsk.core.Geometry).</summary>
    public class Geometry
    {
        /// <summary>
        /// The surface type of the geometry (reference face.geometry.surfaceType —
        /// unfold's stationary-face detection). Documented assumption (T8): the
        /// genuine adsk may expose this through a cast instead.
        /// </summary>
        public SurfaceTypes SurfaceType => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>
    /// Surface geometry types (reference adsk.core.SurfaceTypes). Values are a
    /// documented assumption (T8) — only the PlaneSurfaceType comparison matters.
    /// </summary>
    public enum SurfaceTypes
    {
        UnknownSurfaceType = 0,
        PlaneSurfaceType = 1,
        CylindricalSurfaceType = 2,
        ConicalSurfaceType = 3,
        SphericalSurfaceType = 4,
    }

    /// <summary>
    /// An RGBA color (reference adsk.core.Color; set_color assigns
    /// <c>Color.create(r, g, b, a)</c> to an appearance).
    /// </summary>
    public sealed class Color
    {
        public static Color Create(int red, int green, int blue, int alpha)
            => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A plane (normal + origin); castable from planar-face geometry (reference adsk.core.Plane).</summary>
    public sealed class Plane : Geometry
    {
        public Vector3D Normal => throw new NotSupportedException("adsknet stub");
        public Point3D Origin => throw new NotSupportedException("adsknet stub");

        /// <summary>Static cast (reference Plane.cast); null when the geometry is not a plane.</summary>
        public static Plane? Cast(Geometry geometry) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>UI command definitions (reference: adsk.core.CommandDefinitions).</summary>
    public sealed class CommandDefinitions
    {
        /// <summary>Looks up a command by its id (e.g. "UndoCommand"); null when absent.</summary>
        public CommandDefinition? ItemById(string id) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>One UI command (reference: adsk.core.CommandDefinition).</summary>
    public sealed class CommandDefinition
    {
        public void Execute() => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>
    /// Message-pump equivalent of the Python reference's adsk.doEvents():
    /// lets Fusion process queued messages while the add-in runs.
    /// NOTE (T5 assumption — verify on a live Fusion install in T8): the
    /// C# API exposes this as a static adsk.core.Environment.DoEvents();
    /// if the genuine adsknet spells it differently, adjust AdskFusionContext.
    /// </summary>
    public static class Environment
    {
        public static void DoEvents() => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>A user-defined event; handlers are invoked on the main thread.</summary>
    public sealed class CustomEvent
    {
        /// <summary>Attaches a handler to the event.</summary>
        public bool Add(CustomEventHandler handler) => throw new NotSupportedException("adsknet stub");

        /// <summary>Detaches a handler from the event.</summary>
        public bool Remove(CustomEventHandler handler) => throw new NotSupportedException("adsknet stub");
    }

    /// <summary>Base class for custom-event handlers; <see cref="Notify"/> runs on the main thread.</summary>
    public abstract class CustomEventHandler
    {
        /// <summary>Called on Fusion's main thread when the event fires.</summary>
        public abstract void Notify(object args);
    }
}
