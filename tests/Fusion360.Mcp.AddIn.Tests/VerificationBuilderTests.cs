using System;
using System.IO;
using System.Text.Json.Nodes;
using Fusion360.Mcp.AddIn;
using Fusion360.Mcp.Core.Protocol;
using Xunit;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// P1 (ADR-0009, TOOL_CONTRACTS §1.2/§4): the VerificationBuilder ladder —
/// every mutation reports exactly the level it actually confirmed, and
/// every unverified aspect is named with a reason (§5.3).
/// </summary>
public class VerificationBuilderTests
{
    // ------------------------------------------------------------------
    // Fixture helpers
    // ------------------------------------------------------------------

    private static FakeFusionContext CtxWithSketch(string curveLabel, params string[] extraCurveLabels)
    {
        var ctx = new FakeFusionContext();
        var sketch = new FakeSketch { Name = "Sketch1", ProfilesCount = 1 };
        sketch.CurvesList.Add(new FakeSketchCurve { Label = curveLabel, CurveKind = KindOf(curveLabel) });
        foreach (var label in extraCurveLabels)
        {
            sketch.CurvesList.Add(new FakeSketchCurve { Label = label, CurveKind = KindOf(label) });
        }

        ctx.Design.RootComponent.AddSketch(sketch);
        return ctx;
    }

    /// <summary>Mirrors FakeSketchCurveCollections' label → kind mapping.</summary>
    private static string KindOf(string label) => label switch
    {
        "circle" => "circle",
        "arc" => "arc",
        "fitted_spline" => "spline",
        "control_spline" => "spline",
        _ => "line",
    };

    private static void AddBody(FakeFusionContext ctx, string name, double volume, FusionPoint min, FusionPoint max)
        => ctx.Design.RootComponent.AddBody(new FakeBody
        {
            Name = name,
            Volume = volume,
            BoundingBox = new FakeBBox(min, max),
        });

    private static SceneSnapshot Snap(int bodies, double mass, FusionPoint? min = null, FusionPoint? max = null)
        => new(bodies, mass, min, max);

    private static string Level(JsonObject block) => block["level"]!.GetValue<string>();
    private static JsonObject Aspect(JsonObject block, string name)
    {
        foreach (var node in block["unverified_aspects"]!.AsArray())
        {
            if (node!["name"]?.GetValue<string>() == name)
            {
                return node.AsObject();
            }
        }

        throw new Xunit.Sdk.XunitException($"aspect '{name}' not found in: {block}");
    }

    // ------------------------------------------------------------------
    // extrude — new_body with an analytically known volume
    // ------------------------------------------------------------------

    [Fact]
    public void Extrude_RectangleProfile_VolumeMatchesAnalyticValue_IsGeometryChecked()
    {
        var ctx = CtxWithSketch("line", "line");
        AddBody(ctx, "Extrude1", 4.0, new FusionPoint(0, 0, 0), new FusionPoint(2, 2, 1)); // V = 2·2·1 = 4

        var result = new JsonObject { ["operation"] = "new_body" };
        var block = VerificationBuilder.Build(ctx, "extrude", new JsonObject(), result,
            Snap(0, 0.0), Snap(1, 4.0));

        Assert.Equal("geometry_checked", Level(block));
        var checks = block["checks"]!.AsArray();
        Assert.Equal(2, checks.Count);
        Assert.Equal("body_exists", checks[0]!["name"]!.GetValue<string>());
        Assert.True(checks[0]!["passed"]!.GetValue<bool>());
        var volume = checks[1]!.AsObject();
        Assert.Equal("volume", volume["name"]!.GetValue<string>());
        Assert.True(volume["passed"]!.GetValue<bool>());
        Assert.Empty(block["unverified_aspects"]!.AsArray());
    }

    [Fact]
    public void Extrude_CircleProfile_VolumeIsAPartOfTheBBox()
    {
        var ctx = CtxWithSketch("circle");
        var expected = Math.PI; // π/4 · 2 · 2 · 1
        AddBody(ctx, "Extrude1", expected, new FusionPoint(0, 0, 0), new FusionPoint(2, 2, 1));

        var block = VerificationBuilder.Build(ctx, "extrude", new JsonObject(), new JsonObject { ["operation"] = "new_body" },
            Snap(0, 0.0), Snap(1, expected));

        Assert.Equal("geometry_checked", Level(block));
        Assert.Empty(block["unverified_aspects"]!.AsArray());
    }

    [Fact]
    public void Extrude_CircleVolumeOutOfTolerance_StaysAtExistence()
    {
        var ctx = CtxWithSketch("circle");
        AddBody(ctx, "Extrude1", 99.0, new FusionPoint(0, 0, 0), new FusionPoint(2, 2, 1));

        var block = VerificationBuilder.Build(ctx, "extrude", new JsonObject(), new JsonObject { ["operation"] = "new_body" },
            Snap(0, 0.0), Snap(1, 99.0));

        Assert.Equal("existence_checked", Level(block));
        var aspect = Aspect(block, "volume");
        Assert.Contains("tolerance", aspect["reason"]!.GetValue<string>());
    }

