using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Catalog;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// Dispatch of one add-in command frame ({"type": ..., "params": ...})
/// to a response frame. Implemented by <see cref="CommandHandler" />.
/// </summary>
public interface IAddInCommandHandler
{
    /// <summary>Routes the frame and returns the response frame to send back to the client.</summary>
    AddInResponse ExecuteCommand(JsonNode frame);
}

/// <summary>
/// Generic command dispatch — C# port of the reference command_handler.py
/// execute_command routing. T3 shipped only the "ping" smoke command; the
/// T5 adsk handlers (scene / sketch / design-type /
/// render_view / delete_all / undo) are registered in the same table behind
/// the IFusionContext facade (PLAN.md D5). T6-T7 feature / assembly / CAM /
/// export handlers register the same way.
/// </summary>
public sealed class CommandHandler : IAddInCommandHandler
{
    /// <summary>One registered command: facade context + params in, result payload out.</summary>
    private delegate JsonObject Command(IFusionContext ctx, JsonObject p);

    private static readonly Dictionary<string, Command> Commands =
        new(StringComparer.Ordinal)
        {
            // health
            ["ping"] = Ping,
            // scene / query
            ["get_scene_info"] = T5Handlers.GetSceneInfo,
            ["get_object_info"] = T5Handlers.GetObjectInfo,
            ["list_components"] = T5Handlers.ListComponents,
            ["get_bounding_box"] = T5Handlers.GetBoundingBox,
            ["get_parameters"] = T5Handlers.GetParameters,
            // sketch
            ["create_sketch"] = T5Handlers.CreateSketch,
            ["draw_rectangle"] = T5Handlers.DrawRectangle,
            ["draw_circle"] = T5Handlers.DrawCircle,
            ["draw_line"] = T5Handlers.DrawLine,
            ["draw_spline"] = T5Handlers.DrawSpline,
            ["draw_arc"] = T5Handlers.DrawArc,
            ["create_polygon"] = T5Handlers.CreatePolygon,
            ["add_constraint"] = T5Handlers.AddConstraint,
            ["auto_constrain"] = T5Handlers.AutoConstrain,
            ["add_dimension"] = T5Handlers.AddDimension,
            ["offset_curve"] = T5Handlers.OffsetCurve,
            ["trim_curve"] = T5Handlers.TrimCurve,
            ["extend_curve"] = T5Handlers.ExtendCurve,
            ["project_geometry"] = T5Handlers.ProjectGeometry,
            // features
            ["extrude"] = T6Handlers.Extrude,
            ["revolve"] = T6Handlers.Revolve,
            ["sweep"] = T6Handlers.Sweep,
            ["loft"] = T6Handlers.Loft,
            ["fillet"] = T6Handlers.Fillet,
            ["chamfer"] = T6Handlers.Chamfer,
            ["shell"] = T6Handlers.Shell,
            ["mirror"] = T6Handlers.Mirror,
            ["create_hole"] = T6Handlers.CreateHole,
            ["rectangular_pattern"] = T6Handlers.RectangularPattern,
            ["circular_pattern"] = T6Handlers.CircularPattern,
            ["create_thread"] = T6Handlers.CreateThread,
            ["draft_faces"] = T6Handlers.DraftFaces,
            ["split_body"] = T6Handlers.SplitBody,
            ["split_face"] = T6Handlers.SplitFace,
            ["offset_faces"] = T6Handlers.OffsetFaces,
            ["scale_body"] = T6Handlers.ScaleBody,
            ["suppress_feature"] = T6Handlers.SuppressFeature,
            ["unsuppress_feature"] = T6Handlers.UnsuppressFeature,
            // body operations
            ["move_body"] = T6Handlers.MoveBody,
            ["boolean_operation"] = T6Handlers.BooleanOperation,
            // direct primitives
            ["create_box"] = T6Handlers.CreateBox,
            ["create_cylinder"] = T6Handlers.CreateCylinder,
            ["create_sphere"] = T6Handlers.CreateSphere,
            ["create_torus"] = T6Handlers.CreateTorus,
            // surface operations
            ["patch_surface"] = T6Handlers.PatchSurface,
            ["stitch_surfaces"] = T6Handlers.StitchSurfaces,
            ["thicken_surface"] = T6Handlers.ThickenSurface,
            ["ruled_surface"] = T6Handlers.RuledSurface,
            ["trim_surface"] = T6Handlers.TrimSurface,
            // design type safety
            ["get_design_type"] = T5Handlers.GetDesignType,
            ["set_design_type"] = T5Handlers.SetDesignType,
            // scene-wide
            ["delete_all"] = T5Handlers.DeleteAll,
            ["undo"] = T5Handlers.Undo,
            // perception
            ["render_view"] = T5Handlers.RenderView,
            // T7: bodies / parametric
            ["rename_body"] = T7Handlers.RenameBody,
            ["create_box_parametric"] = T7Handlers.CreateBoxParametric,
            // T7: export
            ["export_stl"] = T7Handlers.ExportStl,
            ["export_step"] = T7Handlers.ExportStep,
            ["export_f3d"] = T7Handlers.ExportF3d,
            ["export"] = T7Handlers.Export,
            ["export_view_sheet"] = T7Handlers.ExportViewSheet,
            // T7: mesh
            ["import_mesh"] = T7Handlers.ImportMesh,
            ["compare_meshes"] = T7Handlers.CompareMeshes,
            // T7: construction geometry
            ["create_construction_plane"] = T7Handlers.CreateConstructionPlane,
            ["create_construction_axis"] = T7Handlers.CreateConstructionAxis,
            ["create_ucs"] = T7Handlers.CreateUcs,
            // T7: assembly
            ["create_component"] = T7Handlers.CreateComponent,
            ["add_joint"] = T7Handlers.AddJoint,
            ["create_as_built_joint"] = T7Handlers.CreateAsBuiltJoint,
            ["create_rigid_group"] = T7Handlers.CreateRigidGroup,
            // T7: inspection
            ["measure_distance"] = T7Handlers.MeasureDistance,
            ["measure_angle"] = T7Handlers.MeasureAngle,
            ["get_physical_properties"] = T7Handlers.GetPhysicalProperties,
            ["create_section_analysis"] = T7Handlers.CreateSectionAnalysis,
            ["check_interference"] = T7Handlers.CheckInterference,
            // T7: appearance
            ["set_appearance"] = T7Handlers.SetAppearance,
            ["set_color"] = T7Handlers.SetColor,
            // T7: parameters
            ["create_parameter"] = T7Handlers.CreateParameter,
            ["set_parameter"] = T7Handlers.SetParameter,
            ["delete_parameter"] = T7Handlers.DeleteParameter,
            // T7: sheet metal
            ["create_flange"] = T7Handlers.CreateFlange,
            ["create_bend"] = T7Handlers.CreateBend,
            ["flat_pattern"] = T7Handlers.FlatPattern,
            ["unfold"] = T7Handlers.Unfold,
            // T7: CAM
            ["cam_list_setups"] = T7Handlers.CamListSetups,
            ["cam_list_operations"] = T7Handlers.CamListOperations,
            ["cam_get_operation_info"] = T7Handlers.CamGetOperationInfo,
            ["cam_create_setup"] = T7Handlers.CamCreateSetup,
            ["cam_create_operation"] = T7Handlers.CamCreateOperation,
            ["cam_generate_toolpath"] = T7Handlers.CamGenerateToolpath,
            ["cam_post_process"] = T7Handlers.CamPostProcess,
        };

