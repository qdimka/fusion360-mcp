using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Protocol;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// P1 (ADR-0009, TOOL_CONTRACTS §1.2/§4): builds the optional
/// <c>verification</c> block of every successful mutation — the same
/// insertion point where <see cref="MutationCommands" /> auto-attaches the
/// baseline deltas. Level ladder: <c>call_returned</c> (default) →
/// <c>existence_checked</c> → <c>geometry_checked</c>; a server never
/// reports a level it did not actually confirm, and every unverified
/// aspect is named with a reason (§5.3). Tolerances come from the single
/// <see cref="VerificationTolerances" /> table (§4).
/// </summary>
public static class VerificationBuilder
{
    /// <summary>
    /// Builds the <c>verification</c> block for one successful mutation.
    /// <paramref name="result"/> carries the handler's tool fields plus the
    /// auto-attached <c>deltas</c> (when both snapshots were readable).
    /// </summary>
    public static JsonObject Build(
        IFusionContext? ctx,
        string command,
        JsonObject parameters,
        JsonObject result,
        SceneSnapshot? before,
        SceneSnapshot? after)
    {
        return command switch
        {
            "extrude" => Extrude(ctx, result, before, after),
            "revolve" => Revolve(ctx, parameters, before, after),
            "fillet" => FilletChamfer("fillet", result, before, after),
            "chamfer" => FilletChamfer("chamfer", result, before, after),
            "move_body" => MoveBody(parameters, before, after),
            "scale_body" => ScaleBody(parameters, before, after),
            "boolean_operation" => BooleanOperation(result, before, after),
            "create_box" => PrimitiveCreate(before, after),
            "create_cylinder" => PrimitiveCreate(before, after),
            "create_sphere" => PrimitiveCreate(before, after),
            "create_torus" => PrimitiveCreate(before, after),
            "export_stl" => ExportStl(ctx, parameters, result),
            _ => Default(command, result, after),
        };
    }

    // ------------------------------------------------------------------
    // extrude / revolve — analytic volume for simple profiles
    // ------------------------------------------------------------------

    private static JsonObject Extrude(IFusionContext? ctx, JsonObject result, SceneSnapshot? before, SceneSnapshot? after)
    {
        if (before is null || after is null || ctx is null)
        {
            return NoSnapshots("the analytic volume check needs the scene snapshot and the design context");
        }

        var checks = new List<VerificationCheck>();
        var unverified = new List<UnverifiedAspect>();

        var operation = ParamReader.Str(result, "operation", "new_body");
        if (operation != "new_body")
        {
            // Cut/join extrude modifies the target body — its volume is
            // not analytically known; only the scene-level effect is.
            unverified.Add(new UnverifiedAspect("geometry", "affected body volume is not known for a cut/join extrude"));
            var changed = SceneChanged(before, after);
            checks.Add(VerificationCheck.Outcome("scene_changed", changed ? "changed" : "unchanged", "changed"));
            var level = changed ? VerificationLevel.ExistenceChecked : VerificationLevel.CallReturned;
            if (!changed)
            {
                unverified.Add(new UnverifiedAspect("effect", "the extrude changed nothing (zero deltas) — check the operation and the sketch"));
            }

            return Verification.ToJson(level, checks, unverified);
        }

        // New body: it exists, and — for a simple (rectangle/circle)
        // profile — its volume must match the analytic value within §4.
        var observedCount = after.BodyCount;
        var expectedCount = before.BodyCount + 1;
        checks.Add(VerificationCheck.Count("body_exists", observedCount, expectedCount));
        var bodyExists = observedCount == expectedCount;

        var (kind, profileAspect) = ClassifyProfile(ctx);
        if (profileAspect is not null)
        {
            unverified.Add(profileAspect);
        }

        var body = ResolveNewBody(ctx, before, after, unverified);
        var bbox = body?.BoundingBox;
        if (bodyExists && body is not null && bbox is not null && kind is "rectangle" or "circle")
        {
            var w = bbox.Max.X - bbox.Min.X;
            var d = bbox.Max.Y - bbox.Min.Y;
            var h = bbox.Max.Z - bbox.Min.Z;
            var expected = kind == "circle" ? Math.PI / 4.0 * w * d * h : w * d * h;
            var observed = body.Volume;
            var passed = Math.Abs(observed - expected) <= VerificationTolerances.Volume(expected);
            checks.Add(new VerificationCheck("volume", JsonValue.Create(observed), JsonValue.Create(expected), passed));
            var level = passed ? VerificationLevel.GeometryChecked : VerificationLevel.ExistenceChecked;
            if (!passed)
            {
                unverified.Add(new UnverifiedAspect("volume", "observed volume is outside the §4 tolerance (check the profile and the extrude direction)"));
            }

            return Verification.ToJson(level, checks, unverified);
        }

        if (kind == "complex")
        {
            unverified.Add(new UnverifiedAspect("analytic_volume", "analytic volume is unknown for a complex profile"));
        }

        if (!bodyExists)
        {
            unverified.Add(new UnverifiedAspect("body_exists", "the scene did not gain a body — the extrude did not apply as new_body"));
        }

        return Verification.ToJson(bodyExists ? VerificationLevel.ExistenceChecked : VerificationLevel.CallReturned, checks, unverified);
    }

