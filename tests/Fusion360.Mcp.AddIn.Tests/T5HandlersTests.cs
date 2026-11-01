using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fusion360.Mcp.AddIn;
using Xunit;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// Behavioural tests for the T5 command handlers (scene queries, sketch ops,
/// design-type safety, render_view, delete_all, undo) against
/// the in-memory <see cref="FakeFusionContext" />. Each test drives the real
/// <see cref="CommandHandler" /> dispatch so the full ok-marker / mutation-
/// delta / error-classification path is exercised.
/// </summary>
public class T5HandlersTests
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
    // Scene / query
    // ------------------------------------------------------------------

    [Fact]
    public void GetSceneInfo_ReturnsBodyAndSketchCounts()
    {
        var ctx = new FakeFusionContext();
        ctx.Design.RootComponent.AddBody(new FakeBody { Name = "Body1", Volume = 10, Area = 4, MaterialName = "steel" });
        ctx.Design.RootComponent.AddBody(new FakeBody { Name = "Body2" });
        ctx.Design.RootComponent.AddSketch(new FakeSketch { Name = "Sketch1", ProfilesCount = 2 });

        var r = Run(ctx, "get_scene_info");

        Assert.Equal("Design1.fuse", r["design_name"]!.GetValue<string>());
        Assert.Equal(2, r["bodies_count"]!.GetValue<int>());
        Assert.Equal(1, r["sketches_count"]!.GetValue<int>());
        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("Body1", (r["bodies"] as JsonArray)![0]?["name"]!.GetValue<string>());
        Assert.Equal("Sketch1", (r["sketches"] as JsonArray)![0]?["name"]!.GetValue<string>());
    }

    [Fact]
    public void GetObjectInfo_FindsBodyAndSketchAndReportsMissing()
    {
        var ctx = new FakeFusionContext();
        var body = ctx.Design.RootComponent.AddBody(new FakeBody { Name = "Block", Volume = 100, FacesCount = 6 });
        body.BoundingBox = null;
        var sketch = ctx.Design.RootComponent.AddSketch(new FakeSketch { Name = "Top", ProfilesCount = 1 });

        var bodyInfo = Run(ctx, "get_object_info", """{"name":"Block"}""");
        Assert.True(bodyInfo["found"]!.GetValue<bool>());
        Assert.Equal("body", bodyInfo["type"]!.GetValue<string>());
        Assert.Equal(100, bodyInfo["volume"]!.GetValue<double>());

        var sketchInfo = Run(ctx, "get_object_info", """{"name":"Top"}""");
        Assert.True(sketchInfo["found"]!.GetValue<bool>());
        Assert.Equal("sketch", sketchInfo["type"]!.GetValue<string>());

        var missing = Run(ctx, "get_object_info", """{"name":"nope"}""");
        Assert.False(missing["found"]!.GetValue<bool>());
    }

    [Fact]
    public void GetBoundingBox_ReturnsBodyBoxComponentUnionAndMissing()
    {
        var ctx = new FakeFusionContext();

        // Body path: a root body with a bounding box.
        var body = ctx.Design.RootComponent.AddBody(new FakeBody { Name = "Block" });
        body.BoundingBox = new FakeBBox(new FusionPoint(0, 0, 0), new FusionPoint(10, 4, 2));

        var bodyR = Run(ctx, "get_bounding_box", """{"name":"Block"}""");
        Assert.True(bodyR["found"]!.GetValue<bool>());
        Assert.Equal("body", bodyR["type"]!.GetValue<string>());
        Assert.Equal(10.0, ((JsonArray)bodyR["size"]!)[0]!.GetValue<double>());
        Assert.Equal(5.0, ((JsonArray)bodyR["center"]!)[0]!.GetValue<double>());

        // Component path: union box of contained bodies (name distinct from any body).
        var comp = new FakeComponent { Name = "Widget" };
        var inner = new FakeBody { Name = "Inner" };
        inner.BoundingBox = new FakeBBox(new FusionPoint(-1, -2, -3), new FusionPoint(3, 3, 3));
        comp.BodyList.Add(inner);
        ctx.Design.RootComponent.AddOccurrence(new FakeOccurrence { Name = "Widget1", Component = comp });

        var compR = Run(ctx, "get_bounding_box", """{"name":"Widget"}""");
        Assert.True(compR["found"]!.GetValue<bool>());
        Assert.Equal("component", compR["type"]!.GetValue<string>());
        Assert.Equal(-1.0, ((JsonArray)compR["min"]!)[0]!.GetValue<double>());
        Assert.Equal(3.0, ((JsonArray)compR["max"]!)[0]!.GetValue<double>());

        // Empty component: found but flagged empty.
        var emptyComp = new FakeComponent { Name = "Empty" };
        ctx.Design.RootComponent.AddOccurrence(new FakeOccurrence { Name = "Empty1", Component = emptyComp });
        var emptyR = Run(ctx, "get_bounding_box", """{"name":"Empty"}""");
        Assert.True(emptyR["found"]!.GetValue<bool>());
        Assert.True(emptyR["empty"]!.GetValue<bool>());

        // Missing: neither a body nor a component.
        var missR = Run(ctx, "get_bounding_box", """{"name":"nope"}""");
        Assert.False(missR["found"]!.GetValue<bool>());
    }

    [Fact]
    public void ListComponents_IncludesRootAndOccurrences()
    {
        var ctx = new FakeFusionContext();
        var occ = ctx.Design.RootComponent.AddOccurrence(new FakeOccurrence { Name = "Block1" });
        occ.Component = new FakeComponent { Name = "Block" };

        var r = Run(ctx, "list_components");

        var components = (JsonArray)r["components"]!;
        Assert.Equal(2, r["count"]!.GetValue<int>());
        Assert.True(components[0]?["is_root"]!.GetValue<bool>());
        Assert.Equal("Design", components[0]?["name"]!.GetValue<string>());
        Assert.False(components[1]?["is_root"]!.GetValue<bool>());
        Assert.Equal("Block", components[1]?["name"]!.GetValue<string>());
    }

    [Fact]
    public void GetParameters_ListsUserParameters()
    {
        var ctx = new FakeFusionContext();
        ctx.Design.UserParameterList.Add(new FakeParameter { Name = "width", Value = 5, Unit = "cm", Expression = "2*2.5" });
        ctx.Design.UserParameterList.Add(new FakeParameter { Name = "height", Value = 7 });

        var r = Run(ctx, "get_parameters");

        Assert.Equal(2, r["count"]!.GetValue<int>());
        var params_ = (JsonArray)r["parameters"]!;
        Assert.Equal("width", params_[0]?["name"]!.GetValue<string>());
        Assert.Equal(5, params_[0]?["value"]!.GetValue<double>());
        Assert.Equal("cm", params_[0]?["unit"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Sketch
    // ------------------------------------------------------------------

    [Fact]
    public void CreateSketch_AddsSketchOnRequestedPlane()
    {
        var ctx = new FakeFusionContext();
        var r = Run(ctx, "create_sketch", """{"plane":"xy"}""");

        Assert.Equal("Sketch1", r["sketch_name"]!.GetValue<string>());
        Assert.Equal("xy", r["plane"]!.GetValue<string>());
        Assert.Single(ctx.Design.RootComponent.Sketches);
        Assert.Equal("xy_plane", ctx.Design.RootComponent.Sketches[0].HostPlane);
    }

    [Fact]
    public void CreateSketch_WithZOffset_CreatesOffsetPlane()
    {
        var ctx = new FakeFusionContext();
        var r = Run(ctx, "create_sketch", """{"plane":"xy","z_offset":5}""");

        Assert.Equal(5, r["z_offset"]!.GetValue<double>());
        Assert.Equal(1, ctx.Design.RootComponent.CreateOffsetPlaneCalls);
        Assert.Equal("xy_plane_offset_5", ctx.Design.RootComponent.Sketches[0].HostPlane);
    }

    [Fact]
    public void DrawRectangle_AddsLineCurveToLastSketch()
    {
        var ctx = new FakeFusionContext();
        Run(ctx, "create_sketch", """{"plane":"xy"}""");
        var r = Run(ctx, "draw_rectangle", """{"width":10,"height":4,"origin_x":1,"origin_y":2}""");

        Assert.Equal("Sketch1", r["sketch"]!.GetValue<string>());
        Assert.Equal(10, r["width"]!.GetValue<double>());
        var sketch = ctx.Design.RootComponent.Sketches[0];
        Assert.Single(sketch.CurvesList);
        Assert.Equal("rectangle", sketch.CurvesList[0].Label);
    }

    [Fact]
    public void DrawRectangle_WithoutSketch_Fails()
    {
        var ctx = new FakeFusionContext();
        var r = Run(ctx, "draw_rectangle", """{"width":1,"height":1}""");
        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("No sketch", r["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void DrawCircle_AddsCircleCurveToLastSketch()
    {
        var ctx = new FakeFusionContext();
        Run(ctx, "create_sketch");
        var r = Run(ctx, "draw_circle", """{"radius":3,"center_x":1,"center_y":2}""");

        Assert.Equal("Sketch1", r["sketch"]!.GetValue<string>());
        Assert.Equal(3.0, r["radius"]!.GetValue<double>());
        var sketch = ctx.Design.RootComponent.Sketches[0];
        Assert.Single(sketch.CurvesList);
        Assert.Equal("circle", sketch.CurvesList[0].Label);
    }

    [Fact]
    public void DrawLine_AddsLineCurveToLastSketch()
    {
        var ctx = new FakeFusionContext();
        Run(ctx, "create_sketch");
        var r = Run(ctx, "draw_line", """{"start_x":0,"start_y":0,"end_x":5,"end_y":4}""");

        Assert.Equal("Sketch1", r["sketch"]!.GetValue<string>());
        var sketch = ctx.Design.RootComponent.Sketches[0];
        Assert.Single(sketch.CurvesList);
        Assert.Equal("line", sketch.CurvesList[0].Label);
    }

    [Fact]
    public void AddDimension_CreatesDistanceDimensionOnCurve()
    {
        var ctx = new FakeFusionContext();
        Run(ctx, "create_sketch", """{"plane":"xy"}""");
        Run(ctx, "draw_rectangle", """{"width":10,"height":4}""");
        var r = Run(ctx, "add_dimension",
            """{"dimension_type":"distance","value":5,"entity_one":0,"entity_two":0}""");

        Assert.Equal("Sketch1", r["sketch"]!.GetValue<string>());
        Assert.Equal("distance", r["dimension_type"]!.GetValue<string>());
        var sketch = ctx.Design.RootComponent.Sketches[0];
        Assert.Single(sketch.DimensionsImpl.Created);
        Assert.Equal(5, sketch.DimensionsImpl.Created[0].ParameterValue);
    }

    [Fact]
    public void AddDimension_UnknownType_Fails()
    {
        var ctx = new FakeFusionContext();
        Run(ctx, "create_sketch");
        Run(ctx, "draw_rectangle");
        var r = Run(ctx, "add_dimension", """{"dimension_type":"bogus","value":1}""");
        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("Unknown dimension type", r["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void OffsetCurve_OffsetsLastSketchCurve()
    {
        var ctx = new FakeFusionContext();
        Run(ctx, "create_sketch");
        Run(ctx, "draw_rectangle");
        var r = Run(ctx, "offset_curve", """{"curve_index":0,"offset_distance":2,"direction_x":1}""");

        Assert.Equal(2, r["offset_distance"]!.GetValue<double>());
        var sketch = ctx.Design.RootComponent.Sketches[0];
        Assert.Equal(1, sketch.OffsetCalls);
        Assert.Equal(2, sketch.LastOffsetDistance);
    }

    [Fact]
    public void AutoConstrain_ReturnsResultOrFails()
    {
        var ctx = new FakeFusionContext();
        Run(ctx, "create_sketch");
        var sketch = ctx.Design.RootComponent.Sketches[0];
        sketch.AutoConstrainResult = new FakeAutoConstrainResult { IsFullyConstrained = true, AddedConstraints = 3 };

        var ok = Run(ctx, "auto_constrain", """{}""");
        Assert.True(ok["ok"]!.GetValue<bool>());

        var failCtx = new FakeFusionContext();
        Run(failCtx, "create_sketch");
        failCtx.Design.RootComponent.Sketches[0].AutoConstrainError = "not fully constrained";
        var err = Run(failCtx, "auto_constrain");
        Assert.False(err["ok"]!.GetValue<bool>());
    }

    [Fact]
    public void ProjectGeometry_ProjectsBodyEdgesIntoSketch()
    {
        var ctx = new FakeFusionContext();
        Run(ctx, "create_sketch");
        var body = ctx.Design.RootComponent.AddBody(new FakeBody { Name = "Block" });
        body.EdgeList.Add(new FakeBodyEdge());
        body.EdgeList.Add(new FakeBodyEdge());

        var r = Run(ctx, "project_geometry", """{"source_name":"Block"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("Block", r["source"]!.GetValue<string>());
        Assert.Equal(6, r["projected_curves"]!.GetValue<int>());
    }

    // ------------------------------------------------------------------
    // Design type safety
    // ------------------------------------------------------------------

    [Fact]
    public void GetDesignType_ReturnsCurrentType()
    {
        var ctx = new FakeFusionContext();
        ctx.Design.DesignType = DesignTypes.Direct;
        var r = Run(ctx, "get_design_type");

        Assert.Equal(DesignTypes.Direct, r["design_type_id"]!.GetValue<int>());
        Assert.Equal("direct", r["design_type"]!.GetValue<string>());
    }

    [Fact]
    public void SetDesignType_ChangesType()
    {
        var ctx = new FakeFusionContext();
        var r = Run(ctx, "set_design_type", """{"design_type":"direct"}""");

        Assert.Equal(DesignTypes.Direct, ctx.Design.DesignType);
        Assert.True(r["changed"]!.GetValue<bool>());
        Assert.Equal("direct", r["design_type"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Scene-wide
    // ------------------------------------------------------------------

    [Fact]
    public void DeleteAll_RemovesBodiesSketchesAndOccurrences()
    {
        var ctx = new FakeFusionContext();
        var body = ctx.Design.RootComponent.AddBody(new FakeBody { Name = "Block" });
        var sketch = ctx.Design.RootComponent.AddSketch(new FakeSketch { Name = "S" });
        var occ = ctx.Design.RootComponent.AddOccurrence(new FakeOccurrence { Name = "occ1" });

        var r = Run(ctx, "delete_all");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.True(body.Deleted);
        Assert.True(sketch.Deleted);
        Assert.True(occ.Deleted);
        Assert.Empty(ctx.Design.RootComponent.Bodies);
        Assert.Empty(ctx.Design.RootComponent.Sketches);
        Assert.Empty(ctx.Design.RootComponent.OccurrenceList);
        Assert.Equal(0, r["remaining"]?["bodies"]!.GetValue<int>());
    }

    [Fact]
    public void Undo_ExecutesUndoCommandAndDoEvents()
    {
        var ctx = new FakeFusionContext();
        ctx.Ui.Commands["UndoCommand"] = new FakeUiCommand();

        var r = Run(ctx, "undo");

        Assert.True(r["undone"]!.GetValue<bool>());
        Assert.Equal(DesignTypes.Parametric, r["design_type"]!.GetValue<int>());
        Assert.Equal(1, ctx.Ui.Commands["UndoCommand"].ExecuteCount);
        Assert.Equal(1, ctx.DoEventsCount);
    }

    [Fact]
    public void Undo_WithoutUndoCommand_Fails()
    {
        var ctx = new FakeFusionContext();
        var r = Run(ctx, "undo");
        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("Undo command is not available", r["error_message"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Perception
    // ------------------------------------------------------------------

    [Fact]
    public void RenderView_CurrentView_ReturnsBase64Image()
    {
        var ctx = new FakeFusionContext();
        var r = Run(ctx, "render_view", """{"view":"current","width":64,"height":48}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("current", r["view"]!.GetValue<string>());
        Assert.Equal(64, r["width"]!.GetValue<int>());
        Assert.Equal(48, r["height"]!.GetValue<int>());
        Assert.Equal("png", r["image_format"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), r["image_base64"]!.GetValue<string>());
        Assert.Equal(3, r["bytes"]!.GetValue<int>());
        Assert.True(ctx.Viewport!.FitCalls >= 1);
    }

    [Fact]
    public void RenderView_UnknownView_Fails()
    {
        var ctx = new FakeFusionContext();
        var r = Run(ctx, "render_view", """{"view":"sideways"}""");
        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("Unknown view", r["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void RenderView_SaveFails_Throws()
    {
        var ctx = new FakeFusionContext();
        ctx.Viewport!.SaveAsImageReturns = false;
        var r = Run(ctx, "render_view", """{"view":"current"}""");
        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("saveAsImageFile returned false", r["error_message"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Dispatch guardrails
    // ------------------------------------------------------------------

    [Fact]
    public void UnknownCommand_ReturnsErrorFrame()
    {
        var resp = new CommandHandler(new FakeFusionContext()).ExecuteCommand(Frame("teleport"));
        Assert.Equal("error", resp.Root["status"]!.GetValue<string>());
        Assert.Contains("Unknown command", resp.Root["message"]!.GetValue<string>());
    }

    [Fact]
    public void SceneCommand_WithoutContext_ReturnsNoActiveDesignErrorFrame()
    {
        var resp = new CommandHandler().ExecuteCommand(Frame("get_scene_info"));
        Assert.Equal("error", resp.Root["status"]!.GetValue<string>());
        Assert.Contains("No active design context", resp.Root["message"]!.GetValue<string>());
    }
}
