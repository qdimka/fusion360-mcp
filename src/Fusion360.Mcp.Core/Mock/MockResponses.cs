using System.Globalization;
using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Catalog;
using Fusion360.Mcp.Core.Protocol;
using Fusion360.Mcp.Core.Units;

namespace Fusion360.Mcp.Core.Mock;

/// <summary>
/// Mock responses for every Fusion 360 MCP command.
///
/// Used when the server is started in mock mode so the full
/// tool→response pipeline can be tested without a running Fusion
/// instance. Every response includes "mode": "mock" so callers know it's
/// simulated. Port of reference src/fusion360_mcp/mock.py.
///
/// The envelope matches the real add-in: success results carry
/// <c>ok: true</c>, mutation results carry a <c>deltas</c> sub-object,
/// and an <c>__mock_error__</c> sentinel in params forces a classified
/// error response for tests.
/// </summary>
public static class MockResponses
{
    /// <summary>Sentinel key in params that forces a classified mock error.</summary>
    public const string MockErrorSentinel = "__mock_error__";

    /// <summary>
    /// P3 (ADR-0011): the mock's document revision counter — starts at 0
    /// and increments once per successful mock mutation, mirroring the
    /// add-in's ModelRevision. Exposed in get_scene_info /
    /// get_object_info as <c>model_revision</c> and on mutation results
    /// as <c>revision_after</c>; a mismatching optional
    /// <c>expected_revision</c> input is rejected with REVISION_CONFLICT
    /// before dispatch, exactly like the add-in pre-check.
    /// </summary>
    private static long _mockRevision;

    /// <summary>The mock's current model revision (read-only view).</summary>
    public static long CurrentRevision => Interlocked.Read(ref _mockRevision);

    /// <summary>Resets the mock revision state (tests).</summary>
    public static void ResetRevisionForTests() => Interlocked.Exchange(ref _mockRevision, 0);

    /// <summary>Mutation commands that get a synthetic deltas payload in mock mode.</summary>
    public static readonly HashSet<string> MutationCommands = new()
    {
        "extrude", "revolve", "sweep", "loft", "fillet", "chamfer", "shell", "mirror",
        "create_hole", "rectangular_pattern", "circular_pattern", "create_thread",
        "draft_faces", "split_body", "split_face", "offset_faces", "scale_body",
        "suppress_feature", "unsuppress_feature", "move_body", "boolean_operation",
        "create_box", "create_cylinder", "create_sphere", "create_torus",
        "thicken_surface", "patch_surface", "stitch_surfaces", "ruled_surface",
        "trim_surface", "create_flange", "create_bend", "flat_pattern", "unfold",
        "delete_all", "undo", "set_parameter", "auto_constrain",
        "create_ucs", "set_color",
    };

    /// <summary>The synthetic deltas payload attached to mutation results.</summary>
    private static readonly JsonObject MockDeltas = new()
    {
        ["body_count_before"] = 0,
        ["body_count_after"] = 1,
        ["body_count_delta"] = 1,
        ["mass_g_before"] = 0.0,
        ["mass_g_after"] = 10.0,
        ["mass_g_delta"] = 10.0,
        ["bbox_before"] = null,
        ["bbox_after"] = new JsonObject
        {
            ["min"] = new JsonArray(0.0, 0.0, 0.0),
            ["max"] = new JsonArray(1.0, 1.0, 1.0),
        },
    };

    /// <summary>1x1 transparent PNG, enough to exercise the image-content code path.</summary>
    private const string MockPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0l" +
        "EQVR42mNkAAIAAAoAAv/lxKUAAAAASUVORK5CYII=";

    /// <summary>
    /// Return a plausible mock response for *commandType*.
    /// If *parameters* contains the <c>__mock_error__</c> sentinel (a
    /// string message), the response simulates an add-in-side failure:
    /// ok: false plus a classified error_kind and contextual hints.
    /// </summary>
    public static JsonObject MockCommand(string commandType, JsonObject? parameters = null)
    {
        var p = parameters ?? new JsonObject();

        var isMutation = ToolEffects.IsDocumentMutation(commandType);
        var hasDeltas = MutationCommands.Contains(commandType);
        var currentRevision = CurrentRevision;

        var forced = ForcedError(p);
        if (forced is not null)
        {
            var (kind, hints) = ErrorClassifier.Classify(forced);
            var result = new JsonObject
            {
                ["ok"] = false,
                ["error_kind"] = kind.ToWireString(),
                ["error_message"] = forced,
                ["hints"] = HintsJson(hints),
                ["traceback"] = $"MockError: {forced}",
                ["mode"] = "mock",
            };
            if (ToolEffects.OperationCommands.Contains(commandType))
            {
                // A failed mutation does not move the model — report the
                // current revision (TOOL_CONTRACTS §1.2) and state
                // partial_effects: false explicitly (P4; the mock never
                // mutates the fake scene on failure, so the model is clean).
                if (isMutation)
                {
                    result["revision_after"] = currentRevision;
                }
                result["partial_effects"] = false;
            }
            return result;
        }

        // P3 pre-check (mirrors the add-in's CommandHandler): a stale
        // expected_revision rejects the command BEFORE dispatch, so the
        // model is never touched.
        if (isMutation && ReadExpectedRevision(p) is { } expected && expected != currentRevision)
        {
            return new JsonObject
            {
                ["ok"] = false,
                ["error_kind"] = ErrorKind.RevisionConflict.ToWireString(),
                ["error_message"] = $"expected_revision {expected} does not match the current model revision {currentRevision}",
                ["hints"] = RevisionConflictHints,
                ["revision_after"] = currentRevision,
                // P4: the pre-check never touched the model.
                ["partial_effects"] = false,
                ["mode"] = "mock",
            };
        }

        var dispatched = Dispatch(commandType, p);
        if (!dispatched.ContainsKey("ok"))
        {
            dispatched["ok"] = true;
        }

        if (hasDeltas && !dispatched.ContainsKey("deltas"))
        {
            dispatched["deltas"] = MockDeltas.DeepClone() as JsonObject;
        }

        // P1 (ADR-0009): the mock carries the same optional verification
        // block on every mutation — uniform call_returned with a named
        // reason, because a mock has no live scene to verify against.
        if (isMutation && !dispatched.ContainsKey("verification"))
        {
            dispatched["verification"] = MockVerification();
        }

        if (isMutation)
        {
            dispatched["revision_after"] = Interlocked.Increment(ref _mockRevision);
        }

        dispatched["mode"] = "mock";
        return dispatched;
    }