    private readonly IFusionContext? _ctx;

    /// <summary>Context-less constructor (ping / tests without a design).</summary>
    public CommandHandler() : this(null)
    {
    }

    /// <summary>Constructs a handler bound to the live design facade.</summary>
    public CommandHandler(IFusionContext? ctx)
    {
        _ctx = ctx;
    }

    public AddInResponse ExecuteCommand(JsonNode frame)
    {
        var cmdType = frame?["type"]?.GetValue<string>() ?? "?";

        if (!Commands.TryGetValue(cmdType, out var handler))
        {
            // Infrastructure-level failure (not an application error) —
            // keep the legacy error envelope so the client raises
            // (reference command_handler.py, handler is None branch).
            return AddInResponse.Error($"Unknown command: {cmdType}", ErrorKind.UnknownCommand);
        }

        var paramsJson = frame?["params"] as JsonObject ?? new JsonObject();

        // Only "ping" may run without a live design facade.
        if (cmdType != "ping" && _ctx is null)
        {
            return AddInResponse.Error("No active design context", ErrorKind.NoActiveDesign);
        }

        // Reference mutation bookkeeping: before/after snapshots so agents
        // can sanity-check without a render.
        var isMutation = ToolEffects.IsDocumentMutation(cmdType);
        var hasDeltas = MutationCommands.Set.Contains(cmdType);
        var snapBefore = hasDeltas ? _ctx!.TakeSnapshot() : null;

        // P3 (ADR-0011): optional expected_revision guard — reject a
        // mismatching caller context BEFORE touching the model.
        if (isMutation && ReadExpectedRevision(paramsJson) is { } expectedRevision)
        {
            // Best-effort document name: the guard is only meaningful when
            // a design is active; without one the handler below fails on
            // its own (NO_ACTIVE_DESIGN), so fall back to the counter's
            // all-empty key rather than throwing out of the dispatch path.
            string documentIdentity;
            try
            {
                documentIdentity = _ctx!.Design.DocumentIdentity;
            }
            catch
            {
                documentIdentity = string.Empty;
            }

            var currentRevision = ModelRevision.Current(documentIdentity);
            if (expectedRevision != currentRevision)
            {
                var failure = CommandResult.Failure(
                    ErrorKind.RevisionConflict,
                    $"expected_revision {expectedRevision} does not match the current model revision {currentRevision}",
                    RevisionConflictHints);
                // A rejected mutation moves nothing — the current revision
                // stands (TOOL_CONTRACTS §1.2).
                failure.Fields["revision_after"] = currentRevision;
                // P4: a mutation rejection states partial_effects explicitly;
                // the pre-check never touched the model.
                failure.Fields["partial_effects"] = false;
                return AddInResponse.Success(failure);
            }
        }

        try
        {
            var result = handler(_ctx!, paramsJson);

            SceneSnapshot? snapAfter = null;
            if (hasDeltas && snapBefore is not null)
            {
                snapAfter = _ctx!.TakeSnapshot();
                if (snapAfter is not null)
                {
                    result["deltas"] = MutationCommands.Deltas(snapBefore, snapAfter);
                }
            }

            if (isMutation)
            {
                // P1 (ADR-0009): the verification block — the same
                // insertion point as the auto-deltas above. Optional v2
                // field; old clients never read it.
                result["verification"] = VerificationBuilder.Build(_ctx, cmdType, paramsJson, result, snapBefore, snapAfter);
            }

            if (isMutation)
            {
                // One applied mutation = one revision step (undo / regen
                // included — both are in the mutation set).
                result["revision_after"] = ModelRevision.Increment(_ctx!.Design.DocumentIdentity);
            }

            // Reference result.setdefault("ok", True) marker.
            return AddInResponse.Success(CommandResult.Success(result));
        }
        catch (Exception ex)
        {
            // Application error — reference returns a SUCCESS frame carrying
            // an ok:false classified result (only unknown commands raise the
            // status:error envelope above).
            var (kind, hints) = ErrorClassifier.Classify(ex);
            var message = string.IsNullOrEmpty(ex.Message) ? ex.GetType().Name : ex.Message;
            var failure = CommandResult.Failure(kind, message, hints, ex.ToString());
            if (isMutation)
            {
                // A failed mutation does not roll back — report the current
                // revision (TOOL_CONTRACTS §1.2: revision_after on failure
                // carries the current revision). The document name is
                // best-effort: when no design is active the counter simply
                // reads 0 (the empty-document key).
                long revisionAfter;
                try
                {
                    revisionAfter = ModelRevision.Current(_ctx!.Design.DocumentIdentity);
                }
                catch
                {
                    revisionAfter = 0;
                }

                failure.Fields["revision_after"] = revisionAfter;

                // A changed snapshot proves partial effects. An unchanged or
                // unreadable body snapshot cannot prove that sketches,
                // parameters, appearances or feature history stayed unchanged.
                if (snapBefore is { } before)
                {
                    SceneSnapshot? after = null;
                    try
                    {
                        after = _ctx!.TakeSnapshot();
                    }
                    catch
                    {
                        after = null; // snapshot unreadable — nothing observable
                    }

                    if (after is not null && MutationCommands.HasPartialEffects(before, after))
                    {
                        failure.Fields["partial_effects"] = true;
                        failure.Fields["deltas"] = MutationCommands.Deltas(before, after);
                    }
                    else
                    {
                        failure.Fields["outcome_unknown"] = true;
                    }
                }
                else
                {
                    // This command has no body snapshot contract. Its failed
                    // outcome cannot prove that no document change occurred.
                    failure.Fields["outcome_unknown"] = true;
                }
            }
            return AddInResponse.Success(failure);
        }
    }

    /// <summary>Revision-conflict hints (TOOL_CONTRACTS §2/§3: reacquire_context).</summary>
    private static readonly string[] RevisionConflictHints =
    {
        "Re-read get_scene_info (model_revision) and re-issue the operation with the current revision.",
        "After re-reading, names that no longer resolve report STALE_REFERENCE, not NOT_FOUND.",
    };

    /// <summary>Reads the optional integer <c>expected_revision</c> param (null when absent or non-numeric).</summary>
    private static long? ReadExpectedRevision(JsonObject p)
    {
        if (p["expected_revision"] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var l))
        {
            return l;
        }

        return value.TryGetValue<double>(out var d) ? (long)d : null;
    }

    /// <summary>Health smoke command: instant, no adsk API. Also fast-pathed by EventBridge.Submit.</summary>
    private static JsonObject Ping(IFusionContext ctx, JsonObject p)
        => new() { ["status"] = "pong" };
}
