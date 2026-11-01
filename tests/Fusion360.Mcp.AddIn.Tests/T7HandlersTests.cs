using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fusion360.Mcp.AddIn;
using Xunit;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// Behavioural tests for the T7 command handlers (sheet metal, construction
/// geometry, assembly, inspection, mesh, appearance, parameters, export,
/// CAM, rename_body / create_box_parametric) against the in-memory
/// <see cref="FakeFusionContext" />. Each test drives the real
/// <see cref="CommandHandler" /> dispatch so the full ok-marker / error-
/// classification path is exercised, matching the T5/T6 test style.
/// </summary>
public class T7HandlersTests
{
    // ------------------------------------------------------------------
    // Frame + dispatch helpers
    // ------------------------------------------------------------------

    private static JsonNode Frame(string type, JsonObject? p)
    {
        var root = new JsonObject { ["type"] = type };
        root["params"] = p ?? new JsonObject();
        return root;
    }

    private static JsonObject Run(FakeFusionContext ctx, string type, string? paramsJson)
        => Run(ctx, type, paramsJson is null ? null : JsonNode.Parse(paramsJson)!.AsObject());

    private static JsonObject Run(FakeFusionContext ctx, string type, JsonObject? p)
        => new CommandHandler(ctx).ExecuteCommand(Frame(type, p)).Root["result"]!.AsObject();

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "t7-helpers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ------------------------------------------------------------------
    // Fixture builders
    // ------------------------------------------------------------------

    private static FakeBody AddBox(FakeFusionContext ctx, string name = "Box1")
    {
        var body = new FakeBody { Name = name, Volume = 8, Area = 16 };

        var corners = new[]
        {
            new FusionPoint(0, 0, 0),
            new FusionPoint(2, 0, 0),
            new FusionPoint(2, 2, 0),
            new FusionPoint(0, 2, 0),
        };
        foreach (var c in corners)
        {
            body.EdgeList.Add(new FakeBodyEdge
            {
                PointOnEdge = new FusionPoint(c.X, c.Y, 0.5),
                StartVertex = c,
                EndVertex = new FusionPoint(c.X, c.Y, 1),
            });
        }

        body.FaceList.Add(new FakeFace
        {
            Label = "top",
            PlaneGeometry = new FakePlane { Normal = new FusionPoint(0, 0, 1), Origin = new FusionPoint(0, 0, 1) },
        });
        body.FaceList.Add(new FakeFace
        {
            Label = "side",
            PlaneGeometry = new FakePlane { Normal = new FusionPoint(0, -1, 0), Origin = new FusionPoint(0, 0, 0) },
        });

        ctx.Design.RootComponent.AddBody(body);
        return body;
    }

    private static FakeSketch AddSketchWithLine(FakeFusionContext ctx, string name)
    {
        var sketch = new FakeSketch { Name = name, ProfilesCount = 1 };
        sketch.CurvesImpl.Lines.AddByTwoPoints(new FusionPoint(0, 0, 0), new FusionPoint(5, 0, 0));
        ctx.Design.RootComponent.AddSketch(sketch);
        return sketch;
    }

    private static FakeOccurrence AddComponent(FakeFusionContext ctx, string name)
    {
        var occ = (FakeOccurrence)ctx.Design.RootComponent.Occurrences.AddNewComponent();
        occ.Component.Name = name;
        return occ;
    }

    private static FakeCamProduct AddCam(FakeFusionContext ctx, string setupName = "MILL", string opName = "OP1")
    {
        var cam = new FakeCamProduct();
        ctx.CamProduct = cam;
        var setup = (FakeCamSetup)cam.Setups.Add(cam.Setups.CreateInput(FusionCamOperationType.Milling));
        setup.Name = setupName;
        var op = (FakeCamOperation)setup.Operations.Add(setup.Operations.CreateInput("face_milling"))!;
        op.Name = opName;
        op.HasToolpath = true;
        return cam;
    }

    // ------------------------------------------------------------------
    // Bodies
    // ------------------------------------------------------------------