    /// <summary>
    /// P1 (ADR-0009): the mock's uniform <c>verification</c> block. A mock
    /// has no live scene, so every mutation reports the lowest rung with a
    /// named reason (the deltas are synthetic, so nothing is "verified").
    /// </summary>
    private static JsonObject MockVerification()
        => Verification.ToJson(
            VerificationLevel.CallReturned,
            Array.Empty<VerificationCheck>(),
            new[] { new UnverifiedAspect("scene_state", "mock mode — synthetic deltas, no live Fusion scene to verify against") });

    /// <summary>Revision-conflict hints (TOOL_CONTRACTS §2/§3: reacquire_context).</summary>
    private static readonly JsonArray RevisionConflictHints = new()
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

    // ── helpers ─────────────────────────────────────────────────────

    private static JsonArray HintsJson(string[] hints)
    {
        var array = new JsonArray();
        foreach (var hint in hints)
        {
            array.Add(hint);
        }

        return array;
    }

    /// <summary>Reads the forced-error sentinel, mirroring Python's str() semantics for scalars.</summary>
    private static string? ForcedError(JsonObject p)
    {
        if (!p.TryGetPropertyValue(MockErrorSentinel, out var node) || node is null)
        {
            return null;
        }

        if (node is not JsonValue value)
        {
            return node.ToJsonString();
        }

        var underlying = value.GetValue<object?>();
        return underlying switch
        {
            null => null, // JSON null — Python sees None, sentinel not active
            string s => s,
            bool b => b ? "True" : "False",
            double d => d.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            _ => node.ToJsonString(),
        };
    }

    private static JsonNode? Get(JsonObject? p, string key)
        => p is not null && p.TryGetPropertyValue(key, out var value) ? value : null;

    private static JsonNode? GetOr(JsonObject? p, string key, JsonNode? fallback)
    {
        // Deep-clone so the node detaches from its input parent — Python
        // dict values are shared freely, but STJ nodes belong to one
        // parent; re-parenting throws.
        return Get(p, key)?.DeepClone() ?? fallback;
    }