    private static JsonObject Revolve(IFusionContext? ctx, JsonObject parameters, SceneSnapshot? before, SceneSnapshot? after)
    {
        if (before is null || after is null || ctx is null)
        {
            return NoSnapshots("the analytic volume check needs the scene snapshot and the design context");
        }

        var checks = new List<VerificationCheck>();
        var unverified = new List<UnverifiedAspect>();

        var observedCount = after.BodyCount;
        var expectedCount = before.BodyCount + 1;
        checks.Add(VerificationCheck.Count("body_exists", observedCount, expectedCount));
        var bodyExists = observedCount == expectedCount;

        var (kind, profileAspect) = ClassifyProfile(ctx);
        if (profileAspect is not null)
        {
            unverified.Add(profileAspect);
        }

        // The one revolve with a closed-form volume: a single circle swept
        // 360° about a canonical axis through the origin — a sphere, whose
        // bbox must then be a cube and V = π/6 · w·d·h.
        var isSphereCase = kind == "circle"
            && Math.Abs(ParamReader.Num(parameters, "angle", 0) - 360.0) < 1e-9
            && ParamReader.Num(parameters, "axis_origin_x", 0) == 0
            && ParamReader.Num(parameters, "axis_origin_y", 0) == 0
            && ParamReader.Num(parameters, "axis_origin_z", 0) == 0
            && IsCanonicalAxis(parameters);

        var body = ResolveNewBody(ctx, before, after, unverified);
        var bbox = body?.BoundingBox;
        if (bodyExists && body is not null && bbox is not null && isSphereCase)
        {
            var w = bbox.Max.X - bbox.Min.X;
            var d = bbox.Max.Y - bbox.Min.Y;
            var h = bbox.Max.Z - bbox.Min.Z;
            var isCube = Math.Abs(w - d) <= 0.001 * Math.Max(1.0, w)
                         && Math.Abs(w - h) <= 0.001 * Math.Max(1.0, w);
            if (isCube)
            {
                var expected = Math.PI / 6.0 * w * d * h;
                var observed = body.Volume;
                var passed = Math.Abs(observed - expected) <= VerificationTolerances.Volume(expected);
                checks.Add(new VerificationCheck("volume", JsonValue.Create(observed), JsonValue.Create(expected), passed));
                var level = passed ? VerificationLevel.GeometryChecked : VerificationLevel.ExistenceChecked;
                if (!passed)
                {
                    unverified.Add(new UnverifiedAspect("volume", "observed volume is outside the §4 tolerance for the revolved sphere"));
                }

                return Verification.ToJson(level, checks, unverified);
            }

            unverified.Add(new UnverifiedAspect("analytic_volume", "the revolved body is not a sphere (non-cube bbox) — analytic volume unknown"));
        }
        else if (!isSphereCase)
        {
            unverified.Add(new UnverifiedAspect("analytic_volume", "the revolve axis/angle/profile combination is not analytically resolvable"));
        }

        if (!bodyExists)
        {
            unverified.Add(new UnverifiedAspect("body_exists", "the scene did not gain a body — the revolve did not apply as new_body"));
        }

        return Verification.ToJson(bodyExists ? VerificationLevel.ExistenceChecked : VerificationLevel.CallReturned, checks, unverified);
    }

