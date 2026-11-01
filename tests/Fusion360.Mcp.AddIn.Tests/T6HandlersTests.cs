using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fusion360.Mcp.AddIn;
using Xunit;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// Behavioural tests for the T6 command handlers (features, body ops,
/// direct primitives, surface ops) against the in-memory
/// <see cref="FakeFusionContext" />. Each test drives the real
/// <see cref="CommandHandler" /> dispatch so the full ok-marker / mutation-
/// delta / error-classification path is exercised.
/// </summary>
public class T6HandlersTests
{
    // ------------------------------------------------------------------
    // Frame + dispatch helpers
    // ------------------------------------------------------------------

    private static JsonNode Frame(string type, string? paramsJson = null)
    {
        var root = new JsonObject { ["type"] = type };
        root["params"] = paramsJson is null ? new JsonObject() : JsonNode.Parse(paramsJson)!;
        return root;
    }

    private static JsonObject Run(FakeFusionContext ctx, string type, string? paramsJson = null)
        => new CommandHandler(ctx).ExecuteCommand(Frame(type, paramsJson)).Root["result"]!.AsObject();

    // ------------------------------------------------------------------
    // Fixture builders
    // ------------------------------------------------------------------

    private static FakeBody AddBox(FakeFusionContext ctx, string name = "Box1")
    {
        var body = new FakeBody
        {
            Name = name,
            Volume = 8,
            Area = 16,
            BoundingBox = new FakeBBox(new FusionPoint(0, 0, 0), new FusionPoint(2, 2, 1)),
        };

        // Four vertical edges (start/end at opposite Z).
        var corners = new[]
        {
            new FusionPoint(0, 0, 0),
            new FusionPoint(2, 0, 0),
            new FusionPoint(2, 2, 0),
            new FusionPoint(0, 2, 0),
        };
        for (var i = 0; i < corners.Length; i++)
        {
            var c = corners[i];
            body.EdgeList.Add(new FakeBodyEdge
            {
                Label = $"vertical{i}",
                PointOnEdge = new FusionPoint(c.X, c.Y, 0.5),
                StartVertex = c,
                EndVertex = new FusionPoint(c.X, c.Y, 1),
            });
        }

        // Top edges at Z = 1, bottom edges at Z = 0.
        foreach (var i in new[] { 0, 1, 2, 3 })
        {
            body.EdgeList.Add(new FakeBodyEdge
            {
                Label = $"top{i}",
                PointOnEdge = new FusionPoint(0, 0, 1),
                StartVertex = new FusionPoint(0, 0, 1),
                EndVertex = new FusionPoint(2, 0, 1),
            });
        }

        foreach (var i in new[] { 0, 1, 2, 3 })
        {
            body.EdgeList.Add(new FakeBodyEdge
            {
                Label = $"bottom{i}",
                PointOnEdge = new FusionPoint(0, 0, 0),
                StartVertex = new FusionPoint(0, 0, 0),
                EndVertex = new FusionPoint(2, 0, 0),
            });
        }

        body.FaceList.Add(new FakeFace
        {
            Label = "top",
            Area = 4,
            BoundingBox = new FakeBBox(new FusionPoint(0, 0, 1), new FusionPoint(2, 2, 1)),
            PointOnFace = new FusionPoint(1, 1, 1),
            PlaneGeometry = new FakePlane { Normal = new FusionPoint(0, 0, 1), Origin = new FusionPoint(0, 0, 1) },
        });
        body.FaceList[0].Normals.Add(new FusionPoint(0, 0, 1));

        // Four side faces (vertical, planar).
        body.FaceList.Add(new FakeFace
        {
            Label = "side",
            Area = 2,
            BoundingBox = new FakeBBox(new FusionPoint(0, 0, 0), new FusionPoint(2, 0, 1)),
            PointOnFace = new FusionPoint(1, 0, 0.5),
            PlaneGeometry = new FakePlane { Normal = new FusionPoint(0, -1, 0), Origin = new FusionPoint(0, 0, 0) },
        });
        body.FaceList[1].Normals.Add(new FusionPoint(0, -1, 0));

        ctx.Design.RootComponent.AddBody(body);
        return body;
    }

    private static FakeSketch AddSketch(FakeFusionContext ctx, string name = "Sketch1")
    {
        var sketch = new FakeSketch { Name = name, ProfilesCount = 1 };
        sketch.ProfilesImpl.Profiles.Add(new FakeProfile { Index = 0 });
        ctx.Design.RootComponent.AddSketch(sketch);
        return sketch;
    }

    // ------------------------------------------------------------------
    // Features
    // ------------------------------------------------------------------