    /// <summary>Python's p.get(key) or default for strings (blank also falls back).</summary>
    private static string StrOrEmpty(JsonObject? p, string key)
        => Get(p, key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    /// <summary>Python's p.get(key) or default (plain, no blank fallback).</summary>
    private static string? Str(JsonObject? p, string key)
        => Get(p, key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Python's p.get(key) or default — blank values fall back.</summary>
    private static string StrOr(JsonObject? p, string key, string fallback)
    {
        var value = Str(p, key);
        return value is null || value.Length == 0 ? fallback : value;
    }

    /// <summary>Python's p.get(key, default) for integers.</summary>
    private static int Int(JsonObject? p, string key, int fallback)
    {
        if (Get(p, key) is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return (int)doubleValue;
        }

        return fallback;
    }

    /// <summary>Python's p.get(key, []) — a list parameter with an empty fallback.</summary>
    private static JsonArray ListOr(JsonObject? p, string key)
    {
        if (Get(p, key) is JsonArray array && array.Count > 0)
        {
            // Clone so the list detaches from its input parent.
            return array.DeepClone() as JsonArray ?? new JsonArray();
        }

        return new JsonArray();
    }

    /// <summary>Python's p.get(key, default) for a list, falling back when absent or empty (string fallback).</summary>
    private static JsonArray ListOrStrings(JsonObject? p, string key, params string[] fallback)
    {
        if (Get(p, key) is JsonArray array && array.Count > 0)
        {
            // Clone so the list detaches from its input parent.
            return array.DeepClone() as JsonArray ?? new JsonArray();
        }

        var result = new JsonArray();
        foreach (var item in fallback)
        {
            result.Add(JsonValue.Create(item));
        }

        return result;
    }

    /// <summary>Python's p.get(key, default) for a list, falling back when absent or empty (numeric fallback).</summary>
    private static JsonArray ListOrNumbers(JsonObject? p, string key, params double[] fallback)
    {
        if (Get(p, key) is JsonArray array && array.Count > 0)
        {
            // Clone so the list detaches from its input parent.
            return array.DeepClone() as JsonArray ?? new JsonArray();
        }

        var result = new JsonArray();
        foreach (var item in fallback)
        {
            result.Add(JsonValue.Create(item));
        }

        return result;
    }

    /// <summary>Length of a list parameter (Python's len(p.get(key, []))).</summary>
    private static int CountOf(JsonObject? p, string key)
        => Get(p, key) is JsonArray array ? array.Count : 0;

    /// <summary>Raw JSON scalar text (Python's str(value) for numbers/strings).</summary>
    private static string JsonText(JsonNode? node, string fallback)
    {
        if (node is not JsonValue value)
        {
            return fallback;
        }

        return value.GetValue<object?>() switch
        {
            null => fallback,
            string s => s,
            double d => d.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "True" : "False",
            _ => node.ToJsonString(),
        };
    }

    // ── individual mock handlers (port of mock.py) ──────────────────

    private static JsonObject Dispatch(string commandType, JsonObject p)
    {
        return commandType switch
        {
            "ping" => Ping(),
            "get_scene_info" => GetSceneInfo(),
            "get_object_info" => GetObjectInfo(p),
            "get_bounding_box" => GetBoundingBox(p),
            "create_sketch" => CreateSketch(p),
            "draw_rectangle" => DrawRectangle(p),
            "draw_circle" => DrawCircle(p),
            "draw_line" => DrawLine(p),
            "extrude" => Extrude(p),
            "revolve" => Revolve(p),
            "fillet" => Fillet(p),
            "chamfer" => Chamfer(p),
            "shell" => Shell(p),
            "mirror" => Mirror(p),
            "rename_body" => RenameBody(p),
            "move_body" => MoveBody(p),
            "export_stl" => ExportStl(p),
            "boolean_operation" => BooleanOperation(p),
            "delete_all" => DeleteAll(),
            "undo" => Undo(),
            "get_design_type" => GetDesignType(),
            "set_design_type" => SetDesignType(p),
            "sweep" => Sweep(p),
            "loft" => Loft(p),
            "create_polygon" => CreatePolygon(p),
            "draw_arc" => DrawArc(p),
            "create_hole" => CreateHole(p),
            "rectangular_pattern" => RectangularPattern(p),
            "circular_pattern" => CircularPattern(p),
            "create_component" => CreateComponent(p),
            "add_joint" => AddJoint(p),
            "list_components" => ListComponents(),
            "export_step" => ExportStep(p),
            "export_f3d" => ExportF3d(p),
            "export_view_sheet" => ExportViewSheet(p),
            "export" => Export(p),
            "import_mesh" => ImportMesh(p),
            "create_box_parametric" => CreateBoxParametric(p),
            "get_parameters" => GetParameters(),
            "create_parameter" => CreateParameter(p),
            "set_parameter" => SetParameter(p),
            "delete_parameter" => DeleteParameter(p),
            "add_constraint" => AddConstraint(p),
            "auto_constrain" => AutoConstrain(p),
            "add_dimension" => AddDimension(p),
            "create_construction_plane" => CreateConstructionPlane(p),
            "create_construction_axis" => CreateConstructionAxis(p),
            "draw_spline" => DrawSpline(p),
            "offset_curve" => OffsetCurve(p),
            "trim_curve" => TrimCurve(p),
            "extend_curve" => ExtendCurve(p),
            "create_thread" => CreateThread(p),
            "draft_faces" => DraftFaces(p),
            "split_body" => SplitBody(p),
            "split_face" => SplitFace(p),
            "offset_faces" => OffsetFaces(p),
            "scale_body" => ScaleBody(p),
            "create_box" => CreateBox(p),
            "create_cylinder" => CreateCylinder(p),
            "create_sphere" => CreateSphere(p),
            "create_torus" => CreateTorus(p),
            "create_as_built_joint" => CreateAsBuiltJoint(p),
            "create_rigid_group" => CreateRigidGroup(p),
            "measure_distance" => MeasureDistance(p),
            "measure_angle" => MeasureAngle(p),
            "get_physical_properties" => GetPhysicalProperties(p),
            "create_section_analysis" => CreateSectionAnalysis(p),
            "check_interference" => CheckInterference(p),
            "compare_meshes" => CompareMeshes(p),
            "create_ucs" => CreateUcs(p),
            "set_color" => SetColor(p),
            "set_appearance" => SetAppearance(p),
            "project_geometry" => ProjectGeometry(p),
            "suppress_feature" => SuppressFeature(p),
            "unsuppress_feature" => UnsuppressFeature(p),
            "patch_surface" => PatchSurface(p),
            "stitch_surfaces" => StitchSurfaces(p),
            "thicken_surface" => ThickenSurface(p),
            "ruled_surface" => RuledSurface(p),
            "trim_surface" => TrimSurface(p),
            "create_flange" => CreateFlange(p),
            "create_bend" => CreateBend(p),
            "flat_pattern" => FlatPattern(p),
            "unfold" => Unfold(p),
            "cam_create_setup" => CamCreateSetup(p),
            "cam_create_operation" => CamCreateOperation(p),
            "cam_generate_toolpath" => CamGenerateToolpath(p),
            "cam_post_process" => CamPostProcess(p),
            "cam_list_setups" => CamListSetups(),
            "cam_list_operations" => CamListOperations(p),
            "cam_get_operation_info" => CamGetOperationInfo(p),
            "render_view" => RenderView(p),
            _ => DefaultMock(p),
        };
    }

    private static JsonObject Ping()
        => new() { ["status"] = "pong" };

    private static JsonObject GetSceneInfo()
        => new()
        {
            ["design_name"] = "MockDesign",
            ["bodies"] = new JsonArray("Body1"),
            ["sketches"] = new JsonArray("Sketch1"),
            ["features"] = new JsonArray("Extrude1"),
            ["components"] = new JsonArray("RootComponent"),
            // P3 (ADR-0011): the mock document revision the caller should
            // echo back as expected_revision on the next mock mutation.
            ["model_revision"] = CurrentRevision,
        };

    private static JsonObject GetObjectInfo(JsonObject p)
    {
        var name = StrOr(p, "name", "Unknown");
        return new JsonObject
        {
            ["name"] = name,
            ["type"] = "BRepBody",
            ["faces"] = 6,
            ["edges"] = 12,
            ["vertices"] = 8,
            ["bounding_box"] = new JsonObject
            {
                ["min"] = new JsonArray(0.0, 0.0, 0.0),
                ["max"] = new JsonArray(1.0, 1.0, 1.0),
            },
            // P3 (ADR-0011): same document-revision field as get_scene_info.
            ["model_revision"] = CurrentRevision,
        };
    }

    private static JsonObject GetBoundingBox(JsonObject p)
    {
        var name = StrOr(p, "name", "Unknown");
        return new JsonObject
        {
            ["found"] = true,
            ["type"] = "body",
            ["name"] = name,
            ["min"] = new JsonArray(0.0, 0.0, 0.0),
            ["max"] = new JsonArray(50.0, 30.0, 15.0),
            ["size"] = new JsonArray(50.0, 30.0, 15.0),
            ["center"] = new JsonArray(25.0, 15.0, 7.5),
        };
    }

    private static JsonObject CreateSketch(JsonObject p)
    {
        var plane = StrOr(p, "plane", "xy");
        return new JsonObject
        {
            ["sketch_name"] = $"Sketch_mock_{plane}",
            ["plane"] = plane,
        };
    }

    private static JsonObject DrawRectangle(JsonObject p)
        => new()
        {
            ["sketch_name"] = "Sketch_mock_xy",
            ["width"] = GetOr(p, "width", JsonValue.Create(1)),
            ["height"] = GetOr(p, "height", JsonValue.Create(1)),
        };

    private static JsonObject DrawCircle(JsonObject p)
        => new()
        {
            ["sketch_name"] = "Sketch_mock_xy",
            ["radius"] = GetOr(p, "radius", JsonValue.Create(1)),
        };

    private static JsonObject DrawLine(JsonObject p)
        => new()
        {
            ["sketch_name"] = "Sketch_mock_xy",
            ["start"] = new JsonArray
            {
                GetOr(p, "start_x", JsonValue.Create(0)),
                GetOr(p, "start_y", JsonValue.Create(0)),
            },
            ["end"] = new JsonArray
            {
                GetOr(p, "end_x", JsonValue.Create(1)),
                GetOr(p, "end_y", JsonValue.Create(1)),
            },
        };

    private static JsonObject Extrude(JsonObject p)
        => new()
        {
            ["body_name"] = "Body_mock",
            ["height"] = GetOr(p, "height", JsonValue.Create(1)),
            ["operation"] = GetOr(p, "operation", JsonValue.Create("new_body")),
        };

    private static JsonObject Revolve(JsonObject p)
        => new()
        {
            ["body_name"] = "Body_mock_revolve",
            ["angle"] = GetOr(p, "angle", JsonValue.Create(360)),
            ["operation"] = GetOr(p, "operation", JsonValue.Create("new_body")),
        };

    private static JsonObject Fillet(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["radius"] = GetOr(p, "radius", JsonValue.Create(0.1)),
        };

    private static JsonObject Chamfer(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["distance"] = GetOr(p, "distance", JsonValue.Create(0.1)),
        };

    private static JsonObject Shell(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["thickness"] = GetOr(p, "thickness", JsonValue.Create(0.1)),
        };

    private static JsonObject Mirror(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["mirror_plane"] = GetOr(p, "mirror_plane", JsonValue.Create("yz")),
            ["new_body_name"] = "Body1_mirrored",
        };

    private static JsonObject RenameBody(JsonObject p)
        => new()
        {
            ["renamed"] = true,
            ["old_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["new_name"] = GetOr(p, "new_name", JsonValue.Create("RenamedBody")),
        };

    private static JsonObject MoveBody(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["translation"] = new JsonArray
            {
                GetOr(p, "x", JsonValue.Create(0)),
                GetOr(p, "y", JsonValue.Create(0)),
                GetOr(p, "z", JsonValue.Create(0)),
            },
        };

    private static JsonObject ExportStl(JsonObject p)
    {
        var name = StrOr(p, "body_name", "Body1");
        var path = StrOr(p, "file_path", $"~/Desktop/{name}.stl");
        return new JsonObject
        {
            ["body_name"] = name,
            ["file_path"] = path,
        };
    }

    private static JsonObject BooleanOperation(JsonObject p)
    {
        var target = GetOr(p, "target_body", JsonValue.Create("Body1"));
        return new JsonObject
        {
            ["target_body"] = target,
            ["tool_body"] = GetOr(p, "tool_body", JsonValue.Create("Body2")),
            ["operation"] = GetOr(p, "operation", JsonValue.Create("join")),
            // Second use of the same node: detach a copy (STJ nodes have one parent).
            ["result_body"] = target!.DeepClone(),
        };
    }

    private static JsonObject DeleteAll()
        => new() { ["deleted"] = true };

    private static JsonObject Undo()
        => new()
        {
            ["undone"] = true,
            ["design_type"] = 1,
        };

    private static JsonObject GetDesignType()
        => new()
        {
            ["design_type"] = "parametric",
            ["design_type_id"] = 1,
        };

    private static JsonObject SetDesignType(JsonObject p)
    {
        var designType = GetOr(p, "design_type", JsonValue.Create("parametric"));
        return new JsonObject
        {
            ["changed"] = true,
            ["design_type"] = designType,
        };
    }

    private static JsonObject Sweep(JsonObject p)
        => new()
        {
            ["body_name"] = "Body_mock_sweep",
            ["path_sketch_name"] = GetOr(p, "path_sketch_name", JsonValue.Create("PathSketch")),
            ["operation"] = GetOr(p, "operation", JsonValue.Create("new_body")),
        };

    private static JsonObject Loft(JsonObject p)
        => new()
        {
            ["body_name"] = "Body_mock_loft",
            ["profile_sketch_names"] = ListOr(p, "profile_sketch_names"),
            ["operation"] = GetOr(p, "operation", JsonValue.Create("new_body")),
        };

    private static JsonObject CreatePolygon(JsonObject p)
        => new()
        {
            ["sketch_name"] = "Sketch_mock_xy",
            ["sides"] = GetOr(p, "sides", JsonValue.Create(6)),
            ["radius"] = GetOr(p, "radius", JsonValue.Create(1)),
        };

    private static JsonObject DrawArc(JsonObject p)
        => new()
        {
            ["sketch_name"] = "Sketch_mock_xy",
            ["center"] = new JsonArray
            {
                GetOr(p, "center_x", JsonValue.Create(0)),
                GetOr(p, "center_y", JsonValue.Create(0)),
            },
            ["sweep_angle"] = GetOr(p, "sweep_angle", JsonValue.Create(90)),
        };

    private static JsonObject CreateHole(JsonObject p)
    {
        var body = StrOr(p, "body_name", $"Body{Int(p, "body_index", 0) + 1}");
        var diameter = GetOr(p, "diameter", JsonValue.Create(0.5));
        return new JsonObject
        {
            ["feature_name"] = "Hole_mock",
            ["body_name"] = body,
            ["diameter"] = diameter,
            ["depth"] = GetOr(p, "depth", JsonValue.Create(1)),
            // Second use of the same node: detach a copy (STJ nodes have one parent).
            ["actual_diameter"] = diameter!.DeepClone(),
            ["cut_bodies"] = new JsonArray(body),
            ["resolved_center"] = new JsonArray
            {
                GetOr(p, "center_x", JsonValue.Create(0)),
                GetOr(p, "center_y", JsonValue.Create(0)),
                JsonValue.Create(0.0),
            },
        };
    }

    private static JsonObject RectangularPattern(JsonObject p)
    {
        var xCount = Int(p, "x_count", 1);
        var yCount = Int(p, "y_count", 1);
        return new JsonObject
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["x_count"] = GetOr(p, "x_count", JsonValue.Create(1)),
            ["y_count"] = GetOr(p, "y_count", JsonValue.Create(1)),
            ["created_bodies"] = xCount * yCount,
        };
    }

    private static JsonObject CircularPattern(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["count"] = GetOr(p, "count", JsonValue.Create(3)),
            ["axis"] = GetOr(p, "axis", JsonValue.Create("z")),
            ["total_angle"] = GetOr(p, "total_angle", JsonValue.Create(360)),
        };

    private static JsonObject CreateComponent(JsonObject p)
        => new()
        {
            ["component_name"] = GetOr(p, "name", JsonValue.Create("Component1")),
            ["parent"] = GetOr(p, "parent_name", JsonValue.Create("RootComponent")),
        };

    private static JsonObject AddJoint(JsonObject p)
        => new()
        {
            ["component_one"] = GetOr(p, "component_one", JsonValue.Create("Comp1")),
            ["component_two"] = GetOr(p, "component_two", JsonValue.Create("Comp2")),
            ["joint_type"] = GetOr(p, "joint_type", JsonValue.Create("rigid")),
            ["joint_name"] = "Joint_mock",
        };

    private static JsonObject ListComponents()
        => new()
        {
            ["components"] = new JsonArray
            {
                new JsonObject { ["name"] = "RootComponent", ["bodies"] = new JsonArray("Body1") },
                new JsonObject { ["name"] = "SubComponent1", ["bodies"] = new JsonArray() },
            },
        };

    private static JsonObject ExportStep(JsonObject p)
    {
        var name = StrOr(p, "body_name", "Body1");
        var path = StrOr(p, "file_path", $"~/Desktop/{name}.step");
        return new JsonObject
        {
            ["body_name"] = name,
            ["file_path"] = path,
        };
    }

    private static JsonObject ExportF3d(JsonObject p)
        => new() { ["file_path"] = GetOr(p, "file_path", JsonValue.Create("~/Desktop/MockDesign.f3d")) };

    private static JsonObject ExportViewSheet(JsonObject p)
    {
        var views = ListOrStrings(p, "views", "iso", "front", "top", "right");
        var size = ListOrNumbers(p, "image_size", 1200, 900);
        var outputDir = StrOr(p, "output_dir", "~/Desktop/MockDesign_views_mock");
        var title = StrOr(p, "title", "MockDesign");

        var viewEntries = new JsonArray();
        foreach (var view in views)
        {
            var viewName = view is JsonValue v && v.TryGetValue<string>(out var s) ? s : view?.ToJsonString() ?? string.Empty;
            viewEntries.Add(new JsonObject
            {
                ["view"] = viewName,
                ["path"] = $"{outputDir}/{viewName}.png",
            });
        }

        return new JsonObject
        {
            ["html_path"] = $"{outputDir}/view_sheet.html",
            ["output_dir"] = outputDir,
            ["title"] = title,
            ["image_size"] = new JsonArray(size.Select(x => x!.DeepClone()).ToArray()),
            ["views"] = viewEntries,
        };
    }

    private static JsonObject Export(JsonObject p)
    {
        var format = StrOrEmpty(p, "format").ToLowerInvariant();
        var path = StrOrEmpty(p, "file_path");
        if (string.IsNullOrEmpty(format) && path.Length > 0)
        {
            var dot = path.LastIndexOf('.');
            format = dot >= 0 ? path.Substring(dot + 1).ToLowerInvariant() : string.Empty;
        }

        var name = StrOr(p, "body_name", "Body1");
        return new JsonObject
        {
            ["exported"] = true,
            ["format"] = string.IsNullOrEmpty(format) ? "f3d" : format,
            ["body"] = name,
            ["file_path"] = string.IsNullOrEmpty(path)
                ? $"~/Desktop/{name}.{(string.IsNullOrEmpty(format) ? "f3d" : format)}"
                : path,
        };
    }

    private static JsonObject ImportMesh(JsonObject p)
    {
        var path = StrOr(p, "file_path", "/tmp/mesh.stl");
        var units = StrOr(p, "units", "mm");
        var component = StrOr(p, "component_name", "RootComponent");
        return new JsonObject
        {
            ["imported"] = true,
            ["file_path"] = path,
            ["mesh_name"] = "MeshBody_mock",
            ["component"] = component,
            ["units"] = units,
            ["bounding_box"] = new JsonObject
            {
                ["min"] = new JsonArray(0.0, 0.0, 0.0),
                ["max"] = new JsonArray(10.0, 5.0, 2.0),
                ["size"] = new JsonArray(10.0, 5.0, 2.0),
            },
        };
    }

    private static JsonObject CreateBoxParametric(JsonObject p)
        => new()
        {
            ["created"] = true,
            ["body_name"] = StrOr(p, "body_name", "BoxParametric_mock"),
            ["feature_name"] = "Extrude_mock",
            ["sketch_name"] = "Sketch_mock",
            ["length"] = GetOr(p, "length", JsonValue.Create(1)),
            ["width"] = GetOr(p, "width", JsonValue.Create(1)),
            ["height"] = GetOr(p, "height", JsonValue.Create(1)),
            ["origin"] = new JsonArray
            {
                GetOr(p, "origin_x", JsonValue.Create(0)),
                GetOr(p, "origin_y", JsonValue.Create(0)),
                GetOr(p, "origin_z", JsonValue.Create(0)),
            },
            ["plane"] = GetOr(p, "plane", JsonValue.Create("xy")),
            ["component"] = StrOr(p, "component_name", "RootComponent"),
        };

    private static JsonObject GetParameters()
        => new()
        {
            ["parameters"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "width",
                    ["value"] = 10.0,
                    ["unit"] = "mm",
                    ["comment"] = "",
                },
                new JsonObject
                {
                    ["name"] = "height",
                    ["value"] = 5.0,
                    ["unit"] = "mm",
                    ["comment"] = "",
                },
            },
        };

    private static JsonObject CreateParameter(JsonObject p)
    {
        var valueNode = GetOr(p, "value", JsonValue.Create(0));
        // The add-in trims the unit and treats blank as unitless; mirror
        // both so mock and real modes agree. "unit" is required by the
        // schema, so the default only covers a caller that omitted it
        // entirely.
        var unit = (Get(p, "unit") is null ? "mm" : StrOr(p, "unit", "mm")).Trim();
        var expression = unit.Length > 0
            ? $"{JsonText(valueNode, "0")} {unit}"
            : JsonText(valueNode, "0");
        return new JsonObject
        {
            ["created"] = true,
            ["name"] = GetOr(p, "name", JsonValue.Create("param1")),
            ["value"] = valueNode,
            ["unit"] = unit,
            ["expression"] = expression,
            ["comment"] = GetOr(p, "comment", JsonValue.Create("")),
        };
    }

    private static JsonObject SetParameter(JsonObject p)
        => new()
        {
            ["updated"] = true,
            ["name"] = GetOr(p, "name", JsonValue.Create("param1")),
            ["value"] = GetOr(p, "value", JsonValue.Create(0)),
            ["unit"] = null,
            ["expression"] = null,
        };

    private static JsonObject DeleteParameter(JsonObject p)
        => new()
        {
            ["name"] = GetOr(p, "name", JsonValue.Create("param1")),
            ["deleted"] = true,
        };

    private static JsonObject AddConstraint(JsonObject p)
        => new()
        {
            ["constraint_type"] = GetOr(p, "constraint_type", JsonValue.Create("coincident")),
            ["entity_one"] = GetOr(p, "entity_one", JsonValue.Create(0)),
            ["entity_two"] = GetOr(p, "entity_two", JsonValue.Create(1)),
            ["sketch_name"] = GetOr(p, "sketch_name", JsonValue.Create("Sketch1")),
        };

    private static JsonObject AutoConstrain(JsonObject p)
        => new()
        {
            ["sketch"] = GetOr(p, "sketch_name", JsonValue.Create("Sketch1")),
            ["is_fully_constrained"] = true,
            ["constraints_added"] = 4,
            ["dimensions_added"] = 2,
            ["entities_moved"] = 0,
            ["result_option"] = GetOr(p, "result_option", JsonValue.Create(1)),
        };

    private static JsonObject AddDimension(JsonObject p)
        => new()
        {
            ["dimension_type"] = GetOr(p, "dimension_type", JsonValue.Create("distance")),
            ["value"] = GetOr(p, "value", JsonValue.Create(1.0)),
            ["entity_one"] = GetOr(p, "entity_one", JsonValue.Create(0)),
            ["sketch_name"] = GetOr(p, "sketch_name", JsonValue.Create("Sketch1")),
        };

    private static JsonObject CreateConstructionPlane(JsonObject p)
        => new()
        {
            ["plane_name"] = "ConstructionPlane_mock",
            ["method"] = GetOr(p, "method", JsonValue.Create("offset")),
        };

    private static JsonObject CreateConstructionAxis(JsonObject p)
        => new()
        {
            ["axis_name"] = "ConstructionAxis_mock",
            ["method"] = GetOr(p, "method", JsonValue.Create("two_points")),
        };

    private static JsonObject DrawSpline(JsonObject p)
        => new()
        {
            ["sketch_name"] = "Sketch_mock_xy",
            ["spline_type"] = GetOr(p, "spline_type", JsonValue.Create("fit_points")),
            ["point_count"] = CountOf(p, "points"),
        };

    private static JsonObject OffsetCurve(JsonObject p)
        => new()
        {
            ["sketch_name"] = GetOr(p, "sketch_name", JsonValue.Create("Sketch1")),
            ["offset_distance"] = GetOr(p, "offset_distance", JsonValue.Create(0.5)),
            ["new_curve_count"] = 4,
        };

    private static JsonObject TrimCurve(JsonObject p)
        => new()
        {
            ["sketch_name"] = GetOr(p, "sketch_name", JsonValue.Create("Sketch1")),
            ["curve_index"] = GetOr(p, "curve_index", JsonValue.Create(0)),
            ["trimmed"] = true,
        };

    private static JsonObject ExtendCurve(JsonObject p)
        => new()
        {
            ["sketch_name"] = GetOr(p, "sketch_name", JsonValue.Create("Sketch1")),
            ["curve_index"] = GetOr(p, "curve_index", JsonValue.Create(0)),
            ["extended"] = true,
        };

    private static JsonObject CreateThread(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["face_index"] = GetOr(p, "face_index", JsonValue.Create(0)),
            ["is_modeled"] = GetOr(p, "is_modeled", JsonValue.Create(false)),
            ["thread_designation"] = GetOr(p, "thread_designation", JsonValue.Create("M10x1.5")),
        };

    private static JsonObject DraftFaces(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["angle"] = GetOr(p, "angle", JsonValue.Create(5)),
            ["face_count"] = 4,
        };

    private static JsonObject SplitBody(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["result_bodies"] = new JsonArray("Body1", "Body1_split"),
        };

    private static JsonObject SplitFace(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["faces_split"] = 2,
        };

    private static JsonObject OffsetFaces(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["distance"] = GetOr(p, "distance", JsonValue.Create(0.5)),
        };

    private static JsonObject ScaleBody(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["scale"] = GetOr(p, "scale", JsonValue.Create(1.0)),
        };

    private static JsonObject CreateBox(JsonObject p)
        => new()
        {
            ["body_name"] = "Box_mock",
            ["length"] = GetOr(p, "length", JsonValue.Create(1)),
            ["width"] = GetOr(p, "width", JsonValue.Create(1)),
            ["height"] = GetOr(p, "height", JsonValue.Create(1)),
        };

    private static JsonObject CreateCylinder(JsonObject p)
        => new()
        {
            ["body_name"] = "Cylinder_mock",
            ["radius"] = GetOr(p, "radius", JsonValue.Create(1)),
            ["height"] = GetOr(p, "height", JsonValue.Create(1)),
        };

    private static JsonObject CreateSphere(JsonObject p)
        => new()
        {
            ["body_name"] = "Sphere_mock",
            ["radius"] = GetOr(p, "radius", JsonValue.Create(1)),
        };

    private static JsonObject CreateTorus(JsonObject p)
        => new()
        {
            ["body_name"] = "Torus_mock",
            ["major_radius"] = GetOr(p, "major_radius", JsonValue.Create(2)),
            ["minor_radius"] = GetOr(p, "minor_radius", JsonValue.Create(0.5)),
        };

    private static JsonObject CreateAsBuiltJoint(JsonObject p)
        => new()
        {
            ["component_one"] = GetOr(p, "component_one", JsonValue.Create("Comp1")),
            ["component_two"] = GetOr(p, "component_two", JsonValue.Create("Comp2")),
            ["joint_type"] = GetOr(p, "joint_type", JsonValue.Create("rigid")),
            ["joint_name"] = "AsBuiltJoint_mock",
        };

    private static JsonObject CreateRigidGroup(JsonObject p)
        => new()
        {
            ["component_names"] = ListOr(p, "component_names"),
            ["rigid_group_name"] = "RigidGroup_mock",
        };

    private static JsonObject MeasureDistance(JsonObject p)
        => new()
        {
            ["entity_one"] = GetOr(p, "entity_one", JsonValue.Create("Body1")),
            ["entity_two"] = GetOr(p, "entity_two", JsonValue.Create("Body2")),
            ["distance"] = 2.54,
            ["point_one"] = new JsonArray(0.0, 0.0, 0.0),
            ["point_two"] = new JsonArray(2.54, 0.0, 0.0),
        };

    private static JsonObject MeasureAngle(JsonObject p)
        => new()
        {
            ["entity_one"] = GetOr(p, "entity_one", JsonValue.Create("Face1")),
            ["entity_two"] = GetOr(p, "entity_two", JsonValue.Create("Face2")),
            ["angle_degrees"] = 90.0,
        };

    private static JsonObject GetPhysicalProperties(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["mass"] = 0.785,
            ["volume"] = 1.0,
            ["area"] = 6.0,
            ["density"] = 0.00785,
            ["center_of_mass"] = new JsonArray(0.5, 0.5, 0.5),
        };

    private static JsonObject CreateSectionAnalysis(JsonObject p)
        => new()
        {
            ["plane"] = GetOr(p, "plane", JsonValue.Create("yz")),
            ["offset"] = GetOr(p, "offset", JsonValue.Create(0)),
            ["analysis_name"] = "SectionAnalysis_mock",
        };

    private static JsonObject CheckInterference(JsonObject p)
        => new()
        {
            ["component_names"] = ListOr(p, "component_names"),
            ["interference_count"] = 0,
            ["interferences"] = new JsonArray(),
        };

    private static JsonObject CompareMeshes(JsonObject p)
        => new()
        {
            ["mesh_a"] = GetOr(p, "mesh_name_a", JsonValue.Create("Mesh1")),
            ["mesh_b"] = GetOr(p, "mesh_name_b", JsonValue.Create("Mesh2")),
            ["node_count"] = 1200,
            ["min_deviation"] = -0.004,
            ["max_deviation"] = 0.006,
            ["mean_abs_deviation"] = 0.001,
            ["rms_deviation"] = 0.0015,
            ["max_abs_deviation"] = 0.006,
            ["units"] = "cm",
        };

    private static JsonObject CreateUcs(JsonObject p)
    {
        var name = StrOr(p, "name", "UCS1");
        return new JsonObject
        {
            ["name"] = name,
            ["origin"] = new JsonArray
            {
                GetOr(p, "x", JsonValue.Create(0)),
                GetOr(p, "y", JsonValue.Create(0)),
                GetOr(p, "z", JsonValue.Create(0)),
            },
            ["angles_deg"] = new JsonArray
            {
                GetOr(p, "angle_x", JsonValue.Create(0)),
                GetOr(p, "angle_y", JsonValue.Create(0)),
                GetOr(p, "angle_z", JsonValue.Create(0)),
            },
            ["reference_sketch"] = $"UCS_{name}_ref",
        };
    }

    private static JsonObject SetColor(JsonObject p)
    {
        var red = Int(p, "red", 255);
        var green = Int(p, "green", 0);
        var blue = Int(p, "blue", 0);
        var opacity = GetOr(p, "opacity", JsonValue.Create(1.0)) is JsonValue opacityNode &&
                     opacityNode.TryGetValue<double>(out var opacityValue)
            ? opacityValue
            : 1.0;
        var alpha = (int)Math.Round(opacity * 255);
        return new JsonObject
        {
            ["body"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["color"] = new JsonArray(red, green, blue),
            ["opacity"] = opacity,
            ["appearance"] = $"MCP_{red}_{green}_{blue}_{alpha}",
        };
    }

    private static JsonObject SetAppearance(JsonObject p)
        => new()
        {
            ["target_name"] = GetOr(p, "target_name", JsonValue.Create("Body1")),
            ["appearance_name"] = GetOr(p, "appearance_name", JsonValue.Create("Steel - Satin")),
            ["applied"] = true,
        };

    private static JsonObject ProjectGeometry(JsonObject p)
        => new()
        {
            ["source_name"] = GetOr(p, "source_name", JsonValue.Create("Body1")),
            ["sketch_name"] = GetOr(p, "sketch_name", JsonValue.Create("Sketch1")),
            ["projected_curves"] = 4,
        };

    private static JsonObject SuppressFeature(JsonObject p)
        => new()
        {
            ["feature_name"] = GetOr(p, "feature_name", JsonValue.Create("Feature1")),
            ["suppressed"] = true,
        };

    private static JsonObject UnsuppressFeature(JsonObject p)
        => new()
        {
            ["feature_name"] = GetOr(p, "feature_name", JsonValue.Create("Feature1")),
            ["suppressed"] = false,
        };

    private static JsonObject PatchSurface(JsonObject p)
        => new()
        {
            ["sketch_name"] = GetOr(p, "sketch_name", JsonValue.Create("Sketch1")),
            ["body_name"] = "PatchSurface_mock",
            ["continuity"] = GetOr(p, "continuity", JsonValue.Create("connected")),
        };

    private static JsonObject StitchSurfaces(JsonObject p)
        => new()
        {
            ["body_names"] = ListOr(p, "body_names"),
            ["result_body"] = "StitchedBody_mock",
            ["tolerance"] = GetOr(p, "tolerance", JsonValue.Create(0.01)),
        };

    private static JsonObject ThickenSurface(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Surface1")),
            ["thickness"] = GetOr(p, "thickness", JsonValue.Create(0.1)),
            ["result_body"] = "ThickenedBody_mock",
        };

    private static JsonObject RuledSurface(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["edge_index"] = GetOr(p, "edge_index", JsonValue.Create(0)),
            ["distance"] = GetOr(p, "distance", JsonValue.Create(1.0)),
            ["result_body"] = "RuledSurface_mock",
        };

    private static JsonObject TrimSurface(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Surface1")),
            ["tool_name"] = GetOr(p, "tool_name", JsonValue.Create("Tool1")),
            ["trimmed"] = true,
        };

    private static JsonObject CreateFlange(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("SheetBody1")),
            ["edge_index"] = GetOr(p, "edge_index", JsonValue.Create(0)),
            ["height"] = GetOr(p, "height", JsonValue.Create(1.0)),
            ["angle"] = GetOr(p, "angle", JsonValue.Create(90)),
        };

    private static JsonObject CreateBend(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("SheetBody1")),
            ["angle"] = GetOr(p, "angle", JsonValue.Create(90)),
            ["bend_radius"] = GetOr(p, "bend_radius", JsonValue.Create(0.1)),
        };