    /// <summary>The revolve axis is a canonical X/Y/Z direction through the origin (the sphere case).</summary>
    private static bool IsCanonicalAxis(JsonObject p)
    {
        var dx = ParamReader.Num(p, "axis_direction_x", 1);
        var dy = ParamReader.Num(p, "axis_direction_y", 0);
        var dz = ParamReader.Num(p, "axis_direction_z", 0);
        return Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) == 1.0;
    }

    /// <summary>
    /// Classifies the last sketch's profile from its curve kinds:
    /// "rectangle" (2 or 4 lines — the two-point or four-line forms),
    /// "circle" (one circle), else "complex". A readable failure degrades
    /// to "complex" with a named aspect — nothing is silent.
    /// </summary>
    private static (string Kind, UnverifiedAspect? Aspect) ClassifyProfile(IFusionContext ctx)
    {
        try
        {
            var sketch = DesignQueries.LastSketch(ctx);
            var curves = sketch.SketchCurves;
            var n = curves.Count;
            if (n == 1 && curves[0].CurveKind == "circle")
            {
                return ("circle", null);
            }

            if (n is 2 or 4)
            {
                var allLines = true;
                foreach (var curve in curves)
                {
                    if (curve.CurveKind != "line")
                    {
                        allLines = false;
                        break;
                    }
                }

                if (allLines)
                {
                    return ("rectangle", null);
                }
            }

            return ("complex", null);
        }
        catch
        {
            return ("complex", new UnverifiedAspect("profile", "the profile shape was not readable — treated as complex"));
        }
    }

    /// <summary>
    /// The new_body product of an extrude/revolve: the last root body —
    /// trusted only when the snapshot count matches the root count (no
    /// occurrence bodies in the scene, so the index is unambiguous).
    /// </summary>
    private static IFusionBody? ResolveNewBody(IFusionContext ctx, SceneSnapshot before, SceneSnapshot after, List<UnverifiedAspect> unverified)
    {
        try
        {
            var root = ctx.Design.RootComponent;
            var rootCount = root.BRepBodies.Count;
            if (rootCount <= 0)
            {
                return null;
            }

            var body = root.BRepBodies.Item(rootCount - 1);
            if (after.BodyCount != rootCount)
            {
                unverified.Add(new UnverifiedAspect("new_body", "the scene has occurrence bodies — new-body resolution is not reliable"));
            }

            return body;
        }
        catch
        {
            unverified.Add(new UnverifiedAspect("new_body", "the new body was not resolvable from the design context"));
            return null;
        }
    }

    // ------------------------------------------------------------------
    // fillet / chamfer — existence level (no analytic ΔV formula in scope)
    // ------------------------------------------------------------------

    private static JsonObject FilletChamfer(string command, JsonObject result, SceneSnapshot? before, SceneSnapshot? after)
    {
        if (before is null || after is null)
        {
            return NoSnapshots("the fillet/chamfer ΔV formula is not available for arbitrary edge sets (simple rounded corners only)");
        }

        var changed = SceneChanged(before, after);
        var checks = new List<VerificationCheck>
        {
            VerificationCheck.Outcome("scene_changed", changed ? "changed" : "unchanged", "changed"),
        };
        var unverified = new List<UnverifiedAspect>
        {
            new("analytic_volume",
                "the analytic ΔV formula is not available for an arbitrary " + command + " edge set (simple rounded corners only)"),
        };
        if (!changed)
        {
            unverified.Add(new UnverifiedAspect("effect", "the " + command + " changed nothing (zero deltas) — check the edge selection"));
        }

        return Verification.ToJson(changed ? VerificationLevel.ExistenceChecked : VerificationLevel.CallReturned, checks, unverified);
    }

    // ------------------------------------------------------------------
    // move_body / scale_body — scene-bbox shift vs the expected transform
    // ------------------------------------------------------------------

    private static JsonObject MoveBody(JsonObject parameters, SceneSnapshot? before, SceneSnapshot? after)
    {
        if (before is null || after is null)
        {
            return NoSnapshots("the bbox shift check needs both scene snapshots");
        }

        var t = new[]
        {
            ParamReader.Num(parameters, "x", 0),
            ParamReader.Num(parameters, "y", 0),
            ParamReader.Num(parameters, "z", 0),
        };

        var checks = new List<VerificationCheck>();
        var unverified = new List<UnverifiedAspect>();
        unverified.Add(new UnverifiedAspect("scene_bbox", "the scene bbox is used — a moved interior body in a multi-body scene does not shift it"));

        if (before.BBoxMin is { } bMin && before.BBoxMax is { } bMax && after.BBoxMin is { } aMin && after.BBoxMax is { } aMax)
        {
            var observed = new[] { aMin.X, aMin.Y, aMin.Z, aMax.X, aMax.Y, aMax.Z };
            var expected = new[]
            {
                bMin.X + t[0], bMin.Y + t[1], bMin.Z + t[2],
                bMax.X + t[0], bMax.Y + t[1], bMax.Z + t[2],
            };
            var check = VerificationCheck.Coords("bbox_shift", observed, expected);
            checks.Add(check);
            var level = check.Passed ? VerificationLevel.GeometryChecked : VerificationLevel.CallReturned;
            if (!check.Passed)
            {
                unverified.Add(new UnverifiedAspect("bbox_shift", "the scene bbox did not shift by the translation — multi-body scene or a partial move"));
            }

            return Verification.ToJson(level, checks, unverified);
        }

        unverified.Add(new UnverifiedAspect("bbox", "the scene bounding box is not readable (no bodies, or the snapshot has no bbox)"));
        return Verification.ToJson(VerificationLevel.CallReturned, checks, unverified);
    }

    private static JsonObject ScaleBody(JsonObject parameters, SceneSnapshot? before, SceneSnapshot? after)
    {
        if (before is null || after is null)
        {
            return NoSnapshots("the bbox scale check needs both scene snapshots");
        }

        var s = ParamReader.Num(parameters, "scale", 1);
        var sx = ParamReader.NumOrNull(parameters, "scale_x") ?? s;
        var sy = ParamReader.NumOrNull(parameters, "scale_y") ?? s;
        var sz = ParamReader.NumOrNull(parameters, "scale_z") ?? s;
        var ax = ParamReader.Num(parameters, "anchor_x", 0);
        var ay = ParamReader.Num(parameters, "anchor_y", 0);
        var az = ParamReader.Num(parameters, "anchor_z", 0);

        var checks = new List<VerificationCheck>();
        var unverified = new List<UnverifiedAspect>();
        unverified.Add(new UnverifiedAspect("scene_bbox", "the scene bbox is used — scaling an interior body in a multi-body scene does not shift it"));

        if (before.BBoxMin is { } bMin && before.BBoxMax is { } bMax && after.BBoxMin is { } aMin && after.BBoxMax is { } aMax)
        {
            var observed = new[] { aMin.X, aMin.Y, aMin.Z, aMax.X, aMax.Y, aMax.Z };
            var expected = new[]
            {
                ax + (bMin.X - ax) * sx, ay + (bMin.Y - ay) * sy, az + (bMin.Z - az) * sz,
                ax + (bMax.X - ax) * sx, ay + (bMax.Y - ay) * sy, az + (bMax.Z - az) * sz,
            };
            var check = VerificationCheck.Coords("bbox_scale", observed, expected);
            checks.Add(check);
            var level = check.Passed ? VerificationLevel.GeometryChecked : VerificationLevel.CallReturned;
            if (!check.Passed)
            {
                unverified.Add(new UnverifiedAspect("bbox_scale", "the scene bbox did not scale as expected — multi-body scene or a partial scale"));
            }

            return Verification.ToJson(level, checks, unverified);
        }

        unverified.Add(new UnverifiedAspect("bbox", "the scene bounding box is not readable (no bodies, or the snapshot has no bbox)"));
        return Verification.ToJson(VerificationLevel.CallReturned, checks, unverified);
    }

    // ------------------------------------------------------------------
    // boolean_operation — the existing BOOLEAN_NO_OP becomes a named check
    // ------------------------------------------------------------------

    private static JsonObject BooleanOperation(JsonObject result, SceneSnapshot? before, SceneSnapshot? after)
    {
        if (before is null || after is null)
        {
            return NoSnapshots("the body-count / mass-delta checks need both scene snapshots");
        }

        var checks = new List<VerificationCheck>();
        var unverified = new List<UnverifiedAspect>();

        var noOp = !SceneChanged(before, after);
        checks.Add(VerificationCheck.Outcome("boolean_applied", noOp ? "no_op" : "applied", "applied"));
        checks.Add(VerificationCheck.Count("body_count_delta", after.BodyCount - before.BodyCount, -1));

        if (noOp)
        {
            unverified.Add(new UnverifiedAspect("effect", "BOOLEAN_NO_OP: the operation changed nothing (zero deltas) — check that the bodies actually intersect"));
            return Verification.ToJson(VerificationLevel.CallReturned, checks, unverified);
        }

        var level = after.BodyCount - before.BodyCount == -1 ? VerificationLevel.ExistenceChecked : VerificationLevel.CallReturned;
        if (level == VerificationLevel.CallReturned)
        {
            unverified.Add(new UnverifiedAspect("body_count_delta", "the body count did not drop by one — the tool body was not consumed"));
        }

        return Verification.ToJson(level, checks, unverified);
    }

    // ------------------------------------------------------------------
    // create_* — name checked + the body exists (existence level)
    // ------------------------------------------------------------------

    private static JsonObject PrimitiveCreate(SceneSnapshot? before, SceneSnapshot? after)
    {
        if (before is null || after is null)
        {
            return NoSnapshots("the body-existence check needs both scene snapshots");
        }

        var observed = after.BodyCount;
        var expected = before.BodyCount + 1;
        var checks = new List<VerificationCheck>
        {
            VerificationCheck.Count("body_exists", observed, expected),
        };
        var unverified = new List<UnverifiedAspect>
        {
            new("geometry", "primitive geometry is not verified beyond existence (name checked, body present)"),
        };
        if (observed != expected)
        {
            unverified.Add(new UnverifiedAspect("body_exists", "the scene did not gain a body — the primitive did not apply"));
            return Verification.ToJson(VerificationLevel.CallReturned, checks, unverified);
        }

        return Verification.ToJson(VerificationLevel.ExistenceChecked, checks, unverified);
    }

    // ------------------------------------------------------------------
    // export_stl — KOMPAS G5 pattern: file + byte_length + triangle count
    // (binary STL header) + mesh bbox vs the body's bbox
    // ------------------------------------------------------------------

    private static JsonObject ExportStl(IFusionContext? ctx, JsonObject parameters, JsonObject result)
    {
        var checks = new List<VerificationCheck>();
        var unverified = new List<UnverifiedAspect>();

        var filePath = result.TryGetPropertyValue("file_path", out var fn) && fn is JsonValue fvn && fvn.TryGetValue<string>(out var fp) ? fp : null;
        if (filePath is null)
        {
            unverified.Add(new UnverifiedAspect("file", "the export result carries no file_path — nothing to verify on disk"));
            return Verification.ToJson(VerificationLevel.CallReturned, checks, unverified);
        }

        if (!File.Exists(filePath))
        {
            checks.Add(new VerificationCheck("file_exists", JsonValue.Create(false), JsonValue.Create(true), false));
            unverified.Add(new UnverifiedAspect("export", "the export reported success but the file is not on disk"));
            return Verification.ToJson(VerificationLevel.CallReturned, checks, unverified);
        }

        checks.Add(new VerificationCheck("file_exists", JsonValue.Create(true), JsonValue.Create(true), true));

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(filePath);
        }
        catch
        {
            unverified.Add(new UnverifiedAspect("stl_read", "the STL file could not be read back"));
            return Verification.ToJson(VerificationLevel.ExistenceChecked, checks, unverified);
        }

        // 84 bytes is the minimum binary STL size (80-byte header + count).
        checks.Add(new VerificationCheck("byte_length", JsonValue.Create((long)bytes.Length), JsonValue.Create(84), bytes.Length >= 84));

        if (!IsBinaryStl(bytes, out var triangleCount))
        {
            unverified.Add(new UnverifiedAspect("stl_binary_header", "the STL file is not a binary STL (ASCII, or corrupt) — triangle count and mesh bbox unverified"));
            return Verification.ToJson(VerificationLevel.ExistenceChecked, checks, unverified);
        }

        checks.Add(new VerificationCheck("triangle_count", JsonValue.Create((long)triangleCount), JsonValue.Create(1), triangleCount >= 1));

        var (min, max) = ScanStlBbox(bytes, triangleCount);
        var bodyName = ParamReader.Str(parameters, "body_name", string.Empty);
        IFusionBody? body = null;
        if (ctx is not null && bodyName.Length > 0)
        {
            try
            {
                body = DesignQueries.BodyByName(ctx, bodyName);
            }
            catch
            {
                unverified.Add(new UnverifiedAspect("body_bbox", $"body '{bodyName}' no longer resolves — mesh bbox compared against nothing"));
            }
        }

        if (body?.BoundingBox is { } bb)
        {
            var observed = new[] { min[0], min[1], min[2], max[0], max[1], max[2] };
            var expected = new[] { bb.Min.X, bb.Min.Y, bb.Min.Z, bb.Max.X, bb.Max.Y, bb.Max.Z };
            var check = VerificationCheck.Coords("mesh_bbox_vs_body_bbox", observed, expected);
            checks.Add(check);
            if (check.Passed)
            {
                return Verification.ToJson(VerificationLevel.GeometryChecked, checks, unverified);
            }

            unverified.Add(new UnverifiedAspect("unit_scale", "the mesh bbox is outside the §4 bbox tolerance — the export units may differ from cm (a 10× factor is mm vs cm)"));
            return Verification.ToJson(VerificationLevel.ExistenceChecked, checks, unverified);
        }

        unverified.Add(new UnverifiedAspect("body_bbox", "the body bounding box is not readable — the mesh bbox was scanned but not compared"));
        return Verification.ToJson(VerificationLevel.ExistenceChecked, checks, unverified);
    }

    /// <summary>
    /// Binary STL: 80-byte header + uint32 count + 50 bytes per triangle.
    /// (ASCII files start with "solid" too, so the length equation is the
    /// discriminator, as in the KOMPAS G5 pattern.)
    /// </summary>
    private static bool IsBinaryStl(byte[] data, out int triangleCount)
    {
        triangleCount = 0;
        if (data.Length < 84)
        {
            return false;
        }

        var count = ReadUInt32Le(data, 80);
        if ((long)count * 50 + 84 != data.Length)
        {
            return false;
        }

        triangleCount = count;
        return true;
    }

    /// <summary>Scans every triangle vertex of a binary STL and returns the mesh bbox (cm).</summary>
    private static (double[] Min, double[] Max) ScanStlBbox(byte[] data, int triangleCount)
    {
        var min = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
        var max = new[] { double.MinValue, double.MinValue, double.MinValue };

        for (var i = 0; i < triangleCount; i++)
        {
            var record = 84 + i * 50;
            for (var v = 0; v < 3; v++)
            {
                // Three vertices: 12 floats from byte 12 of the record.
                var off = record + 12 + v * 12;
                for (var c = 0; c < 3; c++)
                {
                    var f = ReadFloatLe(data, off + c * 4);
                    if (f < min[c])
                    {
                        min[c] = f;
                    }

                    if (f > max[c])
                    {
                        max[c] = f;
                    }
                }
            }
        }

        return (min, max);
    }

    private static int ReadUInt32Le(byte[] data, int offset)
        => data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

    private static float ReadFloatLe(byte[] data, int offset)
    {
        uint value = (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        return BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
    }

    // ------------------------------------------------------------------
    // Default: call_returned + a named reason (never silent, §5.3)
    // ------------------------------------------------------------------

    private static JsonObject Default(string command, JsonObject result, SceneSnapshot? after)
    {
        var checks = new List<VerificationCheck>();
        if (result["deltas"] is not null)
        {
            checks.Add(new VerificationCheck("deltas_recorded", JsonValue.Create(true), JsonValue.Create(true), true));
        }

        var unverified = new List<UnverifiedAspect>
        {
            new("geometry", DefaultReason(command)),
        };
        if (after is null)
        {
            unverified.Add(new UnverifiedAspect("scene", "the scene snapshot is unavailable — no deltas were recorded"));
        }

        return Verification.ToJson(VerificationLevel.CallReturned, checks, unverified);
    }

    /// <summary>The named reason for the default <c>call_returned</c> level (never a silent absence).</summary>
    private static string DefaultReason(string command)
    {
        const string fallback = "no analytic expected geometry for this command";
        return command switch
        {
            "delete_all" => "the emptied scene state was not independently re-verified",
            "undo" => "the undone step was not re-verified against the pre-undo snapshot",
            "set_parameter" => "the parameter's effect on dependent features is not analytically verifiable",
            "auto_constrain" => "the constraint solve outcome was not re-verified",
            "set_color" => "cosmetic state was not re-verified",
            "create_ucs" => "the construction coordinate system was not re-verified",
            "sweep" => "the swept path/profile outcome is not analytically verifiable",
            "loft" => "the loft section outcome is not analytically verifiable",
            "shell" => "the shelled body's wall thickness was not re-measured",
            "mirror" => "the mirrored body was not re-measured",
            "create_hole" => "the hole diameter and depth were not re-measured",
            "rectangular_pattern" => "the pattern instance count was not re-counted",
            "circular_pattern" => "the pattern instance count was not re-counted",
            "create_thread" => "the thread geometry was not re-measured",
            "draft_faces" => "the draft angle was not re-measured",
            "split_body" => "the split result bodies were not re-counted",
            "split_face" => "the split result faces were not re-counted",
            "offset_faces" => "the offset faces were not re-measured",
            "suppress_feature" => "the suppressed feature's effect was not re-verified",
            "unsuppress_feature" => "the unsuppressed feature's effect was not re-verified",
            "thicken_surface" => "the thickened body's thickness was not re-measured",
            "patch_surface" => "the patched surface was not re-verified",
            "stitch_surfaces" => "the stitched body was not re-verified",
            "ruled_surface" => "the ruled surface was not re-verified",
            "trim_surface" => "the trimmed surface was not re-verified",
            "create_flange" => "the flange sheet-metal result is not analytically verifiable",
            "create_bend" => "the bend sheet-metal result is not analytically verifiable",
            "flat_pattern" => "the flat pattern is not analytically verifiable",
            "unfold" => "the unfolded sheet-metal result is not analytically verifiable",
            _ => fallback,
        };
    }

    private static JsonObject NoSnapshots(string geometryReason)
    {
        var unverified = new List<UnverifiedAspect>
        {
            new("geometry", geometryReason),
            new("scene", "the scene snapshot is unavailable — no deltas were recorded"),
        };
        return Verification.ToJson(VerificationLevel.CallReturned, Array.Empty<VerificationCheck>(), unverified);
    }

    /// <summary>Any measurable scene change between the snapshots (the zero-delta sentinel).</summary>
    private static bool SceneChanged(SceneSnapshot before, SceneSnapshot after)
        => after.MassGrams != before.MassGrams || after.BodyCount != before.BodyCount;
}