    [Fact]
    public void Extrude_ComplexProfile_IsNamed()
    {
        // One line: not a circle, not 2/4 lines → complex.
        var ctx = CtxWithSketch("fitted_spline");
        AddBody(ctx, "Extrude1", 3.0, new FusionPoint(0, 0, 0), new FusionPoint(1, 1, 3));

        var block = VerificationBuilder.Build(ctx, "extrude", new JsonObject(), new JsonObject { ["operation"] = "new_body" },
            Snap(0, 0.0), Snap(1, 3.0));

        Assert.Equal("existence_checked", Level(block));
        Assert.Contains(block["unverified_aspects"]!.AsArray(), n =>
            n!["name"]!.GetValue<string>() == "analytic_volume");
    }

    [Fact]
    public void Extrude_CutOperation_NamesTheEffectLevel()
    {
        var ctx = CtxWithSketch("line", "line");
        var block = VerificationBuilder.Build(ctx, "extrude", new JsonObject(), new JsonObject { ["operation"] = "cut" },
            Snap(1, 4.0), Snap(1, 3.0));

        Assert.Equal("existence_checked", Level(block)); // scene changed
        Assert.Contains(block["unverified_aspects"]!.AsArray(), n =>
            n!["name"]!.GetValue<string>() == "geometry");
    }

    [Fact]
    public void Extrude_NoSnapshots_IsCallReturnedWithNamedReasons()
    {
        var ctx = CtxWithSketch("line", "line");
        var block = VerificationBuilder.Build(ctx, "extrude", new JsonObject(), new JsonObject(), null, null);

        Assert.Equal("call_returned", Level(block));
        Assert.Empty(block["checks"]!.AsArray());
        Assert.Equal(2, block["unverified_aspects"]!.AsArray().Count);
    }

    // ------------------------------------------------------------------
    // revolve — the sphere case
    // ------------------------------------------------------------------

    [Fact]
    public void Revolve_FullCircleOnCanonicalAxis_SphereVolumeIsVerified()
    {
        var ctx = CtxWithSketch("circle");
        var expected = Math.PI / 6.0 * 2 * 2 * 2; // sphere of d = 2
        AddBody(ctx, "Revolve1", expected, new FusionPoint(-1, -1, -1), new FusionPoint(1, 1, 1));

        var parameters = new JsonObject { ["angle"] = 360, ["axis_direction_x"] = 1 };
        var block = VerificationBuilder.Build(ctx, "revolve", parameters, new JsonObject(),
            Snap(0, 0.0), Snap(1, expected));

        Assert.Equal("geometry_checked", Level(block));
        Assert.Empty(block["unverified_aspects"]!.AsArray());
    }

    [Fact]
    public void Revolve_PartialAngle_NamesTheAnalyticLimitation()
    {
        var ctx = CtxWithSketch("circle");
        AddBody(ctx, "Revolve1", 1.0, new FusionPoint(0, 0, 0), new FusionPoint(1, 1, 1));

        var parameters = new JsonObject { ["angle"] = 90 };
        var block = VerificationBuilder.Build(ctx, "revolve", parameters, new JsonObject(),
            Snap(0, 0.0), Snap(1, 1.0));

        Assert.Equal("existence_checked", Level(block));
        Assert.Contains(block["unverified_aspects"]!.AsArray(), n =>
            n!["name"]!.GetValue<string>() == "analytic_volume");
    }

    // ------------------------------------------------------------------
    // fillet / chamfer
    // ------------------------------------------------------------------

    [Fact]
    public void Fillet_ChangedScene_ExistenceWithNamedLimitation()
    {
        var block = VerificationBuilder.Build(null, "fillet", new JsonObject(), new JsonObject(),
            Snap(1, 4.0), Snap(1, 3.5));

        Assert.Equal("existence_checked", Level(block));
        Assert.Contains(block["unverified_aspects"]!.AsArray(), n =>
            n!["name"]!.GetValue<string>() == "analytic_volume");
    }