    [Fact]
    public void Extrude_DistanceExtent_RecordsParameters()
    {
        var ctx = new FakeFusionContext();
        AddSketch(ctx);

        var r = Run(ctx, "extrude", """{"height": 5, "direction": "negative", "operation": "join"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Extrude.Inputs.Single();
        Assert.True(input.Reverse);
        Assert.Equal(5, input.DistanceCm);
        Assert.Equal(FusionFeatureOperation.Join, input.Operation);
        Assert.Equal("Extrude1", r["feature_name"]!.GetValue<string>());
        Assert.Equal("join", r["operation"]!.GetValue<string>());
    }

    [Fact]
    public void Extrude_Symmetric_UsesSymmetricExtent()
    {
        var ctx = new FakeFusionContext();
        AddSketch(ctx);

        Run(ctx, "extrude", """{"height": 3, "direction": "symmetric"}""");

        var input = ctx.Design.RootComponent.FeaturesImpl.Extrude.Inputs.Single();
        Assert.True(input.Symmetric);
        Assert.True(input.Mirror);
        Assert.Equal(3, input.DistanceCm);
    }

    [Fact]
    public void Extrude_NoProfiles_Fails()
    {
        var ctx = new FakeFusionContext();
        AddSketch(ctx);
        ctx.Design.RootComponent.Sketches[0].ProfilesCount = 0;

        var r = Run(ctx, "extrude");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("No profiles in sketch", r["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void Revolve_CanonicalAxis_UsesConstructionAxis()
    {
        var ctx = new FakeFusionContext();
        AddSketch(ctx);

        // Default axis direction is X, so the handler must pick the X
        // construction axis; pass a Z direction to exercise the Z branch.
        var r = Run(ctx, "revolve", """{"angle": 120, "axis_direction_x": 0, "axis_direction_z": 1}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Revolve.Inputs.Single();
        Assert.Same(ctx.Design.RootComponent.ZAxis, input.Axis);
        Assert.Equal(120, input.AngleDegrees);
    }

    [Fact]
    public void Revolve_ArbitraryAxis_CreatesConstructionLine()
    {
        var ctx = new FakeFusionContext();
        var sketch = AddSketch(ctx);

        var r = Run(ctx, "revolve", """{"angle": 360, "axis_direction_x": 1, "axis_direction_y": 1}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var curve = (FakeSketchCurve)sketch.CurvesList.Single();
        Assert.True(curve.IsConstruction);
        var input = ctx.Design.RootComponent.FeaturesImpl.Revolve.Inputs.Single();
        Assert.Same(curve, input.Axis);
    }

    [Fact]
    public void Sweep_CreatesPathFromPathSketch()
    {
        var ctx = new FakeFusionContext();
        AddSketch(ctx, "ProfileSketch");
        var pathSketch = AddSketch(ctx, "PathSketch");
        var pathCurve = new FakeSketchCurve { Label = "line" };
        pathSketch.CurvesList.Add(pathCurve);

        var r = Run(ctx, "sweep", """{"profile_sketch_name": "ProfileSketch", "path_sketch_name": "PathSketch"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(1, ctx.Design.RootComponent.FeaturesImpl.CreatePathCalls);
        Assert.Same(pathCurve, ctx.Design.RootComponent.FeaturesImpl.LastPathCurve);
        var input = ctx.Design.RootComponent.FeaturesImpl.Sweep.Inputs.Single();
        // The path is an opaque IFusionPath built from the curve.
        Assert.IsType<FakePath>(input.Path);
    }

    [Fact]
    public void Loft_AddsSectionPerSketch()
    {
        var ctx = new FakeFusionContext();
        AddSketch(ctx, "S1");
        AddSketch(ctx, "S2");

        var r = Run(ctx, "loft", """{"profile_sketch_names": ["S1", "S2"]}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(2, r["profile_count"]!.GetValue<int>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Loft.Inputs.Single();
        Assert.Equal(2, input.Sections.Count);
    }

    [Fact]
    public void Fillet_VerticalEdges_PassesEdgeSet()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        var r = Run(ctx, "fillet", """{"radius": 0.2, "body_name": "Box1", "edge_selection": "vertical"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(4, r["edges_count"]!.GetValue<int>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Fillet.Inputs.Single();
        Assert.Equal(0.2, input.RadiusCm);
        Assert.True(input.IncludeAdjacent);
        Assert.Equal(4, input.Edges!.Count);
    }

    [Fact]
    public void Chamfer_EqualDistance_RecordsParameters()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        var r = Run(ctx, "chamfer", """{"distance": 0.1, "body_name": "Box1", "edge_selection": "top"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Chamfer.Inputs.Single();
        Assert.Equal(0.1, input.DistanceCm);
        Assert.True(input.IncludeAdjacent);
    }

    [Fact]
    public void Shell_RemovesTopFaces()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        var r = Run(ctx, "shell", """{"thickness": 0.5, "body_name": "Box1"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Shell.Inputs.Single();
        Assert.Equal(0.5, input.InsideThicknessCm);
        // Both fixture faces reach the top Z (a side face's box spans the
        // full height — the reference's known quirk), so both match "top".
        Assert.Equal(2, input.FacesToRemove.Count);
        Assert.Equal(2, r["faces_removed"]!.GetValue<int>());
    }

    [Fact]
    public void Mirror_UsesRequestedPlane()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        var r = Run(ctx, "mirror", """{"mirror_plane": "yz", "body_name": "Box1"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Mirror.Inputs.Single();
        Assert.Same(ctx.Design.RootComponent.YZ, input.Plane);
    }

    [Fact]
    public void CreateHole_OnTopFace_PassesDiameterNotRadius()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        var r = Run(ctx, "create_hole", """{"diameter": 1, "depth": 2, "body_name": "Box1", "center_x": 1, "center_y": 1}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var holeInput = ctx.Design.RootComponent.FeaturesImpl.Hole.Inputs.Single();
        Assert.Equal(1, holeInput.RequestedDiameterCm);
        Assert.Equal(2, holeInput.DepthCm);
        Assert.Single(holeInput.ParticipantBodies);
        Assert.Same(holeInput.PositionPoint, ctx.Design.RootComponent.Sketches[0].AddedPoints.Single());
        Assert.Equal("Box1", r["cut_bodies"]!.AsArray().Single()!.GetValue<string>());
        Assert.Equal(1, r["actual_diameter"]!.GetValue<double>());
        // The top face sits at Z = 1: the plane solver must report it.
        var resolved = (JsonArray)r["resolved_center"]!;
        Assert.Equal(1, resolved[0]!.GetValue<double>());
        Assert.Equal(1, resolved[1]!.GetValue<double>());
        Assert.Equal(1, resolved[2]!.GetValue<double>());
    }

    [Fact]
    public void CreateHole_NoHorizontalFace_Fails()
    {
        var ctx = new FakeFusionContext();
        var body = AddBox(ctx);
        foreach (var face in body.FaceList)
        {
            face.PlaneGeometry = null;
        }

        var r = Run(ctx, "create_hole", """{"diameter": 1, "body_name": "Box1"}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("No near-horizontal", r["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void RectangularPattern_RecordsTwoDirections()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        var r = Run(ctx, "rectangular_pattern", """{"body_name": "Box1", "x_count": 3, "x_spacing": 1.5, "y_count": 2, "y_spacing": 2}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.RectPattern.Inputs.Single();
        Assert.Equal(3, input.XCount);
        Assert.Equal(1.5, input.XSpacingCm);
        Assert.Equal(2, input.YCount);
        Assert.Equal(2, input.YSpacingCm);
        Assert.Same(ctx.Design.RootComponent.YAxis, input.YAxis);
    }

    [Fact]
    public void CircularPattern_RecordsQuantityAndAngle()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        var r = Run(ctx, "circular_pattern", """{"body_name": "Box1", "count": 8, "axis": "x", "total_angle": 180}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.CirclePattern.Inputs.Single();
        Assert.Equal(8, input.Quantity);
        Assert.Equal(180, input.TotalAngleDegrees);
        Assert.Same(ctx.Design.RootComponent.XAxis, input.Axis);
    }

    [Fact]
    public void CreateThread_SetsThreadDataAndLength()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        var r = Run(ctx, "create_thread", """{"body_name": "Box1", "thread_type": "UNC", "is_full_length": false, "thread_length": 1.2}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var thread = ctx.Design.RootComponent.FeaturesImpl.Thread;
        Assert.Equal("UNC", thread.ThreadDataQueryImpl.ThreadType);
        var input = thread.Inputs.Single();
        Assert.Equal(1.2, input.ThreadLengthCm);
        Assert.False(input.IsFullLength);
        Assert.Equal("UNC", r["thread_type"]!.GetValue<string>());
    }

    [Fact]
    public void DraftFaces_VerticalFaces_PassesAngle()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        var r = Run(ctx, "draft_faces", """{"body_name": "Box1", "angle": 15, "face_selection": "vertical"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(1, r["face_count"]!.GetValue<int>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Draft.Inputs.Single();
        Assert.Equal(15, input.AngleDegrees);
        Assert.False(input.TwoSided);
        Assert.Same(ctx.Design.RootComponent.XY, input.PullPlane);
    }

    [Fact]
    public void SplitBody_ByPlane()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        var r = Run(ctx, "split_body", """{"body_name": "Box1", "splitting_plane": "xz"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.SplitBody.Inputs.Single();
        Assert.Same(ctx.Design.RootComponent.XZ, input.Plane);
        Assert.True(input.ExtendTool);
    }

    [Fact]
    public void SplitBody_ByBody()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "Box1");
        AddBox(ctx, "Tool");

        Run(ctx, "split_body", """{"body_name": "Box1", "splitting_body": "Tool", "extend_tool": false}""");

        var input = ctx.Design.RootComponent.FeaturesImpl.SplitBody.Inputs.Single();
        Assert.Equal("Tool", input.ToolBody?.Name);
        Assert.Same(ctx.Design.RootComponent.Bodies[0], input.Body);
        Assert.False(input.ExtendTool);
    }

    [Fact]
    public void SplitFace_WithIndices()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        Run(ctx, "split_face", """{"body_name": "Box1", "face_indices": [0, 1], "splitting_plane": "xy"}""");

        var input = ctx.Design.RootComponent.FeaturesImpl.SplitFace.Inputs.Single();
        Assert.Equal(2, input.Faces!.Count);
        Assert.Same(ctx.Design.RootComponent.XY, input.Plane);
    }

    [Fact]
    public void OffsetFaces_WithIndices()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        Run(ctx, "offset_faces", """{"body_name": "Box1", "distance": 0.3, "face_indices": [0]}""");

        var input = ctx.Design.RootComponent.FeaturesImpl.Offset.Inputs.Single();
        Assert.Single(input.Faces!);
        Assert.Equal(0.3, input.DistanceCm);
        Assert.Equal(FusionFeatureOperation.NewBody, input.Operation);
    }

    [Fact]
    public void ScaleBody_Uniform()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        Run(ctx, "scale_body", """{"body_name": "Box1", "scale": 2}""");

        var input = ctx.Design.RootComponent.FeaturesImpl.Scale.Inputs.Single();
        Assert.Equal(2, input.UniformScale);
        Assert.Equal("Scale1", input.Result.Name);
    }

    [Fact]
    public void ScaleBody_Axis()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        Run(ctx, "scale_body", """{"body_name": "Box1", "scale_x": 2, "scale_y": 1, "scale_z": 0.5}""");

        var input = ctx.Design.RootComponent.FeaturesImpl.Scale.Inputs.Single();
        var expected = new FusionPoint(2, 1, 0.5);
        Assert.Equal(expected, input.AxisScale);
    }

    [Fact]
    public void SuppressFeature_TogglesTimelineItem()
    {
        var ctx = new FakeFusionContext();
        var item = new FakeTimelineItem { Name = "Extrude1", Entity = new FakeTimelineEntity { Name = "Extrude1" } };
        ctx.Design.Timeline = new FakeTimeline { Items = { item } };

        var r = Run(ctx, "suppress_feature", """{"feature_name": "Extrude1"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.True(r["suppressed"]!.GetValue<bool>());
        Assert.True(item.IsSuppressed);

        var r2 = Run(ctx, "unsuppress_feature", """{"feature_name": "Extrude1"}""");
        Assert.True(r2["unsuppressed"]!.GetValue<bool>());
        Assert.False(item.IsSuppressed);
    }

    [Fact]
    public void SuppressFeature_NotInTimeline_Fails()
    {
        var ctx = new FakeFusionContext();
        ctx.Design.Timeline = new FakeTimeline();

        var r = Run(ctx, "suppress_feature", """{"feature_name": "Missing"}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("not found in timeline", r["error_message"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Body operations
    // ------------------------------------------------------------------

    [Fact]
    public void MoveBody_Translates()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        var r = Run(ctx, "move_body", """{"body_name": "Box1", "x": 1, "y": 2, "z": 3}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Move.Inputs.Single();
        var translation = new FusionPoint(1, 2, 3);
        Assert.Equal(translation, input.TranslationCm);
        Assert.Equal("Move1", r["feature_name"]!.GetValue<string>());
    }

    [Fact]
    public void BooleanOperation_Cut_SetsOperation()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "Target");
        AddBox(ctx, "Tool");

        var r = Run(ctx, "boolean_operation", """{"target_body": "Target", "tool_body": "Tool", "operation": "cut"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Combine.Inputs.Single();
        Assert.Equal("Target", input.Target?.Name);
        Assert.Equal(FusionFeatureOperation.Cut, input.Operation);
    }

    [Fact]
    public void BooleanOperation_UnknownOp_Fails()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "Target");
        AddBox(ctx, "Tool");

        var r = Run(ctx, "boolean_operation", """{"target_body": "Target", "tool_body": "Tool", "operation": "union"}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("Unknown boolean op", r["error_message"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Direct primitives (temporary BRep manager)
    // ------------------------------------------------------------------

    private static void AssertPrimitivePipeline(FakeFusionContext ctx)
    {
        var root = ctx.Design.RootComponent;
        // Each primitive goes under a base feature with start/finish edit.
        var baseFeature = root.FeaturesImpl.Base.Added.Single();
        Assert.Equal(1, baseFeature.StartEditCalls);
        Assert.Equal(1, baseFeature.FinishEditCalls);
        var body = Assert.Single(root.Bodies);
        Assert.True(body.AddedUnderBaseFeature);
    }

    [Fact]
    public void CreateBox_AddsBodyUnderBaseFeature()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_box", """{"length": 2, "width": 1, "height": 0.5}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        AssertPrimitivePipeline(ctx);
        var manager = ctx.Design.RootComponent.BRepManagerImpl;
        Assert.Equal(1, manager.CreateBoxCalls);
        // The box base rests on the Z plane: centre Z is shifted by h/2.
        Assert.Equal(0.25, manager.LastBoxCenter!.Value.Z);
    }

    [Fact]
    public void CreateCylinder_AddsBody()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_cylinder", """{"radius": 1, "height": 3}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        AssertPrimitivePipeline(ctx);
        Assert.Equal(1, ctx.Design.RootComponent.BRepManagerImpl.CreateCylinderCalls);
    }

    [Fact]
    public void CreateSphere_AddsBody()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_sphere", """{"radius": 1.5, "center_z": 2}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        AssertPrimitivePipeline(ctx);
        Assert.Equal(1, ctx.Design.RootComponent.BRepManagerImpl.CreateSphereCalls);
    }

    [Fact]
    public void CreateTorus_AddsBody()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_torus", """{"major_radius": 2, "minor_radius": 0.5}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        AssertPrimitivePipeline(ctx);
        Assert.Equal(1, ctx.Design.RootComponent.BRepManagerImpl.CreateTorusCalls);
    }

    // ------------------------------------------------------------------
    // Surface operations
    // ------------------------------------------------------------------

    [Fact]
    public void PatchSurface_SetsContinuity()
    {
        var ctx = new FakeFusionContext();
        AddSketch(ctx);

        var r = Run(ctx, "patch_surface", """{"sketch_name": "Sketch1", "continuity": "tangent"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Patch.Inputs.Single();
        Assert.Equal(FusionSurfaceContinuity.Tangent, input.BoundaryContinuity);
        Assert.Equal(FusionFeatureOperation.NewBody, input.Operation);
    }

    [Fact]
    public void StitchSurfaces_PassesBodiesAndTolerance()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "A");
        AddBox(ctx, "B");

        var r = Run(ctx, "stitch_surfaces", """{"body_names": ["A", "B"], "tolerance": 0.02}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(2, r["body_count"]!.GetValue<int>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Stitch.Inputs.Single();
        Assert.Equal(0.02, input.ToleranceCm);
        Assert.Equal(2, input.Bodies!.Count);
    }

    [Fact]
    public void ThickenSurface_PassesFaces()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx);

        var r = Run(ctx, "thicken_surface", """{"body_name": "Box1", "thickness": 0.4}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Thicken.Inputs.Single();
        Assert.Equal(0.4, input.ThicknessCm);
        Assert.True(input.Symmetric);
        Assert.Equal(ctx.Design.RootComponent.Bodies[0].FaceList.Count, input.Faces!.Count);
    }

    [Fact]
    public void RuledSurface_PassesEdgeAndDistance()
    {
        var ctx = new FakeFusionContext();
        var body = AddBox(ctx);

        var r = Run(ctx, "ruled_surface", """{"body_name": "Box1", "edge_index": 0, "distance": 2}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Ruled.Inputs.Single();
        Assert.Same(body.EdgeList[0], input.Edge);
        Assert.Equal(2, input.DistanceCm);
    }

    [Fact]
    public void TrimSurface_PassesBodyAndTool()
    {
        var ctx = new FakeFusionContext();
        var body = AddBox(ctx, "Box1");
        AddBox(ctx, "Tool");

        var r = Run(ctx, "trim_surface", """{"body_name": "Box1", "tool_name": "Tool"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Trim.Inputs.Single();
        Assert.Same(body, input.Body);
        Assert.Equal("Tool", input.Tool?.Name);
    }
}
