namespace Fusion360.Mcp.AddIn;

/// <summary>The active product viewport (render_view).</summary>
public interface IFusionViewport
{
    /// <summary>The viewport camera (get/set to reposition).</summary>
    IFusionCamera Camera { get; set; }

    /// <summary>Fits the model into the viewport (can fail on empty designs).</summary>
    void Fit();

    /// <summary>Redraws the viewport.</summary>
    void Refresh();

    /// <summary>
    /// Saves the viewport to a PNG file; true on success (reference
    /// viewport.saveAsImageFile).
    /// </summary>
    bool SaveAsImageFile(string path, int width, int height);
}

/// <summary>The viewport camera (eye/target/up in world cm).</summary>
public interface IFusionCamera
{
    FusionPoint Eye { get; set; }
    FusionPoint Target { get; set; }
    FusionPoint UpVector { get; set; }

    /// <summary>Camera model (0 = perspective, 1 = parallel, 2 = dimetric).</summary>
    int CameraType { get; set; }

    /// <summary>Animates transitions; false snaps the view.</summary>
    bool IsSmoothTransition { get; set; }

    /// <summary>Fits the view to the active document bounds (reference set_view.isFitView — T7).</summary>
    bool IsFitView { get; set; }
}
