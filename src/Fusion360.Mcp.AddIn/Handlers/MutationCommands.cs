using System.Text.Json.Nodes;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// Mutation-command bookkeeping: the closed set of scene-mutating
/// commands and the reference <c>_compute_deltas</c> payload builder.
/// </summary>
public static class MutationCommands
{
    /// <summary>
    /// Commands that can change body mass / bbox / body count
    /// (reference <c>_MUTATION_COMMANDS</c>; kept in lockstep with
    /// Core's <c>MockResponses.MutationCommands</c> — the catalog
    /// parity check verifies both).
    /// </summary>
    public static readonly HashSet<string> Set = new(StringComparer.Ordinal)
    {
        // feature ops
        "extrude",
        "revolve",
        "sweep",
        "loft",
        "fillet",
        "chamfer",
        "shell",
        "mirror",
        "create_hole",
        "rectangular_pattern",
        "circular_pattern",
        "create_thread",
        "draft_faces",
        "split_body",
        "split_face",
        "offset_faces",
        "scale_body",
        "suppress_feature",
        "unsuppress_feature",
        // body ops
        "move_body",
        "boolean_operation",
        // primitives
        "create_box",
        "create_cylinder",
        "create_sphere",
        "create_torus",
        // surface / sheet metal (can produce / thicken bodies)
        "thicken_surface",
        "patch_surface",
        "stitch_surfaces",
        "ruled_surface",
        "trim_surface",
        "create_flange",
        "create_bend",
        "flat_pattern",
        "unfold",
        // scene-wide
        "delete_all",
        "undo",
        // parametric & agent-authored changes
        "set_parameter",
        // sketch constraint mutation
        "auto_constrain",
        // construction geometry / appearance
        "create_ucs",
        "set_color",
    };

    /// <summary>
    /// Port of reference <c>_compute_deltas</c>: counts + masses +
    /// bboxes. The snapshot's bbox dict is the reference
    /// <c>_bbox_dict</c> shape ({min, max}).
    /// </summary>
    public static JsonObject Deltas(SceneSnapshot before, SceneSnapshot after)
    {
        return new JsonObject
        {
            ["body_count_before"] = before.BodyCount,
            ["body_count_after"] = after.BodyCount,
            ["body_count_delta"] = after.BodyCount - before.BodyCount,
            ["mass_g_before"] = before.MassGrams,
            ["mass_g_after"] = after.MassGrams,
            ["mass_g_delta"] = after.MassGrams - before.MassGrams,
            ["bbox_before"] = BboxDict(before),
            ["bbox_after"] = BboxDict(after),
        };
    }

    /// <summary>
    /// P4: true when the model moved between two snapshots (body count,
    /// total mass, or overall bbox differs) — the diff a failed mutation
    /// left behind. Computed from the same values <see cref="Deltas"/>
    /// reports, so <c>partial_effects</c> and the attached deltas can never
    /// disagree (TOOL_CONTRACTS §1.2).
    /// </summary>
    public static bool HasPartialEffects(SceneSnapshot before, SceneSnapshot after)
    {
        if (before.BodyCount != after.BodyCount)
        {
            return true;
        }

        if (before.MassGrams - after.MassGrams != 0.0)
        {
            return true;
        }

        return !SameBBox(before, after);
    }

    /// <summary>Reference <c>_bbox_dict</c>: min/max arrays, or null.</summary>
    private static JsonNode? BboxDict(SceneSnapshot s)
    {
        if (s.BBoxMin is null || s.BBoxMax is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["min"] = new JsonArray { s.BBoxMin.Value.X, s.BBoxMin.Value.Y, s.BBoxMin.Value.Z },
            ["max"] = new JsonArray { s.BBoxMax.Value.X, s.BBoxMax.Value.Y, s.BBoxMax.Value.Z },
        };
    }

    /// <summary>True when two snapshots' overall bboxes are equal (null = no bodies, like the reference).</summary>
    private static bool SameBBox(SceneSnapshot before, SceneSnapshot after)
    {
        if (before.BBoxMin is null || before.BBoxMax is null || after.BBoxMin is null || after.BBoxMax is null)
        {
            // A null bbox means "no bodies" — equal only when both sides have none.
            return before.BBoxMin is null
                && before.BBoxMax is null
                && after.BBoxMin is null
                && after.BBoxMax is null;
        }

        var minEq =
            before.BBoxMin.Value.X == after.BBoxMin.Value.X
            && before.BBoxMin.Value.Y == after.BBoxMin.Value.Y
            && before.BBoxMin.Value.Z == after.BBoxMin.Value.Z;
        var maxEq =
            before.BBoxMax.Value.X == after.BBoxMax.Value.X
            && before.BBoxMax.Value.Y == after.BBoxMax.Value.Y
            && before.BBoxMax.Value.Z == after.BBoxMax.Value.Z;
        return minEq && maxEq;
    }
}
