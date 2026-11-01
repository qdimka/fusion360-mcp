namespace Fusion360.Mcp.Core.Catalog;

/// <summary>Tool effect sets shared by catalog, transport, journal and add-in.</summary>
public static class ToolEffects
{
    /// <summary>Tools that can mutate a document or produce an external side effect.</summary>
    public static readonly HashSet<string> OperationCommands = new(StringComparer.Ordinal)
    {
        "create_sketch", "draw_rectangle", "draw_circle", "draw_line", "extrude", "revolve",
        "fillet", "chamfer", "shell", "mirror", "rename_body", "move_body", "export_stl",
        "boolean_operation", "delete_all", "undo", "sweep", "loft",
        "create_polygon", "draw_arc", "create_hole", "rectangular_pattern", "circular_pattern",
        "create_component", "add_joint", "export_step", "export_f3d", "export_view_sheet", "export",
        "import_mesh", "create_parameter", "set_parameter", "delete_parameter", "add_constraint",
        "auto_constrain", "add_dimension", "create_construction_plane", "create_construction_axis",
        "create_ucs", "draw_spline", "offset_curve", "trim_curve", "extend_curve", "create_thread",
        "draft_faces", "split_body", "split_face", "offset_faces", "scale_body", "create_box",
        "create_box_parametric", "create_cylinder", "create_sphere", "create_torus",
        "create_as_built_joint", "create_rigid_group", "create_section_analysis", "set_appearance",
        "set_color", "project_geometry", "suppress_feature", "unsuppress_feature", "patch_surface",
        "stitch_surfaces", "thicken_surface", "ruled_surface", "trim_surface", "create_flange",
        "create_bend", "flat_pattern", "unfold", "cam_create_setup", "cam_create_operation",
        "cam_generate_toolpath", "cam_post_process", "set_design_type",
    };

    private static readonly HashSet<string> ExternalOutputCommands = new(StringComparer.Ordinal)
    {
        "export_stl", "export_step", "export_f3d", "export_view_sheet", "export", "cam_post_process",
    };

    /// <summary>Operations that change active document state and advance its model revision.</summary>
    public static readonly HashSet<string> DocumentMutationCommands = CreateDocumentMutationCommands();

    /// <summary>True when a tool changes the active Fusion document.</summary>
    public static bool IsDocumentMutation(string command) => DocumentMutationCommands.Contains(command);

    private static HashSet<string> CreateDocumentMutationCommands()
    {
        var commands = new HashSet<string>(OperationCommands, StringComparer.Ordinal);
        commands.ExceptWith(ExternalOutputCommands);
        return commands;
    }
}