    [Fact]
    public void RenameBody_Renames_AndEchoes()
    {
        var ctx = new FakeFusionContext();
        var body = AddBox(ctx, "Box1");

        var r = Run(ctx, "rename_body", """{"body_name": "Box1", "new_name": "Cube"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.True(r["renamed"]!.GetValue<bool>());
        Assert.Equal("Box1", r["old_name"]!.GetValue<string>());
        Assert.Equal("Cube", r["new_name"]!.GetValue<string>());
        Assert.Equal("Cube", body.Name);
    }

    [Fact]
    public void RenameBody_MissingBody_Fails()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "rename_body", """{"body_name": "nope", "new_name": "x"}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("not found", r["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CreateBoxParametric_NumericDimensions_RecordsSketchAndExtrude()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_box_parametric",
            """{"length": 5, "width": 4, "height": 3, "origin_x": 1, "origin_y": 2}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.True(r["created"]!.GetValue<bool>());
        Assert.Equal("Extrude1", r["body_name"]!.GetValue<string>());
        Assert.Equal("Extrude1", r["feature_name"]!.GetValue<string>());
        Assert.Equal("Sketch1", r["sketch_name"]!.GetValue<string>());
        Assert.Equal(5, r["length"]!.GetValue<double>());
        Assert.Equal(4, r["width"]!.GetValue<double>());
        Assert.Equal(3, r["height"]!.GetValue<double>());

        var sketch = ctx.Design.RootComponent.Sketches.Single();
        var dims = sketch.DimensionsImpl.Created;
        Assert.Equal(2, dims.Count);
        Assert.Equal(5, dims[0].ParameterValue);
        Assert.Equal("endpoint_distance:2", dims[0].Type); // horizontal (length)
        Assert.Equal(4, dims[1].ParameterValue);
        Assert.Equal("endpoint_distance:3", dims[1].Type); // vertical (width)

        var extrude = ctx.Design.RootComponent.FeaturesImpl.Extrude.Inputs.Single();
        Assert.Equal(3, extrude.DistanceCm);
        Assert.Null(extrude.DistanceExpression);
    }

    [Fact]
    public void CreateBoxParametric_ExpressionDimensions_DriveDimensionsAndExtrude()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_box_parametric",
            """{"length": "boxL", "width": "boxW", "height": "boxH"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("boxL", r["length"]!.GetValue<string>());
        Assert.Equal("boxW", r["width"]!.GetValue<string>());
        Assert.Equal("boxH", r["height"]!.GetValue<string>());

        var dims = ctx.Design.RootComponent.Sketches.Single().DimensionsImpl.Created;
        Assert.Equal("boxL", dims[0].ParameterExpression);
        Assert.Equal("boxW", dims[1].ParameterExpression);

        var extrude = ctx.Design.RootComponent.FeaturesImpl.Extrude.Inputs.Single();
        Assert.Equal("boxH", extrude.DistanceExpression);
        Assert.Null(extrude.DistanceCm);
    }

    [Fact]
    public void CreateBoxParametric_MissingDimensions_Fails()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_box_parametric", """{"length": 5}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("length/width/height are required", r["error_message"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Export
    // ------------------------------------------------------------------

    [Fact]
    public void ExportStl_RecordsTarget_AndDefaultPath()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "Box1");
        var dir = TempDir();
        var path = Path.Combine(dir, "Box1.stl");

        var r = Run(ctx, "export_stl", $"{{\"body_name\": \"Box1\", \"file_path\": \"{path}\"}}");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.True(r["exported"]!.GetValue<bool>());
        Assert.Equal(path, r["file_path"]!.GetValue<string>());
        Assert.Equal(path, ctx.Design.ExportManagerImpl.StlExports.Single());
    }

    [Fact]
    public void ExportStl_InAssembly_HidesSiblingsAndExportsOccurrence()
    {
        var ctx = new FakeFusionContext();
        var occ = AddComponent(ctx, "Part");
        var bolt = new FakeBody { Name = "bolt", AssemblyContext = occ };
        var nut = new FakeBody { Name = "nut", AssemblyContext = occ };
        occ.BodyList.Add(bolt);
        occ.BodyList.Add(nut);
        var path = Path.Combine(TempDir(), "bolt.stl");

        var r = Run(ctx, "export_stl", $"{{\"body_name\": \"bolt\", \"file_path\": \"{path}\"}}");

        Assert.True(r["ok"]!.GetValue<bool>());
        // The occurrence (not the body) is the export target.
        Assert.Same(occ, ctx.Design.ExportManagerImpl.StlTargets.Single());
        // Siblings are restored after the export.
        Assert.True(nut.IsVisible);
        Assert.True(bolt.IsVisible);
    }

    [Fact]
    public void ExportStep_RecordsTarget()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "Box1");
        var path = Path.Combine(TempDir(), "Box1.step");

        var r = Run(ctx, "export_step", $"{{\"body_name\": \"Box1\", \"file_path\": \"{path}\"}}");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(path, ctx.Design.ExportManagerImpl.StepExports.Single());
    }

    [Fact]
    public void ExportF3d_RecordsArchive()
    {
        var ctx = new FakeFusionContext();
        var path = Path.Combine(TempDir(), "design.f3d");

        var r = Run(ctx, "export_f3d", $"{{\"file_path\": \"{path}\"}}");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(path, ctx.Design.ExportManagerImpl.FusionArchiveExports.Single());
    }

    [Fact]
    public void Export_DispatchesByFormatAndExtension()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "Box1");
        var dir = TempDir();

        var stlPath = Path.Combine(dir, "a.stl");
        var stepPath = Path.Combine(dir, "a.step");
        var f3dPath = Path.Combine(dir, "a.f3d");

        var stl = Run(ctx, "export", $"{{\"format\": \"stl\", \"body_name\": \"Box1\", \"file_path\": \"{stlPath}\"}}");
        Assert.True(stl["ok"]!.GetValue<bool>());
        var step = Run(ctx, "export", $"{{\"format\": \"step\", \"body_name\": \"Box1\", \"file_path\": \"{stepPath}\"}}");
        Assert.True(step["ok"]!.GetValue<bool>());
        var f3d = Run(ctx, "export", $"{{\"file_path\": \"{f3dPath}\"}}");
        Assert.True(f3d["ok"]!.GetValue<bool>());
        Assert.Single(ctx.Design.ExportManagerImpl.StlExports);
        Assert.Single(ctx.Design.ExportManagerImpl.StepExports);
        Assert.Single(ctx.Design.ExportManagerImpl.FusionArchiveExports);
    }