    private static JsonObject FlatPattern(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("SheetBody1")),
            ["flat_pattern_name"] = "FlatPattern_mock",
        };

    private static JsonObject Unfold(JsonObject p)
        => new()
        {
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("SheetBody1")),
            ["bends_unfolded"] = Get(p, "bend_indices") is JsonArray bendIndices ? bendIndices.Count : 2,
        };

    private static JsonObject CamCreateSetup(JsonObject p)
        => new()
        {
            ["setup_name"] = GetOr(p, "name", JsonValue.Create("Setup1")),
            ["body_name"] = GetOr(p, "body_name", JsonValue.Create("Body1")),
            ["operation_type"] = GetOr(p, "operation_type", JsonValue.Create("milling")),
            ["stock_mode"] = GetOr(p, "stock_mode", JsonValue.Create("relative_box")),
        };

    private static JsonObject CamCreateOperation(JsonObject p)
        => new()
        {
            ["setup_name"] = GetOr(p, "setup_name", JsonValue.Create("Setup1")),
            ["operation_name"] = GetOr(p, "name", JsonValue.Create("Operation1")),
            ["strategy"] = GetOr(p, "strategy", JsonValue.Create("2d_contour")),
            ["tool_diameter"] = GetOr(p, "tool_diameter", JsonValue.Create(0.6)),
        };

    private static JsonObject CamGenerateToolpath(JsonObject p)
        => new()
        {
            ["setup_name"] = GetOr(p, "setup_name", JsonValue.Create("Setup1")),
            ["operation_name"] = Get(p, "operation_name"),
            ["generated"] = true,
            ["toolpath_count"] = 1,
        };

    private static JsonObject CamPostProcess(JsonObject p)
    {
        var setup = StrOr(p, "setup_name", "Setup1");
        var post = StrOr(p, "post_processor", "fanuc");
        return new JsonObject
        {
            ["setup_name"] = setup,
            ["post_processor"] = post,
            ["output_file"] = $"~/Desktop/{setup}.nc",
            ["output_units"] = GetOr(p, "output_units", JsonValue.Create("mm")),
        };
    }

    private static JsonObject CamListSetups()
        => new()
        {
            ["setups"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Setup1",
                    ["operation_type"] = "milling",
                    ["operation_count"] = 2,
                },
            },
        };

    private static JsonObject CamListOperations(JsonObject p)
        => new()
        {
            ["setup_name"] = GetOr(p, "setup_name", JsonValue.Create("Setup1")),
            ["operations"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Face1",
                    ["strategy"] = "face",
                    ["has_toolpath"] = true,
                },
                new JsonObject
                {
                    ["name"] = "2D Contour1",
                    ["strategy"] = "2d_contour",
                    ["has_toolpath"] = false,
                },
            },
        };

    private static JsonObject CamGetOperationInfo(JsonObject p)
        => new()
        {
            ["setup_name"] = GetOr(p, "setup_name", JsonValue.Create("Setup1")),
            ["operation_name"] = GetOr(p, "operation_name", JsonValue.Create("Face1")),
            ["strategy"] = "face",
            ["tool_diameter"] = 0.6,
            ["stepdown"] = 0.1,
            ["feed_rate"] = 100.0,
            ["spindle_speed"] = 10000,
            ["has_toolpath"] = true,
            ["toolpath_valid"] = true,
        };

    private static JsonObject RenderView(JsonObject p)
        => new()
        {
            ["view"] = GetOr(p, "view", JsonValue.Create("current")),
            ["width"] = Int(p, "width", 1024),
            ["height"] = Int(p, "height", 768),
            ["image_format"] = "png",
            ["image_base64"] = MockPngBase64,
            ["bytes"] = MockPngBase64.Length,
        };

    private static JsonObject DefaultMock(JsonObject p)
        => new()
        {
            ["warning"] = "no mock handler for this command",
            // Clone: the caller keeps its own parameter object (STJ nodes
            // belong to one parent; the reference embeds the same dict).
            ["params_received"] = p.DeepClone(),
        };
}