    [Fact]
    public void Chamfer_ZeroDelta_SaysSoLoudly()
    {
        var block = VerificationBuilder.Build(null, "chamfer", new JsonObject(), new JsonObject(),
            Snap(1, 4.0), Snap(1, 4.0));

        Assert.Equal("call_returned", Level(block));
        var effect = Aspect(block, "effect");
        Assert.Contains("zero deltas", effect["reason"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // move_body / scale_body
    // ------------------------------------------------------------------

    [Fact]
    public void MoveBody_BBoxShiftedAsExpected_GeometryChecked()
    {
        var p = new JsonObject { ["x"] = 1.0, ["y"] = 2.0, ["z"] = 3.0 };
        var block = VerificationBuilder.Build(null, "move_body", p, new JsonObject(),
            Snap(1, 4.0, new FusionPoint(0, 0, 0), new FusionPoint(2, 2, 2)),
            Snap(1, 4.0, new FusionPoint(1, 2, 3), new FusionPoint(3, 4, 5)));

        Assert.Equal("geometry_checked", Level(block));
        // The scene-bbox caveat is always named.
        Assert.Contains(block["unverified_aspects"]!.AsArray(), n =>
            n!["name"]!.GetValue<string>() == "scene_bbox");
    }

    [Fact]
    public void MoveBody_BBoxDidNotShift_IsNamed()
    {
        var p = new JsonObject { ["x"] = 1.0 };
        var block = VerificationBuilder.Build(null, "move_body", p, new JsonObject(),
            Snap(1, 4.0, new FusionPoint(0, 0, 0), new FusionPoint(2, 2, 2)),
            Snap(1, 4.0, new FusionPoint(0, 0, 0), new FusionPoint(2, 2, 2)));

        Assert.Equal("call_returned", Level(block));
        var aspect = Aspect(block, "bbox_shift");
        Assert.Contains("did not shift", aspect["reason"]!.GetValue<string>());
    }

    [Fact]
    public void ScaleBody_BBoxScaled_GeometryChecked()
    {
        var p = new JsonObject { ["scale"] = 2.0 };
        var block = VerificationBuilder.Build(null, "scale_body", p, new JsonObject(),
            Snap(1, 4.0, new FusionPoint(0, 0, 0), new FusionPoint(1, 1, 1)),
            Snap(1, 32.0, new FusionPoint(0, 0, 0), new FusionPoint(2, 2, 2)));

        Assert.Equal("geometry_checked", Level(block));
    }

    [Fact]
    public void ScaleBody_AnchoredAtOrigin_IsHonoured()
    {
        var p = new JsonObject { ["scale"] = 2.0, ["anchor_x"] = 1.0 };
        // x: 1 + (0-1)·2 = -1 … 1 + (1-1)·2 = 1; y/z around anchor 0: 0…2.
        var block = VerificationBuilder.Build(null, "scale_body", p, new JsonObject(),
            Snap(1, 4.0, new FusionPoint(0, 0, 0), new FusionPoint(1, 1, 1)),
            Snap(1, 32.0, new FusionPoint(-1, 0, 0), new FusionPoint(1, 2, 2)));

        Assert.Equal("geometry_checked", Level(block));
    }

    // ------------------------------------------------------------------
    // boolean_operation — the existing BOOLEAN_NO_OP becomes a named check
    // ------------------------------------------------------------------

    [Fact]
    public void Boolean_JoinConsumesTheToolBody_Existence()
    {
        var block = VerificationBuilder.Build(null, "boolean_operation", new JsonObject(), new JsonObject(),
            Snap(2, 10.0), Snap(1, 8.0));

        Assert.Equal("existence_checked", Level(block));
        var checks = block["checks"]!.AsArray();
        Assert.True(checks[0]!["passed"]!.GetValue<bool>(), "boolean_applied");
        Assert.True(checks[1]!["passed"]!.GetValue<bool>(), "body_count_delta");
    }

    [Fact]
    public void Boolean_NoOp_IsNamedLoudly()
    {
        var block = VerificationBuilder.Build(null, "boolean_operation", new JsonObject(), new JsonObject(),
            Snap(2, 10.0), Snap(2, 10.0));

        Assert.Equal("call_returned", Level(block));
        var effect = Aspect(block, "effect");
        Assert.Contains("BOOLEAN_NO_OP", effect["reason"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // create_* primitives
    // ------------------------------------------------------------------

    [Fact]
    public void CreateBox_BodyExists_ExistenceWithNamedGeometryLimitation()
    {
        var block = VerificationBuilder.Build(null, "create_box", new JsonObject(), new JsonObject(),
            Snap(0, 0.0), Snap(1, 4.0));

        Assert.Equal("existence_checked", Level(block));
        var aspect = Aspect(block, "geometry");
        Assert.Contains("not verified beyond existence", aspect["reason"]!.GetValue<string>());
    }

    [Fact]
    public void CreateCylinder_BodyNotGained_IsNamed()
    {
        var block = VerificationBuilder.Build(null, "create_cylinder", new JsonObject(), new JsonObject(),
            Snap(1, 4.0), Snap(1, 4.0));

        Assert.Equal("call_returned", Level(block));
        Assert.Contains(block["unverified_aspects"]!.AsArray(), n =>
            n!["name"]!.GetValue<string>() == "body_exists");
    }

    // ------------------------------------------------------------------
    // export_stl — KOMPAS G5 pattern
    // ------------------------------------------------------------------

    private static byte[] BinaryStl(params double[][] triangles)
    {
        // Each triangle is 9 doubles: 3 vertices × 3 components.
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write(new byte[80]);
        writer.Write((uint)triangles.Length);
        foreach (var tri in triangles)
        {
            writer.Write(0.0f); writer.Write(0.0f); writer.Write(0.0f); // normal
            for (var i = 0; i < 9; i++)
            {
                writer.Write((float)tri[i]);
            }

            writer.Write((ushort)0);
        }

        return stream.ToArray();
    }

    [Fact]
    public void ExportStl_BinaryFileMatchesBodyBBox_GeometryChecked()
    {
        var ctx = new FakeFusionContext();
        AddBody(ctx, "Box1", 1.0, new FusionPoint(0, 0, 0), new FusionPoint(2, 1, 0));

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "f3d_verify_" + Guid.NewGuid().ToString("N")));
        try
        {
            var file = Path.Combine(dir.FullName, "out.stl");
            File.WriteAllBytes(file, BinaryStl(new[]
            {
                new[] { 0.0, 0.0, 0.0, 2.0, 0.0, 0.0, 0.0, 1.0, 0.0 },
            }));

            var result = new JsonObject { ["file_path"] = file };
            var p = new JsonObject { ["body_name"] = "Box1" };
            var block = VerificationBuilder.Build(ctx, "export_stl", p, result, null, null);

            Assert.Equal("geometry_checked", Level(block));
            var names = block["checks"]!.AsArray().Select(n => n!["name"]!.GetValue<string>()).ToList();
            Assert.Contains("file_exists", names);
            Assert.Contains("byte_length", names);
            Assert.Contains("triangle_count", names);
            Assert.Contains("mesh_bbox_vs_body_bbox", names);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExportStl_MissingFile_IsNamed()
    {
        var ctx = new FakeFusionContext();
        var result = new JsonObject { ["file_path"] = "/nonexistent/path.stl" };
        var block = VerificationBuilder.Build(ctx, "export_stl", new JsonObject { ["body_name"] = "Box1" }, result, null, null);

        Assert.Equal("call_returned", Level(block));
        var aspect = Aspect(block, "export");
        Assert.Contains("not on disk", aspect["reason"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // default (call_returned + a named reason per command)
    // ------------------------------------------------------------------

    [Fact]
    public void Default_Mutation_WithDeltas_SaysWhatWasNotChecked()
    {
        var result = new JsonObject { ["deltas"] = new JsonObject() };
        var block = VerificationBuilder.Build(null, "mirror", new JsonObject(), result, null, null);

        Assert.Equal("call_returned", Level(block));
        Assert.True(block["checks"]!.AsArray().Single()!["passed"]!.GetValue<bool>(), "deltas_recorded");
        var aspect = Aspect(block, "geometry");
        Assert.Contains("mirror", aspect["reason"]!.GetValue<string>());
        var scene = Aspect(block, "scene");
        Assert.Contains("unavailable", scene["reason"]!.GetValue<string>());
    }

    [Fact]
    public void Default_UnknownCommand_UsesTheFallbackReason()
    {
        var block = VerificationBuilder.Build(null, "some_future_tool", new JsonObject(), new JsonObject(), null, Snap(1, 1.0));
        Assert.Equal("call_returned", Level(block));
        Assert.Contains("no analytic expected geometry", Aspect(block, "geometry")["reason"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // The P1 insertion point: CommandHandler auto-attaches the block
    // ------------------------------------------------------------------

    [Fact]
    public void CommandHandler_Mutation_AutoAttachesTheVerificationBlock()
    {
        var ctx = new FakeFusionContext();
        ctx.SnapshotSequence.Add(Snap(0, 0.0));
        ctx.SnapshotSequence.Add(Snap(1, 4.0));

        var frame = new JsonObject { ["type"] = "create_box" };
        frame["params"] = JsonNode.Parse("""{"length": 2, "width": 1, "height": 2}""")!;
        var result = new CommandHandler(ctx).ExecuteCommand(frame).Root!["result"]!.AsObject();

        Assert.True(result["ok"]!.GetValue<bool>());
        Assert.NotNull(result["deltas"]);
        var block = result["verification"]!.AsObject();
        Assert.Equal("existence_checked", Level(block));
    }

    [Fact]
    public void CommandHandler_NonMutation_HasNoBlock()
    {
        var ctx = new FakeFusionContext();
        var frame = new JsonObject { ["type"] = "ping" };
        frame["params"] = new JsonObject();
        var result = new CommandHandler(ctx).ExecuteCommand(frame).Root["result"]!.AsObject();

        Assert.False(result.ContainsKey("verification"));
    }
}