    [Fact]
    public void Export_UnknownOrMissingFormat_Fails()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "Box1");

        var unknown = Run(ctx, "export", """{"format": "iges", "body_name": "Box1"}""");
        Assert.False(unknown["ok"]!.GetValue<bool>());
        Assert.Contains("Unknown format", unknown["error_message"]!.GetValue<string>());

        var none = Run(ctx, "export", "{}");
        Assert.False(none["ok"]!.GetValue<bool>());
        Assert.Contains("Specify format", none["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void ExportViewSheet_RendersViews_WritesSheetAndRestoresCamera()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "Box1");
        var cam = ctx.Viewport!.CameraImpl;
        cam.Eye = new FusionPoint(5, 5, 5);
        cam.Target = new FusionPoint(1, 1, 1);
        cam.UpVector = new FusionPoint(0, 0, 1);
        cam.CameraType = 0;

        var dir = TempDir();
        var r = Run(ctx, "export_view_sheet", new JsonObject
        {
            ["views"] = new JsonArray("iso", "top"),
            ["image_size"] = new JsonArray(640, 480),
            ["output_dir"] = dir,
        });

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(2, r["views"]!.AsArray().Count);
        Assert.Equal("iso", r["views"]!.AsArray()[0]!["view"]!.GetValue<string>());

        var htmlPath = r["html_path"]!.GetValue<string>();
        Assert.True(File.Exists(htmlPath));
        Assert.Contains("data:image/png;base64", File.ReadAllText(htmlPath));

        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")))!;
        Assert.Equal(2, manifest["views"]!.AsArray().Count);
        Assert.Equal(htmlPath, manifest["html_path"]!.GetValue<string>());

        Assert.Equal(2, ctx.Viewport!.SaveAsImageCalls);
        Assert.True(ctx.Viewport.RefreshCalls >= 3);

        // Camera restored to the pre-sheet state.
        Assert.Equal(5, cam.Eye.X);
        Assert.Equal(5, cam.Eye.Y);
        Assert.Equal(5, cam.Eye.Z);
        Assert.Equal(0, cam.CameraType);
    }

    [Fact]
    public void ExportViewSheet_UnknownView_Fails()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "export_view_sheet",
            new JsonObject { ["views"] = new JsonArray("iso", "weird"), ["output_dir"] = TempDir() });

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("Unknown views", r["error_message"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Mesh
    // ------------------------------------------------------------------

    [Fact]
    public void ImportMesh_RecordsImportWithUnitMapping()
    {
        var ctx = new FakeFusionContext();
        var path = Path.Combine(TempDir(), "scan.stl");
        File.WriteAllBytes(path, new byte[] { 1 });

        var r = Run(ctx, "import_mesh", $"{{\"file_path\": \"{path}\", \"units\": \"cm\"}}");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("scan", r["mesh_name"]!.GetValue<string>());
        Assert.Equal("cm", r["units"]!.GetValue<string>());
        Assert.Equal((path, FusionMeshUnits.Centimeter), ctx.Design.RootComponent.MeshBodies.Imports.Single());
    }

    [Fact]
    public void ImportMesh_MissingFile_OrUnknownUnits_Fail()
    {
        var ctx = new FakeFusionContext();
        var missing = Run(ctx, "import_mesh", """{"file_path": "/does/not/exist.stl"}""");
        Assert.False(missing["ok"]!.GetValue<bool>());
        Assert.Contains("Mesh file not found", missing["error_message"]!.GetValue<string>());

        var path = Path.Combine(TempDir(), "x.stl");
        File.WriteAllBytes(path, new byte[] { 1 });
        var bad = Run(ctx, "import_mesh", $"{{\"file_path\": \"{path}\", \"units\": \"yd\"}}");
        Assert.False(bad["ok"]!.GetValue<bool>());
        Assert.Contains("Unknown units", bad["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CompareMeshes_ReportsDeviationStats()
    {
        var ctx = new FakeFusionContext();
        var root = ctx.Design.RootComponent;
        var deviations = new List<double> { 0.1, -0.2, 0.3 };
        root.MeshBodies.Bodies.Add(new FakeMeshBody { Name = "a", ComparisonResult = deviations });
        root.MeshBodies.Bodies.Add(new FakeMeshBody { Name = "b" });

        var r = Run(ctx, "compare_meshes", """{"mesh_name_a": "a", "mesh_name_b": "b"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(3, r["node_count"]!.GetValue<int>());
        Assert.Equal(-0.2, r["min_deviation"]!.GetValue<double>());
        Assert.Equal(0.3, r["max_deviation"]!.GetValue<double>());
        // Same arithmetic order as the handler, so the doubles match exactly.
        Assert.Equal(deviations.Average(Math.Abs), r["mean_abs_deviation"]!.GetValue<double>());
        Assert.Equal(Math.Sqrt(deviations.Sum(d => d * d) / deviations.Count), r["rms_deviation"]!.GetValue<double>());
        Assert.Equal(0.3, r["max_abs_deviation"]!.GetValue<double>());
        Assert.Equal("cm", r["units"]!.GetValue<string>());
    }

    [Fact]
    public void CompareMeshes_MissingMesh_Fails()
    {
        var ctx = new FakeFusionContext();
        ctx.Design.RootComponent.MeshBodies.Bodies.Add(new FakeMeshBody { Name = "a" });

        var r = Run(ctx, "compare_meshes", """{"mesh_name_a": "a", "mesh_name_b": "z"}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("not found in root component", r["error_message"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Construction geometry
    // ------------------------------------------------------------------

    [Fact]
    public void CreateConstructionPlane_OffsetMethod_AddsPlane()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_construction_plane", """{"method": "offset", "plane": "xz", "offset": 5}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("CPlane1", r["name"]!.GetValue<string>());
        Assert.Equal("offset", r["method"]!.GetValue<string>());
        Assert.Single(ctx.Design.RootComponent.ConstructionPlanes.Planes);
    }

    [Fact]
    public void CreateConstructionPlane_ThreePoints_RequiresAllThree()
    {
        var ctx = new FakeFusionContext();

        var ok = Run(ctx, "create_construction_plane",
            """{"method": "three_points", "point_one": [0,0,0], "point_two": [3,0,0], "point_three": [0,3,0]}""");
        Assert.True(ok["ok"]!.GetValue<bool>());
        Assert.Equal("three_points", ok["method"]!.GetValue<string>());

        var missing = Run(ctx, "create_construction_plane",
            """{"method": "three_points", "point_one": [0,0,0]}""");
        Assert.False(missing["ok"]!.GetValue<bool>());
        Assert.Contains("three_points requires", missing["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CreateConstructionPlane_TangentOrUnknownMethod_Fails()
    {
        var ctx = new FakeFusionContext();

        var tangent = Run(ctx, "create_construction_plane", """{"method": "tangent"}""");
        Assert.False(tangent["ok"]!.GetValue<bool>());
        Assert.Contains("requires selecting a face", tangent["error_message"]!.GetValue<string>());

        var unknown = Run(ctx, "create_construction_plane", """{"method": "bogus"}""");
        Assert.Contains("Unknown method", unknown["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CreateConstructionAxis_TwoPointsAndEdgeMethods()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "Box1");

        var r = Run(ctx, "create_construction_axis",
            """{"method": "two_points", "point_one": [0,0,0], "point_two": [2,0,0]}""");
        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("CAxis1", r["name"]!.GetValue<string>());

        var edge = Run(ctx, "create_construction_axis", """{"method": "edge", "body_name": "Box1", "edge_index": 1}""");
        Assert.True(edge["ok"]!.GetValue<bool>());
        Assert.Equal("CAxis2", edge["name"]!.GetValue<string>());

        var unknown = Run(ctx, "create_construction_axis", """{"method": "bogus"}""");
        Assert.Contains("Unknown method", unknown["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CreateUcs_CreatesHiddenReferenceSketchAndUcs()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_ucs",
            """{"name": "top_face", "x": 1, "y": 2, "z": 0, "angle_z": 30}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("top_face", r["name"]!.GetValue<string>());
        Assert.Equal("UCS_top_face_ref", r["reference_sketch"]!.GetValue<string>());
        var origin = r["origin"]!.AsArray();
        Assert.Equal(1, origin[0]!.GetValue<double>());
        Assert.Equal(2, origin[1]!.GetValue<double>());

        var root = ctx.Design.RootComponent;
        var sketch = root.Sketches[root.Sketches.Count - 1];
        Assert.Equal("UCS_top_face_ref", sketch.Name);
        Assert.False(sketch.IsVisible);
        // z = 0 stays on the XY construction plane — no offset plane created.
        Assert.Empty(root.ConstructionPlanes.Planes);

        var input = root.UserCoordinateSystems.CreatedInputs.Single();
        Assert.Equal(Math.PI * 30.0 / 180.0, input.AngleZRadians);
        Assert.Null(input.AngleXRadians);
        Assert.Same(sketch.AddedPoints.Single(), root.LastUcsGeometryPoint);
    }

    [Fact]
    public void CreateUcs_NonZeroZ_UsesOffsetPlane()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_ucs", """{"name": "lifted", "z": 5}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var root = ctx.Design.RootComponent;
        Assert.Single(root.ConstructionPlanes.Planes);
        var sketch = root.Sketches[root.Sketches.Count - 1];
        Assert.Equal("CPlane1", sketch.HostPlane);
    }

    // ------------------------------------------------------------------
    // Assembly
    // ------------------------------------------------------------------

    [Fact]
    public void CreateComponent_AddsNamedComponent()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_component", """{"name": "Part"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("Part", r["name"]!.GetValue<string>());
        var occ = ctx.Design.RootComponent.Occurrences.Created.Single();
        Assert.Equal("Part", occ.Component.Name);
    }

    [Fact]
    public void AddJoint_RigidType_SetsRigidMotion()
    {
        var ctx = new FakeFusionContext();
        AddComponent(ctx, "A");
        AddComponent(ctx, "B");

        var r = Run(ctx, "add_joint", """{"component_one": "A", "component_two": "B"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("rigid", r["joint_type"]!.GetValue<string>());
        var joint = ctx.Design.RootComponent.Joints.Added.Single();
        Assert.True(joint.Input!.RigidJointMotion);
    }

    [Fact]
    public void AddJoint_UnknownTypeOrMissingComponent_Fails()
    {
        var ctx = new FakeFusionContext();
        AddComponent(ctx, "A");

        var bad = Run(ctx, "add_joint", """{"component_one": "A", "component_two": "A", "joint_type": "mystery"}""");
        Assert.False(bad["ok"]!.GetValue<bool>());
        Assert.Contains("Unknown joint type", bad["error_message"]!.GetValue<string>());

        var missing = Run(ctx, "add_joint", """{"component_one": "A", "component_two": "Z"}""");
        Assert.Contains("not found", missing["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CreateAsBuiltJoint_SucceedsOrFailsOnMissingComponent()
    {
        var ctx = new FakeFusionContext();
        var occA = AddComponent(ctx, "A");
        var occB = AddComponent(ctx, "B");

        var r = Run(ctx, "create_as_built_joint", """{"component_one": "A", "component_two": "B"}""");
        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.AsBuiltJoints.Added.Single().Input!;
        Assert.Same(occA, input.OccurrenceOne);
        Assert.Same(occB, input.OccurrenceTwo);

        var missing = Run(ctx, "create_as_built_joint", """{"component_one": "A", "component_two": "Z"}""");
        Assert.Contains("not found", missing["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CreateRigidGroup_GroupsComponents()
    {
        var ctx = new FakeFusionContext();
        AddComponent(ctx, "A");
        AddComponent(ctx, "B");

        var r = Run(ctx, "create_rigid_group",
            """{"component_names": ["A", "B"], "include_children": false}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(2, r["component_count"]!.GetValue<int>());
        var group = ctx.Design.RootComponent.RigidGroups.AddedGroups.Single();
        Assert.Equal(2, group.Count);
        Assert.False(ctx.Design.RootComponent.RigidGroups.IncludeChildrens.Single());
    }

    [Fact]
    public void CreateRigidGroup_SingleComponent_Fails()
    {
        var ctx = new FakeFusionContext();
        AddComponent(ctx, "A");

        var r = Run(ctx, "create_rigid_group", """{"component_names": ["A"]}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("Need at least 2 components", r["error_message"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Inspection
    // ------------------------------------------------------------------

    [Fact]
    public void MeasureDistance_BodiesAndPoints()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "BoxA");

        var bodies = Run(ctx, "measure_distance", """{"entity_one": "BoxA", "entity_two": "BoxA"}""");
        Assert.True(bodies["ok"]!.GetValue<bool>());
        Assert.Equal(1.0, bodies["distance"]!.GetValue<double>());
        // One measureMinimumDistance call per command (reference parity).
        Assert.Single(ctx.MeasureManagerImpl.MeasureDistanceCalls);

        var points = Run(ctx, "measure_distance",
            """{"entity_one": "1,2,3", "entity_two": "4,2,3"}""");
        Assert.True(points["ok"]!.GetValue<bool>());
        var call = ctx.MeasureManagerImpl.MeasureDistanceCalls[ctx.MeasureManagerImpl.MeasureDistanceCalls.Count - 1];
        Assert.IsType<FusionMeasurePoint>(call.One);
        Assert.IsType<FusionMeasurePoint>(call.Two);
    }

    [Fact]
    public void MeasureDistance_UnknownEntity_Fails()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "BoxA");

        var r = Run(ctx, "measure_distance", """{"entity_one": "BoxA", "entity_two": "ghost"}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("'ghost' not found", r["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void MeasureAngle_BodiesReportDegrees()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "BoxA");
        AddBox(ctx, "BoxB");

        var r = Run(ctx, "measure_angle", """{"entity_one": "BoxA", "entity_two": "BoxB"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        // The fake reports pi/2 radians; same conversion order as the handler.
        Assert.Equal(Math.PI / 2 * 180.0 / Math.PI, r["angle_degrees"]!.GetValue<double>());
    }

    [Fact]
    public void GetPhysicalProperties_MapsAccuracy()
    {
        var ctx = new FakeFusionContext();
        var body = AddBox(ctx, "Box1");
        body.Volume = 8;
        body.Area = 16;

        var r = Run(ctx, "get_physical_properties", """{"body_name": "Box1", "accuracy": "very_high"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(8, r["volume"]!.GetValue<double>());
        Assert.Equal(16, r["area"]!.GetValue<double>());
        Assert.Equal(1, r["mass"]!.GetValue<double>());
        Assert.Equal(FusionCalculationAccuracy.VeryHigh, body.GetPhysicalPropertiesCalls.Single());
    }

    [Fact]
    public void CreateSectionAnalysis_RecordsPlaneAndOffset()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_section_analysis", """{"plane": "yz", "offset": 2.5}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var input = ctx.Design.RootComponent.Analyses.Added.Single();
        Assert.Same(ctx.Design.RootComponent.YZ, input.Plane);
        Assert.Equal(2.5, input.DistanceCm);
    }

    [Fact]
    public void CheckInterference_AnalyzesBodiesAcrossComponents()
    {
        var ctx = new FakeFusionContext();
        var occA = AddComponent(ctx, "A");
        var occB = AddComponent(ctx, "B");
        occA.BodyList.Add(new FakeBody { Name = "a-body" });
        occB.BodyList.Add(new FakeBody { Name = "b-body" });
        ctx.Design.InterferenceResults = new List<IFusionInterferenceResult>
        {
            new FakeInterferenceResult { BodyOne = "a-body", BodyTwo = "b-body", InterferenceVolumeCm3 = 1.5 },
        };

        var r = Run(ctx, "check_interference",
            """{"component_names": ["A", "B"], "include_coincident_faces": true}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(1, r["count"]!.GetValue<int>());
        Assert.Equal("a-body", r["interferences"]!.AsArray()[0]!["body_one"]!.GetValue<string>());
        Assert.Equal(1.5, r["interferences"]!.AsArray()[0]!["volume"]!.GetValue<double>());
        var input = ctx.Design.LastInterferenceInput!;
        Assert.True(input.IncludeCoincidentFaces);
        Assert.Equal(2, input.Bodies.Count);
    }

    [Fact]
    public void CheckInterference_NeedTwoBodiedComponents()
    {
        var ctx = new FakeFusionContext();
        var occA = AddComponent(ctx, "A");
        occA.BodyList.Add(new FakeBody { Name = "a-body" });
        AddComponent(ctx, "B"); // no bodies

        var r = Run(ctx, "check_interference", """{"component_names": ["A", "B"]}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("Need at least 2 components with bodies", r["error_message"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Appearance
    // ------------------------------------------------------------------

    [Fact]
    public void SetAppearance_CopiesLibraryAppearanceOntoTarget()
    {
        var ctx = new FakeFusionContext();
        var body = AddBox(ctx, "Box1");

        var r = Run(ctx, "set_appearance",
            """{"target_name": "Box1", "appearance_name": "Steel"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.True(r["applied"]!.GetValue<bool>());
        var local = (FakeAppearance)body.Appearance!;
        Assert.Equal("Steel", local.Name);
        Assert.Contains("Steel", ctx.Design.AppearancesImpl.Added);
    }

    [Fact]
    public void SetAppearance_MissingAppearance_Fails()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "Box1");

        var r = Run(ctx, "set_appearance",
            """{"target_name": "Box1", "appearance_name": "Gold"}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("Appearance 'Gold' not found", r["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void SetColor_ClampsAndCreatesMcpAppearance()
    {
        var ctx = new FakeFusionContext();
        var body = AddBox(ctx, "Box1");

        var r = Run(ctx, "set_color", """{"body_name": "Box1", "red": 300, "green": -5, "blue": 200, "opacity": 0.5}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        // 300→255, -5→0, 0.5*255=127.5→128.
        Assert.Equal("MCP_255_0_200_128", r["appearance"]!.GetValue<string>());
        var appearance = (FakeAppearance)body.Appearance!;
        var color = appearance.LastColor!.Value;
        Assert.Equal(255, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(200, color.B);
        Assert.Equal(128, color.A);
        Assert.Equal(128 / 255.0, r["opacity"]!.GetValue<double>());

        // Same color again reuses the existing appearance.
        Run(ctx, "set_color", """{"body_name": "Box1", "red": 300, "green": -5, "blue": 200, "opacity": 0.5}""");
        Assert.Single(ctx.Design.AppearancesImpl.Added);
        Assert.Same(appearance, body.Appearance);
    }

    // ------------------------------------------------------------------
    // Parameters
    // ------------------------------------------------------------------

    [Fact]
    public void CreateParameter_NormalizesUnitAndBuildsExpression()
    {
        var ctx = new FakeFusionContext();

        var r = Run(ctx, "create_parameter", """{"name": "width", "value": 5, "unit": "mm"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("mm", r["unit"]!.GetValue<string>());
        Assert.Equal("5 mm", r["expression"]!.GetValue<string>());
        var added = ctx.Design.UserParameterList.Single();
        Assert.Equal("width", added.Name);
        Assert.Equal("5 mm", added.Expression);
    }

    [Fact]
    public void SetParameter_WritesExpressionInExistingUnit()
    {
        var ctx = new FakeFusionContext();
        var param = ctx.Design.UserParameters.Add("height", 10, "cm", null);

        var r = Run(ctx, "set_parameter", """{"name": "height", "value": 7}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("cm", r["unit"]!.GetValue<string>());
        Assert.Equal("7 cm", r["expression"]!.GetValue<string>());
        Assert.Equal("7 cm", param.Expression);

        var missing = Run(ctx, "set_parameter", """{"name": "nope", "value": 1}""");
        Assert.Contains("Parameter 'nope' not found", missing["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void DeleteParameter_RemovesParameter()
    {
        var ctx = new FakeFusionContext();
        var param = ctx.Design.UserParameters.Add("temp", 1, null, null);

        var r = Run(ctx, "delete_parameter", """{"name": "temp"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Same(param, ctx.Design.UserParameters.Deleted.Single());
        Assert.Empty(ctx.Design.UserParameterList);

        var missing = Run(ctx, "delete_parameter", """{"name": "ghost"}""");
        Assert.Contains("Parameter 'ghost' not found", missing["error_message"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Sheet metal
    // ------------------------------------------------------------------

    [Fact]
    public void CreateFlange_RecordsFeatureParameters()
    {
        var ctx = new FakeFusionContext();
        var body = AddBox(ctx, "Box1");

        var r = Run(ctx, "create_flange",
            """{"body_name": "Box1", "edge_index": 0, "height": 2.5, "angle": 45, "bend_radius": 0.125}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("Flange1", r["feature_name"]!.GetValue<string>());
        Assert.Equal(45, r["angle"]!.GetValue<double>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Flange.Inputs.Single();
        Assert.Same(body.EdgeList[0], input.Edge);
        Assert.Equal(45, input.AngleDegrees);
        Assert.Equal(2.5, input.HeightCm);
        Assert.Equal(0.125, input.BendRadiusCm);
    }

    [Fact]
    public void CreateBend_UsesSketchLine_OrFailsWithoutSketch()
    {
        var ctx = new FakeFusionContext();
        var body = AddBox(ctx, "Box1");
        var sketch = AddSketchWithLine(ctx, "BendSketch1");
        var line = sketch.CurvesImpl.Lines.Created.Single();

        var r = Run(ctx, "create_bend",
            """{"body_name": "Box1", "bend_line_sketch": "BendSketch1", "angle": 120}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("Bend1", r["feature_name"]!.GetValue<string>());
        var input = ctx.Design.RootComponent.FeaturesImpl.Bend.Inputs.Single();
        Assert.Same(body, input.Body);
        Assert.Same(line, input.BendLine);
        Assert.Equal(120, input.BendAngleDegrees);

        var missing = Run(ctx, "create_bend", """{"body_name": "Box1"}""");
        Assert.Contains("bend_line_sketch is required", missing["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void FlatPattern_CreatesFeature()
    {
        var ctx = new FakeFusionContext();
        var body = AddBox(ctx, "Box1");

        var r = Run(ctx, "flat_pattern", """{"body_name": "Box1"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("FlatPattern1", r["feature_name"]!.GetValue<string>());
        var flat = ctx.Design.RootComponent.FeaturesImpl.FlatPattern;
        Assert.Same(body, flat.LastBody);
        Assert.Single(flat.Added);
    }

    [Fact]
    public void Unfold_PicksFirstPlanarFaceAsStationary()
    {
        var ctx = new FakeFusionContext();
        var body = AddBox(ctx, "Box1");

        var all = Run(ctx, "unfold", """{"body_name": "Box1"}""");
        Assert.True(all["ok"]!.GetValue<bool>());
        var unfold = ctx.Design.RootComponent.FeaturesImpl.Unfold;
        Assert.Equal(body.FaceList.Count, unfold.LastBends!.Count);
        Assert.Same(body.FaceList[0], unfold.LastStationary);

        var indexed = Run(ctx, "unfold", """{"body_name": "Box1", "bend_indices": [1]}""");
        Assert.True(indexed["ok"]!.GetValue<bool>());
        var recorded = unfold.LastBends!.Single();
        Assert.Same(body.FaceList[1], recorded);
    }

    [Fact]
    public void Unfold_NoPlanarFace_Fails()
    {
        var ctx = new FakeFusionContext();
        var body = new FakeBody { Name = "Shell1" };
        body.FaceList.Add(new FakeFace { Label = "curved" }); // no PlaneGeometry
        ctx.Design.RootComponent.AddBody(body);

        var r = Run(ctx, "unfold", """{"body_name": "Shell1"}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("No planar face found for stationary face", r["error_message"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // CAM
    // ------------------------------------------------------------------

    [Fact]
    public void CamListSetups_ReportsSetupsAndOperations()
    {
        var ctx = new FakeFusionContext();
        var cam = AddCam(ctx, "MILL", "OP1");
        ((FakeCamSetup)cam.Setups.Add(cam.Setups.CreateInput(FusionCamOperationType.Turning))).Name = "DRILL";

        var r = Run(ctx, "cam_list_setups", "{}");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(2, r["count"]!.GetValue<int>());
        Assert.Equal("MILL", r["setups"]!.AsArray()[0]!["name"]!.GetValue<string>());
        var ops = r["setups"]!.AsArray()[0]!["operations"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains("OP1", ops);
    }

    [Fact]
    public void CamCommands_WithoutCamProduct_Fail()
    {
        var ctx = new FakeFusionContext(); // CamProduct stays null

        var r = Run(ctx, "cam_list_setups", "{}");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("No CAM workspace found", r["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CamListOperations_ReportsOps_OrFailsOnMissingSetup()
    {
        var ctx = new FakeFusionContext();
        AddCam(ctx, "MILL", "OP1");

        var r = Run(ctx, "cam_list_operations", """{"setup_name": "MILL"}""");
        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(1, r["count"]!.GetValue<int>());
        Assert.True(r["operations"]!.AsArray()[0]!["has_toolpath"]!.GetValue<bool>());

        var missing = Run(ctx, "cam_list_operations", """{"setup_name": "GHOST"}""");
        Assert.Contains("Setup 'GHOST' not found", missing["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CamGetOperationInfo_IncludesToolAndReadableParameters()
    {
        var ctx = new FakeFusionContext();
        var cam = AddCam(ctx, "MILL", "OP1");
        var op = (FakeCamOperation)cam.Setups.Item(0).Operations.Item(0);
        op.Tool = new FakeCamToolInfo { Description = "1/4 flat end" };
        op.ParametersList.Add(new FakeCamParameter { Name = "tool_feedCutting", Expression = "100 mm/min" });
        op.ParametersList.Add(new FakeCamParameter { Name = "broken", ExpressionFails = true });

        var r = Run(ctx, "cam_get_operation_info", """{"setup_name": "MILL", "operation_name": "OP1"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("1/4 flat end", r["tool"]!["description"]!.GetValue<string>());
        Assert.Equal("100 mm/min", r["parameters"]!["tool_feedCutting"]!.GetValue<string>());
        Assert.Null(r["parameters"]!["broken"]);
    }

    [Fact]
    public void CamCreateSetup_AppliesStockParameters()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "Box1");
        var cam = AddCam(ctx);

        var r = Run(ctx, "cam_create_setup",
            """{"body_name": "Box1", "name": "MILL2", "stock_offset_sides": 1, "stock_offset_top": 2}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        var setup = (FakeCamSetup)cam.Setups.Setups.Single(s => s.Name == "MILL2");
        Assert.Equal("'default'", setup.SetParameters["job_stockMode"]);
        Assert.Equal("1", setup.SetParameters["job_stockOffset"]);
        Assert.Equal("2", setup.SetParameters["job_stockOffsetTop"]);
        Assert.Equal("default", r["stock_applied"]!["stock_mode"]!.GetValue<string>());
    }

    [Fact]
    public void CamCreateSetup_UnknownOperationType_Fails()
    {
        var ctx = new FakeFusionContext();
        AddBox(ctx, "Box1");
        AddCam(ctx);

        var r = Run(ctx, "cam_create_setup", """{"body_name": "Box1", "operation_type": "drilling"}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("Unknown operation_type", r["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CamCreateOperation_AppliesSupportedParameters()
    {
        var ctx = new FakeFusionContext();
        var cam = AddCam(ctx, "MILL", "OP1");

        var r = Run(ctx, "cam_create_operation",
            """{"setup_name": "MILL", "strategy": "face_milling", "tool_number": 7, "stepdown": 0.5, "coolant": "dry"}""");

        Assert.True(r["ok"]!.GetValue<bool>());
        // The setup already holds "OP1" (from AddCam), so the fake's
        // counter-based auto-name yields "Op2" for the handler-created op.
        Assert.Equal("Op2", r["name"]!.GetValue<string>());
        var setup = cam.Setups.Item(0);
        var op = (FakeCamOperation)setup.Operations.Item(setup.Operations.Count - 1);
        Assert.Equal("7", op.SetParameters["tool_number"]);
        Assert.Equal("0.5", op.SetParameters["maximumStepdown"]);
        Assert.Equal("'dry'", op.SetParameters["tool_coolant"]);
        Assert.Equal(7, r["parameters_applied"]!["tool_number"]!.GetValue<int>());
    }

    [Fact]
    public void CamCreateOperation_ToolDiameterIsRejected()
    {
        var ctx = new FakeFusionContext();
        AddCam(ctx, "MILL", "OP1");

        var r = Run(ctx, "cam_create_operation",
            """{"setup_name": "MILL", "strategy": "face_milling", "tool_diameter": 0.5}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("tool_diameter cannot be set", r["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CamCreateOperation_RejectedStrategy_Fails()
    {
        var ctx = new FakeFusionContext();
        var cam = AddCam(ctx, "MILL", "OP1");
        var setup = (FakeCamSetup)cam.Setups.Item(0);
        setup.Operations.AddReturnsNull = true;

        var r = Run(ctx, "cam_create_operation", """{"setup_name": "MILL", "strategy": "bogus"}""");

        Assert.False(r["ok"]!.GetValue<bool>());
        Assert.Contains("Fusion rejected strategy", r["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CamGenerateToolpath_Scopes()
    {
        var ctx = new FakeFusionContext();
        var cam = AddCam(ctx, "MILL", "OP1");
        var setup = cam.Setups.Item(0);
        var op = setup.Operations.Item(0);

        var all = Run(ctx, "cam_generate_toolpath", """{"generate_all": true}""");
        Assert.True(all["ok"]!.GetValue<bool>());
        Assert.Equal("all", all["scope"]!.GetValue<string>());
        Assert.False(cam.GenerateAllCalls.Single());

        var opScope = Run(ctx, "cam_generate_toolpath", """{"setup_name": "MILL", "operation_name": "OP1"}""");
        Assert.Equal("operation", opScope["scope"]!.GetValue<string>());
        Assert.Same(op, cam.GenerateToolpathTargets.Last());

        var setupScope = Run(ctx, "cam_generate_toolpath", """{"setup_name": "MILL"}""");
        Assert.Equal("setup", setupScope["scope"]!.GetValue<string>());
        Assert.Same(setup.Operations, cam.GenerateToolpathTargets.Last());

        var none = Run(ctx, "cam_generate_toolpath", "{}");
        Assert.Contains("Provide setup_name", none["error_message"]!.GetValue<string>());
    }

    [Fact]
    public void CamGenerateToolpath_PumpsFusionEventsWhileFutureRuns()
    {
        var ctx = new FakeFusionContext();
        var cam = AddCam(ctx, "MILL", "OP1");
        cam.NextFuture = new FakeCamFuture { CompleteAfterChecks = 2 };

        var result = Run(ctx, "cam_generate_toolpath", """{"setup_name":"MILL"}""");

        Assert.True(result["ok"]!.GetValue<bool>());
        Assert.Equal(2, ctx.DoEventsCount);
    }

    [Fact]
    public void CamPostProcess_ResolvePostConfigPath()
    {
        var ctx = new FakeFusionContext();
        var cam = AddCam(ctx, "MILL", "OP1");
        var dir = TempDir();
        var cps = Path.Combine(dir, "fanuc.cps");
        File.WriteAllBytes(cps, new byte[] { 1 });
        cam.GenericPostFolder = dir;

        var shortName = Run(ctx, "cam_post_process",
            new JsonObject { ["setup_name"] = "MILL", ["post_processor"] = "fanuc" });
        Assert.True(shortName["ok"]!.GetValue<bool>());
        var input = (FakeCamPostInput)cam.PostProcessCalls.Single().Input;
        Assert.Equal(cps, input.PostConfigPath);

        var fullPath = Run(ctx, "cam_post_process",
            new JsonObject { ["setup_name"] = "MILL", ["operation_name"] = "OP1", ["post_processor"] = cps });
        Assert.True(fullPath["ok"]!.GetValue<bool>());
        var opCall = cam.PostProcessCalls[cam.PostProcessCalls.Count - 1];
        Assert.Same(cam.Setups.Item(0).Operations.Item(0), opCall.Target);
    }

    [Fact]
    public void CamPostProcess_NoGenericFolderOrMissingCps_Fails()
    {
        var ctx = new FakeFusionContext();
        AddCam(ctx, "MILL", "OP1"); // GenericPostFolder stays null

        var noFolder = Run(ctx, "cam_post_process",
            new JsonObject { ["setup_name"] = "MILL", ["post_processor"] = "fanuc" });
        Assert.False(noFolder["ok"]!.GetValue<bool>());
        Assert.Contains("no local generic post folder", noFolder["error_message"]!.GetValue<string>());

        var missing = Run(ctx, "cam_post_process",
            new JsonObject { ["setup_name"] = "MILL", ["post_processor"] = "/nonexistent/foo.cps" });
        Assert.Contains("Post processor not found", missing["error_message"]!.GetValue<string>());
    }
}
