using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Fusion360.Mcp.Core.Units;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// T7 command handlers: ports of the reference command_handler.py
/// sheet-metal (create_flange/create_bend/flat_pattern/unfold),
/// construction (create_construction_plane/axis, create_ucs), assembly
/// (create_component/add_joint/create_as_built_joint/create_rigid_group),
/// inspection (measure_distance/measure_angle/get_physical_properties/
/// create_section_analysis/check_interference), mesh
/// (import_mesh/compare_meshes), appearance (set_appearance/set_color),
/// parameters (create/set/delete_parameter), export (export_stl/step/f3d,
/// export_view_sheet, unified export), CAM (cam_*), plus
/// rename_body / create_box_parametric. All application errors are
/// <see cref="FusionCommandException"/> with the reference message texts so
/// ErrorClassifier maps them to the same codes.
/// </summary>
public static class T7Handlers
{
    // ------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------

    private static JsonArray PointArrayJson(FusionPoint p)
        => new() { p.X, p.Y, p.Z };
    /// <summary>Python list repr of strings ("['a', 'b']") for reference error parity.</summary>
    private static string PythonList(IReadOnlyCollection<string> items)
        => "[" + string.Join(", ", items.Select(s => $"'{s}'")) + "]";

    /// <summary>Reference <c>os.path.expanduser("~/Desktop")</c>.</summary>
    private static string Desktop()
        => Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    /// <summary>Reference <c>os.makedirs(os.path.dirname(file_path), exist_ok=True)</c>.</summary>
    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>Reference _get_cam error (workspace activation folded into the facade).</summary>
    private static IFusionCamProduct GetCam(IFusionContext ctx)
    {
        var cam = ctx.CamProduct;
        if (cam is null)
        {
            throw new FusionCommandException(
                "No CAM workspace found. Open the Manufacturing workspace " +
                "in Fusion 360 at least once to initialise it.");
        }

        return cam;
    }

    private static IFusionCamSetup FindSetup(IFusionCamProduct cam, string name)
    {
        for (var i = 0; i < cam.Setups.Count; i++)
        {
            var setup = cam.Setups.Item(i);
            if (setup.Name == name)
            {
                return setup;
            }
        }

        throw new FusionCommandException($"Setup '{name}' not found");
    }

    private static IFusionCamOperation FindOperation(IFusionCamSetup setup, string name)
    {
        for (var i = 0; i < setup.Operations.Count; i++)
        {
            var op = setup.Operations.Item(i);
            if (op.Name == name)
            {
                return op;
            }
        }

        throw new FusionCommandException($"Operation '{name}' not found in setup '{setup.Name}'");
    }

    /// <summary>Reference _wait_future: bounded 25 s wait, 0.1 s polling.</summary>
    private static void WaitCamFuture(IFusionContext ctx, IFusionCamFuture future)
    {
        var deadline = Stopwatch.StartNew();
        while (!future.IsCompleted)
        {
            if (deadline.Elapsed.TotalSeconds > 25.0)
            {
                throw new FusionCommandException(
                    "Toolpath generation did not finish within 25s. " +
                    "The operation may still complete in the background — " +
                    "check with cam_list_operations before regenerating.");
            }

            ctx.DoEvents();
            System.Threading.Thread.Sleep(100);
        }
    }

    private static string NumToString(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);

    private static JsonNode NumberOrExpression(double value, string? expression)
        => expression is null ? JsonValue.Create(value) : JsonValue.Create(expression);

    // ------------------------------------------------------------------
    // Bodies
    // ------------------------------------------------------------------

    public static JsonObject RenameBody(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var newName = ParamReader.Str(p, "new_name", string.Empty);

        var body = DesignQueries.BodyByName(ctx, bodyName);
        var oldName = body.Name;
        body.Name = newName;

        return new JsonObject
        {
            ["renamed"] = true,
            ["old_name"] = oldName,
            ["new_name"] = newName,
        };
    }

    public static JsonObject CreateBoxParametric(IFusionContext ctx, JsonObject p)
    {
        var lengthOrExpr = ParamReader.DimOrExpression(p, "length");
        var widthOrExpr = ParamReader.DimOrExpression(p, "width");
        var heightOrExpr = ParamReader.DimOrExpression(p, "height");
        if (lengthOrExpr is null || widthOrExpr is null || heightOrExpr is null)
        {
            throw new FusionCommandException("length/width/height are required");
        }

        var (length, lengthExpr) = lengthOrExpr.Value;
        var (width, widthExpr) = widthOrExpr.Value;
        var (height, heightExpr) = heightOrExpr.Value;
        var originX = ParamReader.Num(p, "origin_x", 0);
        var originY = ParamReader.Num(p, "origin_y", 0);
        var originZ = ParamReader.Num(p, "origin_z", 0);
        var plane = ParamReader.Str(p, "plane", "xy");
        var componentName = ParamReader.StrOrNull(p, "component_name");
        var bodyName = ParamReader.StrOrNull(p, "body_name");

        var comp = componentName is null
            ? ctx.Design.RootComponent
            : DesignQueries.ComponentByName(ctx, componentName);
        // The reference _construction_plane always resolves against the root
        // component's default planes, even when the box lands in a child.
        var basePlane = DesignQueries.ConstructionPlane(ctx.Design.RootComponent, plane);

        IFusionConstructionPlane sketchPlane = basePlane;
        if (originZ != 0)
        {
            var planeInput = comp.ConstructionPlanes.CreateInput();
            planeInput.SetByOffset(basePlane, originZ);
            sketchPlane = comp.ConstructionPlanes.Add(planeInput);
        }

        var sketch = comp.Sketches.Add(sketchPlane);

        // The reference's _initial: expressions get a 1.0 placeholder so the
        // rectangle is dimensionable; the driving dimensions carry the value.
        double Initial(double value, string? expression) => expression is null ? value : 1.0;

        var p1 = new FusionPoint(originX, originY, 0);
        var p2 = new FusionPoint(originX + Initial(length, lengthExpr), originY + Initial(width, widthExpr), 0);
        var (bottom, right) = sketch.Curves.Lines.AddTwoPointRectangleLines(p1, p2);

        var textPoint = new FusionPoint(0, 0, 0);

        void SetDim(IFusionSketchDimension dim, double value, string? expression)
        {
            if (expression is null)
            {
                dim.SetParameterValue(value);
            }
            else
            {
                dim.SetParameterExpression(expression);
            }
        }

        var lengthDim = sketch.Dimensions.AddEndpointDistanceDimension(
            bottom, DimensionOrientations.Horizontal, textPoint);
        SetDim(lengthDim, length, lengthExpr);

        var widthDim = sketch.Dimensions.AddEndpointDistanceDimension(
            right, DimensionOrientations.Vertical, textPoint);
        SetDim(widthDim, width, widthExpr);

        if (sketch.ProfilesCount == 0)
        {
            throw new FusionCommandException("Rectangle sketch produced no profile");
        }

        var profile = sketch.Profiles.Item(0);

        var extInput = comp.Features.ExtrudeFeatures.CreateInput(profile, FusionFeatureOperation.NewBody);
        if (heightExpr is null)
        {
            extInput.SetDistanceExtent(false, height);
        }
        else
        {
            extInput.SetDistanceExtentExpression(heightExpr);
        }

        var feature = extInput.Add();
        var body = feature.FirstBody;
        if (bodyName is not null)
        {
            body.Name = bodyName;
        }

        return new JsonObject
        {
            ["created"] = true,
            ["body_name"] = body.Name,
            ["feature_name"] = feature.Name,
            ["sketch_name"] = sketch.Name,
            ["length"] = NumberOrExpression(length, lengthExpr),
            ["width"] = NumberOrExpression(width, widthExpr),
            ["height"] = NumberOrExpression(height, heightExpr),
            ["origin"] = new JsonArray { originX, originY, originZ },
            ["plane"] = plane,
            ["component"] = comp.Name,
        };
    }

    // ------------------------------------------------------------------
    // Export
    // ------------------------------------------------------------------

    public static JsonObject ExportStl(IFusionContext ctx, JsonObject p)
        => ExportStlCore(ctx, ParamReader.Str(p, "body_name", string.Empty), ParamReader.StrOrNull(p, "file_path"));

    public static JsonObject ExportStep(IFusionContext ctx, JsonObject p)
        => ExportStepCore(ctx, ParamReader.Str(p, "body_name", string.Empty), ParamReader.StrOrNull(p, "file_path"));

    private static JsonObject ExportStlCore(IFusionContext ctx, string bodyName, string? filePath)
    {
        var body = DesignQueries.BodyByName(ctx, bodyName);
        if (filePath is null)
        {
            filePath = Path.Combine(Desktop(), $"{bodyName}.stl");
        }

        EnsureDirectory(filePath);

        // Body lives in a component occurrence: hide siblings so the
        // occurrence export only contains the target body. Siblings are
        // identified by entityToken, not name (same-name bodies).
        var occ = body.AssemblyContext;
        if (occ is null)
        {
            ctx.Design.ExportManager.ExportStl(body, filePath);
        }
        else
        {
            var targetToken = body.EntityToken;
            var hidden = new List<IFusionBody>();
            for (var i = 0; i < occ.BRepBodies.Count; i++)
            {
                var sibling = occ.BRepBodies.Item(i);
                if (sibling.EntityToken != targetToken && sibling.IsVisible)
                {
                    sibling.IsVisible = false;
                    hidden.Add(sibling);
                }
            }

            try
            {
                ctx.Design.ExportManager.ExportStl(occ, filePath);
            }
            finally
            {
                foreach (var sibling in hidden)
                {
                    sibling.IsVisible = true;
                }
            }
        }

        return new JsonObject
        {
            ["exported"] = true,
            ["body"] = bodyName,
            ["file_path"] = filePath,
        };
    }

    private static JsonObject ExportStepCore(IFusionContext ctx, string bodyName, string? filePath)
    {
        var body = DesignQueries.BodyByName(ctx, bodyName);
        if (filePath is null)
        {
            filePath = Path.Combine(Desktop(), $"{bodyName}.step");
        }

        EnsureDirectory(filePath);

        var occ = body.AssemblyContext;
        if (occ is null)
        {
            ctx.Design.ExportManager.ExportStep(filePath, body);
        }
        else
        {
            var targetToken = body.EntityToken;
            var hidden = new List<IFusionBody>();
            for (var i = 0; i < occ.BRepBodies.Count; i++)
            {
                var sibling = occ.BRepBodies.Item(i);
                if (sibling.EntityToken != targetToken && sibling.IsVisible)
                {
                    sibling.IsVisible = false;
                    hidden.Add(sibling);
                }
            }

            try
            {
                ctx.Design.ExportManager.ExportStep(filePath, occ);
            }
            finally
            {
                foreach (var sibling in hidden)
                {
                    sibling.IsVisible = true;
                }
            }
        }

        return new JsonObject
        {
            ["exported"] = true,
            ["body"] = bodyName,
            ["file_path"] = filePath,
        };
    }

    public static JsonObject ExportF3d(IFusionContext ctx, JsonObject p)
    {
        var design = ctx.Design;
        var filePath = ParamReader.StrOrNull(p, "file_path");
        if (filePath is null)
        {
            filePath = Path.Combine(Desktop(), $"{design.DocumentName}.f3d");
        }

        EnsureDirectory(filePath);
        design.ExportManager.ExportFusionArchive(filePath);

        return new JsonObject
        {
            ["exported"] = true,
            ["file_path"] = filePath,
        };
    }

    public static JsonObject Export(IFusionContext ctx, JsonObject p)
    {
        var format = ParamReader.StrOrNull(p, "format");
        var bodyName = ParamReader.StrOrNull(p, "body_name");
        var filePath = ParamReader.StrOrNull(p, "file_path");

        var fmt = format?.ToLowerInvariant();
        if (string.IsNullOrEmpty(fmt) && filePath is not null)
        {
            fmt = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        }

        if (string.IsNullOrEmpty(fmt))
        {
            throw new FusionCommandException(
                "Specify format (stl/step/f3d) or file_path with extension");
        }

        if (fmt == "stl")
        {
            if (bodyName is null)
            {
                throw new FusionCommandException("body_name required for STL export");
            }

            return ExportStlCore(ctx, bodyName, filePath);
        }

        if (fmt is "step" or "stp")
        {
            if (bodyName is null)
            {
                throw new FusionCommandException("body_name required for STEP export");
            }

            return ExportStepCore(ctx, bodyName, filePath);
        }

        if (fmt == "f3d")
        {
            return ExportF3dCore(ctx, filePath);
        }

        throw new FusionCommandException($"Unknown format: {fmt}. Expected: stl, step, f3d");
    }

    private static JsonObject ExportF3dCore(IFusionContext ctx, string? filePath)
    {
        var design = ctx.Design;
        if (filePath is null)
        {
            filePath = Path.Combine(Desktop(), $"{design.DocumentName}.f3d");
        }

        EnsureDirectory(filePath);
        design.ExportManager.ExportFusionArchive(filePath);

        return new JsonObject
        {
            ["exported"] = true,
            ["file_path"] = filePath,
        };
    }

    /// <summary>
    /// Port of reference <c>export_view_sheet</c>: render the canonical views
    /// (shared with render_view via <see cref="T5Handlers.ViewDirs"/>) as PNGs
    /// and emit a self-contained HTML sheet + machine-readable manifest.
    /// </summary>
    public static JsonObject ExportViewSheet(IFusionContext ctx, JsonObject p)
    {
        var design = ctx.Design;
        var docName = design.DocumentName;
        var sheetTitle = ParamReader.StrOrNull(p, "title") ?? docName;
        var notes = ParamReader.Str(p, "notes", string.Empty);

        // Reference `views or default` / `image_size or default` (empty list = default).
        var views = ParamReader.ListOrNull(p, "views");
        var viewNames = new List<string>();
        if (views is not null)
        {
            foreach (var node in views)
            {
                if (node is JsonValue value && value.TryGetValue<string>(out var s))
                {
                    viewNames.Add(s);
                }
            }
        }

        if (viewNames.Count == 0)
        {
            viewNames = new List<string> { "iso", "front", "top", "right" };
        }

        var size = ParamReader.ListOrNull(p, "image_size");
        int width = 1200, height = 900;
        if (size is not null && size.Count == 2)
        {
            if (NodeInt(size[0], out width) && NodeInt(size[1], out height))
            {
                // fall through with the parsed values
            }
        }

        var unknown = viewNames.Where(v => !T5Handlers.ViewDirs.ContainsKey(v)).ToList();
        if (unknown.Count > 0)
        {
            var valid = T5Handlers.ViewDirs.Keys.OrderBy(k => k).ToList();
            throw new FusionCommandException(
                $"Unknown views: {PythonList(unknown)}. Valid: {PythonList(valid)}");
        }

        var outputDir = ParamReader.StrOrNull(p, "output_dir");
        if (outputDir is null)
        {
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            outputDir = Path.Combine(Desktop(), $"{docName}_views_{ts}");
        }

        Directory.CreateDirectory(outputDir);

        var viewport = ctx.Viewport;
        if (viewport is null)
        {
            throw new FusionCommandException("No active viewport");
        }

        // Save the camera state for the reference restore path.
        var cam = viewport.Camera;
        var origEye = cam.Eye;
        var origTarget = cam.Target;
        var origUp = cam.UpVector;
        var origType = cam.CameraType;

        var (center, radius) = SceneCenterAndRadius(ctx.Design.RootComponent);

        var rendered = new JsonArray();
        try
        {
            foreach (var viewName in viewNames)
            {
                ApplyView(viewport, ctx, viewName, center, radius);
                var pngPath = Path.Combine(outputDir, $"{viewName}.png");
                viewport.SaveAsImageFile(pngPath, width, height);
                rendered.Add(new JsonObject
                {
                    ["view"] = viewName,
                    ["path"] = pngPath,
                });
            }
        }
        finally
        {
            // Restore the user's view (reference camera-restore path).
            var c = viewport.Camera;
            c.IsSmoothTransition = false;
            c.CameraType = origType;
            c.Eye = origEye;
            c.Target = origTarget;
            c.UpVector = origUp;
            viewport.Camera = c;
            viewport.Refresh();
        }

        // Build the self-contained HTML with base64-embedded PNGs.
        var figures = new List<string>();
        foreach (var node in rendered)
        {
            if (node is not JsonObject item)
            {
                continue;
            }

            var path = item["path"]!.GetValue<string>();
            var b64 = Convert.ToBase64String(File.ReadAllBytes(path));
            var label = item["view"]!.GetValue<string>().Replace("_", " ").ToUpperInvariant();
            figures.Add(
                "<figure>" +
                $"<img src=\"data:image/png;base64,{b64}\" alt=\"{label}\">" +
                $"<figcaption>{label}</figcaption>" +
                "</figure>");
        }

        var notesBlock = string.Empty;
        if (notes.Length > 0)
        {
            notesBlock =
                "<section class=\"notes\"><h2>Notes</h2>" +
                $"<pre>{WebUtility.HtmlEncode(notes)}</pre></section>";
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var htmlDoc =
            "<!doctype html>\n<html lang=\"en\"><head>\n" +
            "<meta charset=\"utf-8\">\n" +
            $"<title>{WebUtility.HtmlEncode(sheetTitle)}</title>\n" +
            BuildViewSheetCss() +
            "</head><body>\n" +
            "<header>\n" +
            $"  <h1>{WebUtility.HtmlEncode(sheetTitle)}</h1>\n" +
            $"  <div class=\"meta\">{WebUtility.HtmlEncode(docName)} * {timestamp}</div>\n" +
            "</header>\n" +
            $"<section class=\"views\">{string.Join(string.Empty, figures)}</section>\n" +
            $"{notesBlock}\n" +
            "</body></html>\n";

        var htmlPath = Path.Combine(outputDir, "view_sheet.html");
        File.WriteAllText(htmlPath, htmlDoc);

        // Sidecar manifest — machine-readable record of what was emitted.
        // 'rendered' is DeepCloned into the manifest: the response object
        // below attaches the live node, and System.Text.Json 8 on net48
        // throws ("The node already has a parent.") when a node with a
        // parent is attached to a second parent.
        var manifest = new JsonObject
        {
            ["title"] = sheetTitle,
            ["document"] = docName,
            ["views"] = rendered.DeepClone(),
            ["html_path"] = htmlPath,
            ["image_size"] = new JsonArray { width, height },
        };
        // net48 + System.Text.Json 8: AsReadOnly() (implicit in ToJsonString(options))
        // requires an explicit TypeInfoResolver on non-Core runtimes.
        var manifestJson = manifest.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        });
        File.WriteAllText(Path.Combine(outputDir, "manifest.json"), manifestJson);

        return new JsonObject
        {
            ["html_path"] = htmlPath,
            ["output_dir"] = outputDir,
            ["views"] = rendered,
            ["title"] = sheetTitle,
            ["image_size"] = new JsonArray { width, height },
        };
    }

    private static string BuildViewSheetCss() =>
        "<style>\n" +
        "  :root { color-scheme: light; }\n" +
        "  body { font-family: -apple-system, BlinkMacSystemFont, \"Segoe UI\",\n" +
        "           sans-serif; color: #111; max-width: 1400px;\n" +
        "           margin: 2rem auto; padding: 0 2rem; }\n" +
        "  header { border-bottom: 1px solid #d0d0d0; padding-bottom: .75rem;\n" +
        "            margin-bottom: 2rem; }\n" +
        "  header h1 { margin: 0; font-weight: 500; font-size: 1.6rem; }\n" +
        "  header .meta { color: #666; font-size: .85rem; margin-top: .25rem; }\n" +
        "  .views { display: grid; grid-template-columns: 1fr 1fr;\n" +
        "            gap: 1.25rem; }\n" +
        "  figure { margin: 0; border: 1px solid #e0e0e0; padding: .5rem;\n" +
        "            background: #fafafa; }\n" +
        "  figure img { width: 100%; display: block; background: #fff; }\n" +
        "  figcaption { text-align: center; font-size: .75rem; color: #555;\n" +
        "                margin-top: .35rem; letter-spacing: .15em; }\n" +
        "  .notes { margin-top: 2rem; padding-top: 1rem;\n" +
        "            border-top: 1px solid #e0e0e0; }\n" +
        "  .notes h2 { font-size: 1rem; font-weight: 500; margin: 0 0 .5rem; }\n" +
        "  .notes pre { font-family: inherit; white-space: pre-wrap;\n" +
        "                margin: 0; color: #333; }\n" +
        "  @media print {\n" +
        "    body { max-width: none; margin: 0; padding: 1cm; }\n" +
        "    .views { gap: .5cm; }\n" +
        "    figure { break-inside: avoid; }\n" +
        "  }\n" +
        "</style>\n";

    private static bool NodeInt(JsonNode? node, out int value)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i))
            {
                value = i;
                return true;
            }

            if (v.TryGetValue<double>(out var d))
            {
                value = (int)d;
                return true;
            }
        }

        value = 0;
        return false;
    }

    /// <summary>Reference _scene_center_and_radius: center/radius over all visible bodies.</summary>
    private static (FusionPoint Center, double Radius) SceneCenterAndRadius(IFusionRootComponent root)
    {
        var mnX = double.PositiveInfinity;
        var mnY = double.PositiveInfinity;
        var mnZ = double.PositiveInfinity;
        var mxX = double.NegativeInfinity;
        var mxY = double.NegativeInfinity;
        var mxZ = double.NegativeInfinity;
        var found = false;

        void Grow(IFusionBoundingBox? box)
        {
            if (box is null)
            {
                return;
            }

            if (box.Min.X < mnX) { mnX = box.Min.X; }
            if (box.Min.Y < mnY) { mnY = box.Min.Y; }
            if (box.Min.Z < mnZ) { mnZ = box.Min.Z; }
            if (box.Max.X > mxX) { mxX = box.Max.X; }
            if (box.Max.Y > mxY) { mxY = box.Max.Y; }
            if (box.Max.Z > mxZ) { mxZ = box.Max.Z; }
            found = true;
        }

        for (var i = 0; i < root.BRepBodies.Count; i++)
        {
            var b = root.BRepBodies.Item(i);
            if (b.IsVisible)
            {
                Grow(b.BoundingBox);
            }
        }

        for (var o = 0; o < root.Occurrences.Count; o++)
        {
            var occ = root.Occurrences.OccurrenceAt(o);
            if (!occ.IsVisible)
            {
                continue;
            }

            for (var j = 0; j < occ.BRepBodies.Count; j++)
            {
                var b = occ.BRepBodies.Item(j);
                if (b.IsVisible)
                {
                    Grow(b.BoundingBox);
                }
            }
        }

        if (!found)
        {
            return (new FusionPoint(0, 0, 0), 10.0);
        }

        var center = new FusionPoint((mnX + mxX) * 0.5, (mnY + mxY) * 0.5, (mnZ + mxZ) * 0.5);
        var span = Math.Max(mxX - mnX, Math.Max(mxY - mnY, mxZ - mnZ));
        return (center, Math.Max(span, 1.0));
    }

    /// <summary>Reference _apply_view: point the camera at the scene center from a preset.</summary>
    private static void ApplyView(IFusionViewport viewport, IFusionContext ctx, string viewName, FusionPoint center, double radius)
    {
        if (!T5Handlers.ViewDirs.TryGetValue(viewName, out var spec))
        {
            throw new FusionCommandException($"Unknown view '{viewName}'");
        }

        var dir = spec.EyeDir;
        var length = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
        var dist = radius * 3.0; // give fit() headroom
        var eye = new FusionPoint(
            center.X + dir.X / length * dist,
            center.Y + dir.Y / length * dist,
            center.Z + dir.Z / length * dist);

        var cam = viewport.Camera;
        cam.IsSmoothTransition = false;
        cam.CameraType = 1; // parallel (orthographic)
        cam.Eye = eye;
        cam.Target = center;
        cam.UpVector = spec.UpVec;
        cam.IsFitView = true;
        viewport.Camera = cam;
        viewport.Refresh();
        ctx.DoEvents();
    }

    // ------------------------------------------------------------------
    // Mesh
    // ------------------------------------------------------------------

    public static JsonObject ImportMesh(IFusionContext ctx, JsonObject p)
    {
        var filePath = ParamReader.Str(p, "file_path", string.Empty);
        var componentName = ParamReader.StrOrNull(p, "component_name");
        var units = ParamReader.Str(p, "units", "mm");

        if (!File.Exists(filePath))
        {
            throw new FusionCommandException($"Mesh file not found: {filePath}");
        }

        var target = componentName is null
            ? ctx.Design.RootComponent
            : DesignQueries.ComponentByName(ctx, componentName);

        var unitEnum = units switch
        {
            "mm" => FusionMeshUnits.Millimeter,
            "cm" => FusionMeshUnits.Centimeter,
            "m" => FusionMeshUnits.Meter,
            "in" => FusionMeshUnits.Inch,
            "ft" => FusionMeshUnits.Foot,
            _ => throw new FusionCommandException(
                $"Unknown units '{units}'. Expected one of: " +
                PythonList(new[] { "cm", "ft", "in", "m", "mm" })),
        };

        var meshList = target.MeshBodies.Add(filePath, unitEnum);
        if (meshList.Count == 0)
        {
            throw new FusionCommandException($"No mesh bodies imported from {filePath}");
        }

        var meshBody = meshList.Item(0);
        var box = meshBody.BoundingBox;

        var boundingBox = new JsonObject();
        if (box is not null)
        {
            boundingBox["min"] = PointArrayJson(box.Min);
            boundingBox["max"] = PointArrayJson(box.Max);
            boundingBox["size"] = new JsonArray
            {
                box.Max.X - box.Min.X,
                box.Max.Y - box.Min.Y,
                box.Max.Z - box.Min.Z,
            };
        }

        return new JsonObject
        {
            ["imported"] = true,
            ["file_path"] = filePath,
            ["mesh_name"] = meshBody.Name,
            ["component"] = target.Name,
            ["units"] = units,
            ["bounding_box"] = boundingBox,
        };
    }

    public static JsonObject CompareMeshes(IFusionContext ctx, JsonObject p)
    {
        var meshNameA = ParamReader.Str(p, "mesh_name_a", string.Empty);
        var meshNameB = ParamReader.Str(p, "mesh_name_b", string.Empty);
        var root = ctx.Design.RootComponent;

        IFusionMeshBody MeshBodyByName(string name)
        {
            var meshBodies = root.MeshBodies;
            for (var i = 0; i < meshBodies.Count; i++)
            {
                var b = meshBodies.Item(i);
                if (b.Name == name)
                {
                    return b;
                }
            }

            throw new FusionCommandException(
                $"Mesh body '{name}' not found in root component. " +
                "Use get_scene_info to list mesh bodies.");
        }

        var bodyA = MeshBodyByName(meshNameA);
        var bodyB = MeshBodyByName(meshNameB);

        var deviations = bodyA.CompareWith(bodyB);
        if (deviations is null)
        {
            throw new FusionCommandException("Could not obtain polygon mesh data");
        }

        if (deviations.Count == 0)
        {
            throw new FusionCommandException("compareWith returned no deviation data");
        }

        var n = deviations.Count;
        var meanAbs = deviations.Average(Math.Abs);
        var rms = Math.Sqrt(deviations.Sum(d => d * d) / n);
        var maxAbs = deviations.Max(Math.Abs);

        return new JsonObject
        {
            ["mesh_a"] = meshNameA,
            ["mesh_b"] = meshNameB,
            ["node_count"] = n,
            ["min_deviation"] = deviations.Min(),
            ["max_deviation"] = deviations.Max(),
            ["mean_abs_deviation"] = meanAbs,
            ["rms_deviation"] = rms,
            ["max_abs_deviation"] = maxAbs,
            ["units"] = "cm",
        };
    }

    // ------------------------------------------------------------------
    // Construction geometry
    // ------------------------------------------------------------------

    public static JsonObject CreateConstructionPlane(IFusionContext ctx, JsonObject p)
    {
        var method = ParamReader.Str(p, "method", string.Empty);
        var root = ctx.Design.RootComponent;
        var planes = root.ConstructionPlanes;
        var input = planes.CreateInput();

        switch (method)
        {
            case "offset":
                input.SetByOffset(
                    DesignQueries.ConstructionPlane(root, ParamReader.Str(p, "plane", string.Empty)),
                    ParamReader.Num(p, "offset", 0));
                break;
            case "angle":
                input.SetByAngle(
                    T6Handlers.ConstructionAxis(root, ParamReader.Str(p, "edge_name", "x")),
                    ParamReader.Num(p, "angle", 0),
                    DesignQueries.ConstructionPlane(root, ParamReader.Str(p, "plane", string.Empty)));
                break;
            case "midplane":
                input.SetByTwoPlanes(
                    DesignQueries.ConstructionPlane(root, ParamReader.Str(p, "plane_one", string.Empty)),
                    DesignQueries.ConstructionPlane(root, ParamReader.Str(p, "plane_two", string.Empty)));
                break;
            case "three_points":
            {
                var pointOne = ParamReader.Point3(p, "point_one");
                var pointTwo = ParamReader.Point3(p, "point_two");
                var pointThree = ParamReader.Point3(p, "point_three");
                if (pointOne is null || pointTwo is null || pointThree is null)
                {
                    throw new FusionCommandException(
                        "three_points requires point_one, point_two, and point_three [x, y, z] arrays");
                }

                input.SetByThreePoints(pointOne.Value, pointTwo.Value, pointThree.Value);
                break;
            }
            case "tangent":
                throw new FusionCommandException("Tangent plane creation requires selecting a face in the Fusion UI.");
            default:
                throw new FusionCommandException($"Unknown method: {method}");
        }

        var planeObj = planes.Add(input);
        return new JsonObject
        {
            ["created"] = true,
            ["name"] = planeObj.Name,
            ["method"] = method,
        };
    }

    public static JsonObject CreateConstructionAxis(IFusionContext ctx, JsonObject p)
    {
        var method = ParamReader.Str(p, "method", string.Empty);
        var root = ctx.Design.RootComponent;
        var axes = root.ConstructionAxes;
        var input = axes.CreateInput();

        switch (method)
        {
            case "two_points":
            {
                var pointOne = ParamReader.Point3(p, "point_one");
                var pointTwo = ParamReader.Point3(p, "point_two");
                if (pointOne is null || pointTwo is null)
                {
                    throw new FusionCommandException("two_points requires point_one and point_two [x, y, z] arrays");
                }

                input.SetByTwoPoints(pointOne.Value, pointTwo.Value);
                break;
            }
            case "intersection":
                input.SetByTwoPlanes(
                    DesignQueries.ConstructionPlane(root, ParamReader.Str(p, "plane_one", string.Empty)),
                    DesignQueries.ConstructionPlane(root, ParamReader.Str(p, "plane_two", string.Empty)));
                break;
            case "edge":
            {
                var body = DesignQueries.BodyByName(ctx, ParamReader.Str(p, "body_name", string.Empty));
                var edgeIndex = ParamReader.Int(p, "edge_index", 0);
                input.SetByEdge(body.Edges[edgeIndex]);
                break;
            }
            case "perpendicular_at_point":
            {
                var pointOne = ParamReader.Point3(p, "point_one");
                if (pointOne is null)
                {
                    throw new FusionCommandException("perpendicular_at_point requires point_one [x, y, z]");
                }

                input.SetByPerpendicularAtPoint(
                    DesignQueries.ConstructionPlane(root, ParamReader.Str(p, "plane_one", string.Empty)),
                    pointOne.Value);
                break;
            }
            default:
                throw new FusionCommandException($"Unknown method: {method}");
        }

        var axisObj = axes.Add(input);
        return new JsonObject
        {
            ["created"] = true,
            ["name"] = axisObj.Name,
            ["method"] = method,
        };
    }

    public static JsonObject CreateUcs(IFusionContext ctx, JsonObject p)
    {
        var name = ParamReader.StrOrNull(p, "name");
        var x = ParamReader.Num(p, "x", 0);
        var y = ParamReader.Num(p, "y", 0);
        var z = ParamReader.Num(p, "z", 0);
        var angleX = ParamReader.Num(p, "angle_x", 0);
        var angleY = ParamReader.Num(p, "angle_y", 0);
        var angleZ = ParamReader.Num(p, "angle_z", 0);

        var root = ctx.Design.RootComponent;

        IFusionConstructionPlane basePlane;
        if (z != 0)
        {
            var planeInput = root.ConstructionPlanes.CreateInput();
            planeInput.SetByOffset(root.XYConstructionPlane, z);
            basePlane = root.ConstructionPlanes.Add(planeInput);
        }
        else
        {
            basePlane = root.XYConstructionPlane;
        }

        // The UCS anchor must be a sketch point (construction points are
        // rejected by current builds — reference docstring). The reference
        // sketch stays in the scene; the UCS is linked to it.
        var sketch = root.Sketches.Add(basePlane);
        var point = sketch.AddPointInModelSpace(new FusionPoint(x, y, 0));
        var refName = $"UCS_{name ?? "unnamed"}_ref";
        sketch.Name = refName;
        sketch.IsVisible = false;

        var geometry = root.CreateUcsGeometryAtPoint(point);
        var input = root.UserCoordinateSystems.CreateInput(geometry);
        if (angleX != 0)
        {
            input.SetAngleXRadians(Math.PI * angleX / 180.0);
        }

        if (angleY != 0)
        {
            input.SetAngleYRadians(Math.PI * angleY / 180.0);
        }

        if (angleZ != 0)
        {
            input.SetAngleZRadians(Math.PI * angleZ / 180.0);
        }

        var ucs = root.UserCoordinateSystems.Add(input);
        if (name is not null)
        {
            try
            {
                ucs.Name = name;
            }
            catch
            {
                // Reference try/except pass: some builds reject UCS renames.
            }
        }

        return new JsonObject
        {
            ["name"] = ucs.Name,
            ["origin"] = new JsonArray { x, y, z },
            ["angles_deg"] = new JsonArray { angleX, angleY, angleZ },
            ["reference_sketch"] = refName,
        };
    }

    // ------------------------------------------------------------------
    // Assembly
    // ------------------------------------------------------------------

    public static JsonObject CreateComponent(IFusionContext ctx, JsonObject p)
    {
        var name = ParamReader.Str(p, "name", string.Empty);
        var parentName = ParamReader.StrOrNull(p, "parent_name");
        var root = ctx.Design.RootComponent;

        IFusionComponent parent = parentName is null
            ? root
            : DesignQueries.ComponentByName(ctx, parentName);

        var occ = parent.Occurrences.AddNewComponent();
        occ.Component.Name = name;

        return new JsonObject
        {
            ["created"] = true,
            ["name"] = name,
        };
    }

    public static JsonObject AddJoint(IFusionContext ctx, JsonObject p)
    {
        var componentOne = ParamReader.Str(p, "component_one", string.Empty);
        var componentTwo = ParamReader.Str(p, "component_two", string.Empty);
        var jointType = ParamReader.Str(p, "joint_type", "rigid");

        var (occ1, occ2) = OccurrencesByComponentNames(ctx, componentOne, componentTwo);

        // The reference validates against its JointTypes table; only rigid
        // maps to an input motion (the rest use the default motion).
        var knownTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "rigid", "revolute", "slider", "cylindrical", "pin_slot", "planar", "ball",
        };
        if (!knownTypes.Contains(jointType))
        {
            throw new FusionCommandException($"Unknown joint type: {jointType}");
        }

        var root = ctx.Design.RootComponent;
        var geo1 = root.CreateJointGeometryByPoint(occ1, occ1.Component.OriginConstructionPoint);
        var geo2 = root.CreateJointGeometryByPoint(occ2, occ2.Component.OriginConstructionPoint);

        var input = root.Joints.CreateInput(geo1, geo2);
        if (jointType == "rigid")
        {
            input.SetAsRigidJointMotion();
        }

        root.Joints.Add(input);
        return new JsonObject
        {
            ["created"] = true,
            ["joint_type"] = jointType,
        };
    }

    public static JsonObject CreateAsBuiltJoint(IFusionContext ctx, JsonObject p)
    {
        var componentOne = ParamReader.Str(p, "component_one", string.Empty);
        var componentTwo = ParamReader.Str(p, "component_two", string.Empty);
        var jointType = ParamReader.Str(p, "joint_type", "rigid");

        var (occ1, occ2) = OccurrencesByComponentNames(ctx, componentOne, componentTwo);

        var root = ctx.Design.RootComponent;
        var input = root.AsBuiltJoints.CreateInput(occ1, occ2);
        root.AsBuiltJoints.Add(input);

        return new JsonObject
        {
            ["created"] = true,
            ["joint_type"] = jointType,
        };
    }

    public static JsonObject CreateRigidGroup(IFusionContext ctx, JsonObject p)
    {
        var componentNames = ParamReader.StringList(p, "component_names");
        var includeChildren = ParamReader.Bool(p, "include_children", true);

        var root = ctx.Design.RootComponent;
        var occurrences = new List<IFusionOccurrence>();
        foreach (var name in componentNames)
        {
            for (var i = 0; i < root.AllOccurrences.Count; i++)
            {
                var occ = root.AllOccurrences.Item(i);
                if (occ.Component.Name == name)
                {
                    occurrences.Add(occ);
                    break;
                }
            }
        }

        if (occurrences.Count < 2)
        {
            throw new FusionCommandException("Need at least 2 components for rigid group");
        }

        root.RigidGroups.Add(occurrences, includeChildren);
        return new JsonObject
        {
            ["created"] = true,
            ["component_count"] = occurrences.Count,
        };
    }

    /// <summary>Reference add_joint/create_as_built_joint occurrence lookup.</summary>
    private static (IFusionOccurrence Occ1, IFusionOccurrence Occ2) OccurrencesByComponentNames(
        IFusionContext ctx, string componentOne, string componentTwo)
    {
        var root = ctx.Design.RootComponent;
        IFusionOccurrence? occ1 = null;
        IFusionOccurrence? occ2 = null;

        for (var i = 0; i < root.AllOccurrences.Count; i++)
        {
            var occ = root.AllOccurrences.Item(i);
            if (occ.Component.Name == componentOne)
            {
                occ1 = occ;
            }

            if (occ.Component.Name == componentTwo)
            {
                occ2 = occ;
            }
        }

        if (occ1 is null || occ2 is null)
        {
            throw new FusionCommandException("One or both components not found");
        }

        return (occ1, occ2);
    }

    // ------------------------------------------------------------------
    // Inspection
    // ------------------------------------------------------------------

    public static JsonObject MeasureDistance(IFusionContext ctx, JsonObject p)
    {
        var entityOne = ParamReader.Str(p, "entity_one", string.Empty);
        var entityTwo = ParamReader.Str(p, "entity_two", string.Empty);
        var root = ctx.Design.RootComponent;

        IFusionMeasureEntity GetEntity(string name)
        {
            for (var i = 0; i < root.BRepBodies.Count; i++)
            {
                var b = root.BRepBodies.Item(i);
                if (b.Name == name)
                {
                    return b;
                }
            }

            if (name.Contains(','))
            {
                var coords = name
                    .Split(',')
                    .Select(s => double.Parse(s.Trim(), CultureInfo.InvariantCulture))
                    .ToArray();
                if (coords.Length != 3)
                {
                    throw new FusionCommandException($"Entity '{name}' is not a valid 'x,y,z' point");
                }

                return new FusionMeasurePoint(new FusionPoint(coords[0], coords[1], coords[2]));
            }

            throw new FusionCommandException($"Entity '{name}' not found");
        }

        var e1 = GetEntity(entityOne);
        var e2 = GetEntity(entityTwo);

        var result = ctx.MeasureManager.MeasureMinimumDistance(e1, e2);
        return new JsonObject
        {
            ["distance"] = result.Value,
            ["point_one"] = PointArrayJson(result.PointOnEntityOne),
            ["point_two"] = PointArrayJson(result.PointOnEntityTwo),
        };
    }

    public static JsonObject MeasureAngle(IFusionContext ctx, JsonObject p)
    {
        var entityOne = ParamReader.Str(p, "entity_one", string.Empty);
        var entityTwo = ParamReader.Str(p, "entity_two", string.Empty);
        var root = ctx.Design.RootComponent;

        IFusionFace GetFace(string name)
        {
            for (var i = 0; i < root.BRepBodies.Count; i++)
            {
                var b = root.BRepBodies.Item(i);
                if (b.Name == name)
                {
                    return b.Faces[0]; // first face
                }
            }

            throw new FusionCommandException($"Entity '{name}' not found");
        }

        var faceOne = GetFace(entityOne);
        var faceTwo = GetFace(entityTwo);

        var result = ctx.MeasureManager.MeasureAngle(faceOne, faceTwo);
        return new JsonObject
        {
            ["angle_degrees"] = result.ValueRadians * 180.0 / Math.PI,
        };
    }

    public static JsonObject GetPhysicalProperties(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var accuracy = ParamReader.Str(p, "accuracy", "medium");
        var body = DesignQueries.BodyByName(ctx, bodyName);

        var acc = accuracy switch
        {
            "low" => FusionCalculationAccuracy.Low,
            "high" => FusionCalculationAccuracy.High,
            "very_high" => FusionCalculationAccuracy.VeryHigh,
            _ => FusionCalculationAccuracy.Medium, // reference accuracy_map.get fallback
        };

        var props = body.GetPhysicalProperties(acc);
        return new JsonObject
        {
            ["mass"] = props.MassKg,
            ["volume"] = props.VolumeCm3,
            ["area"] = props.AreaCm2,
            ["density"] = props.Density,
            ["center_of_mass"] = PointArrayJson(props.CenterOfMass),
        };
    }

    public static JsonObject CreateSectionAnalysis(IFusionContext ctx, JsonObject p)
    {
        var plane = ParamReader.Str(p, "plane", "yz");
        var offset = ParamReader.Num(p, "offset", 0);
        var root = ctx.Design.RootComponent;
        var analyses = root.Analyses;

        var input = analyses.CreateInput();
        input.Plane = DesignQueries.ConstructionPlane(root, plane);
        if (offset != 0)
        {
            input.SetDistanceCm(offset);
        }

        analyses.Add(input);
        return new JsonObject
        {
            ["created"] = true,
            ["plane"] = plane,
            ["offset"] = offset,
        };
    }

    public static JsonObject CheckInterference(IFusionContext ctx, JsonObject p)
    {
        var componentNames = ParamReader.StringList(p, "component_names");
        var includeCoincidentFaces = ParamReader.Bool(p, "include_coincident_faces", false);

        var root = ctx.Design.RootComponent;
        var bodies = new List<IFusionBody>();
        foreach (var name in componentNames)
        {
            for (var i = 0; i < root.AllOccurrences.Count; i++)
            {
                var occ = root.AllOccurrences.Item(i);
                if (occ.Component.Name == name)
                {
                    for (var j = 0; j < occ.BRepBodies.Count; j++)
                    {
                        bodies.Add(occ.BRepBodies.Item(j));
                    }
                }
            }
        }

        if (bodies.Count < 2)
        {
            throw new FusionCommandException("Need at least 2 components with bodies");
        }

        var design = ctx.Design;
        var input = design.CreateInterferenceInput(bodies);
        input.IncludeCoincidentFaces = includeCoincidentFaces;
        var interference = design.AnalyzeInterference(input);

        var results = new JsonArray();
        if (interference is not null)
        {
            foreach (var r in interference)
            {
                results.Add(new JsonObject
                {
                    ["body_one"] = r.BodyOne,
                    ["body_two"] = r.BodyTwo,
                    ["volume"] = r.InterferenceVolumeCm3,
                });
            }
        }

        return new JsonObject
        {
            ["interferences"] = results,
            ["count"] = results.Count,
        };
    }

    // ------------------------------------------------------------------
    // Appearance
    // ------------------------------------------------------------------

    public static JsonObject SetAppearance(IFusionContext ctx, JsonObject p)
    {
        var targetName = ParamReader.Str(p, "target_name", string.Empty);
        var appearanceName = ParamReader.Str(p, "appearance_name", string.Empty);
        var targetType = ParamReader.Str(p, "target_type", "body");
        var faceIndex = ParamReader.Int(p, "face_index", 0);

        var libraries = ctx.MaterialLibraries;

        // Find the appearance library — try both known names, then any
        // library that carries appearances.
        var appLib = libraries.ItemByName("Fusion Appearance Library")
            ?? libraries.ItemByName("Fusion 360 Appearance Library");
        if (appLib is null)
        {
            for (var i = 0; i < libraries.Count; i++)
            {
                var lib = libraries.Item(i);
                if (lib is not null && lib.AppearancesCount > 0)
                {
                    appLib = lib;
                    break;
                }
            }
        }

        if (appLib is null)
        {
            throw new FusionCommandException("No appearance library found");
        }

        IFusionAppearance? appearance = null;
        foreach (var a in appLib.Appearances)
        {
            if (a.Name == appearanceName)
            {
                appearance = a;
                break;
            }
        }

        if (appearance is null)
        {
            throw new FusionCommandException($"Appearance '{appearanceName}' not found");
        }

        // Library appearances must be copied into the design before
        // assignment (version-dependent direct assignment fails).
        var design = ctx.Design;
        var local = design.Appearances.ItemByName(appearance.Name);
        if (local is null)
        {
            local = design.Appearances.AddByCopy(appearance, appearance.Name);
        }

        if (local is null)
        {
            throw new FusionCommandException(
                $"Could not copy appearance '{appearanceName}' into the design");
        }

        if (targetType == "body")
        {
            var body = DesignQueries.BodyByName(ctx, targetName);
            body.Appearance = local;
        }
        else if (targetType == "component")
        {
            var comp = DesignQueries.ComponentByName(ctx, targetName);
            comp.Appearance = local;
        }
        else if (targetType == "face")
        {
            var body = DesignQueries.BodyByName(ctx, targetName);
            body.Faces[faceIndex].Appearance = local;
        }

        return new JsonObject
        {
            ["applied"] = true,
            ["target"] = targetName,
            ["appearance"] = appearanceName,
        };
    }

    public static JsonObject SetColor(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var red = ParamReader.Int(p, "red", 0);
        var green = ParamReader.Int(p, "green", 0);
        var blue = ParamReader.Int(p, "blue", 0);
        var opacity = ParamReader.Num(p, "opacity", 1.0);

        var body = DesignQueries.BodyByName(ctx, bodyName);
        var design = ctx.Design;

        red = ClampInt(red, 0, 255);
        green = ClampInt(green, 0, 255);
        blue = ClampInt(blue, 0, 255);
        var alpha = ClampInt((int)Math.Round(opacity * 255.0), 0, 255);

        var colorName = $"MCP_{red}_{green}_{blue}_{alpha}";
        var appearance = design.Appearances.ItemByName(colorName);
        if (appearance is null)
        {
            appearance = design.Appearances.Add(colorName);
            appearance.SetColor(red, green, blue, alpha);
        }

        body.Appearance = appearance;
        return new JsonObject
        {
            ["body"] = bodyName,
            ["color"] = new JsonArray { red, green, blue },
            ["opacity"] = alpha / 255.0,
            ["appearance"] = colorName,
        };
    }

    // ------------------------------------------------------------------
    // Parameters
    // ------------------------------------------------------------------

    public static JsonObject CreateParameter(IFusionContext ctx, JsonObject p)
    {
        var name = ParamReader.Str(p, "name", string.Empty);
        var value = ParamReader.Num(p, "value", 0);
        var unit = ParamReader.Str(p, "unit", string.Empty);
        var comment = ParamReader.StrOrNull(p, "comment");

        var design = ctx.Design;
        var normalizedUnit = ParameterUnits.NormalizeUnit(unit);
        var param = design.UserParameters.Add(name, value, normalizedUnit, comment ?? string.Empty);
        return new JsonObject
        {
            ["created"] = true,
            ["name"] = name,
            ["value"] = value,
            ["unit"] = normalizedUnit,
            ["expression"] = param.Expression,
        };
    }

    public static JsonObject SetParameter(IFusionContext ctx, JsonObject p)
    {
        var name = ParamReader.Str(p, "name", string.Empty);
        var value = ParamReader.Num(p, "value", 0);

        var design = ctx.Design;
        var param = design.UserParameters.ItemByName(name);
        if (param is null)
        {
            throw new FusionCommandException($"Parameter '{name}' not found");
        }

        // Assign the expression in the unit the parameter already declares —
        // a bare .value would silently rescale non-internal units.
        var unit = ParameterUnits.NormalizeUnit(param.Unit);
        param.Expression = ParameterUnits.ExpressionFor(value, unit);
        return new JsonObject
        {
            ["updated"] = true,
            ["name"] = name,
            ["value"] = value,
            ["unit"] = unit,
            ["expression"] = param.Expression,
        };
    }

    public static JsonObject DeleteParameter(IFusionContext ctx, JsonObject p)
    {
        var name = ParamReader.Str(p, "name", string.Empty);
        var design = ctx.Design;
        var param = design.UserParameters.ItemByName(name);
        if (param is null)
        {
            throw new FusionCommandException($"Parameter '{name}' not found");
        }

        design.UserParameters.Delete(param);
        return new JsonObject
        {
            ["deleted"] = true,
            ["name"] = name,
        };
    }

    // ------------------------------------------------------------------
    // Sheet metal
    // ------------------------------------------------------------------

    public static JsonObject CreateFlange(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var edgeIndex = ParamReader.Int(p, "edge_index", 0);
        var height = ParamReader.Num(p, "height", 1.0);
        var angle = ParamReader.Num(p, "angle", 90);
        var bendRadius = ParamReader.NumOrNull(p, "bend_radius");

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);
        var edge = body.Edges[edgeIndex];

        var flanges = root.Features.FlangeFeatures;
        var input = flanges.CreateInput(edge);
        input.SetAngleDegrees(angle);
        input.SetHeightCm(height);
        if (bendRadius is not null && bendRadius > 0)
        {
            input.SetBendRadiusCm(bendRadius.Value);
        }

        var feature = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feature.Name,
            ["height"] = height,
            ["angle"] = angle,
        };
    }

    public static JsonObject CreateBend(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var bendLineSketch = ParamReader.StrOrNull(p, "bend_line_sketch");
        var angle = ParamReader.Num(p, "angle", 90);
        var bendRadius = ParamReader.NumOrNull(p, "bend_radius");

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);

        if (bendLineSketch is not null)
        {
            var sketch = DesignQueries.SketchByName(ctx, bendLineSketch);
            var bendLine = sketch.Curves.Lines.Item(0);

            var bends = root.Features.BendFeatures;
            var input = bends.CreateInput(body, bendLine);
            input.SetBendAngleDegrees(angle);
            if (bendRadius is not null && bendRadius > 0)
            {
                input.SetBendRadiusCm(bendRadius.Value);
            }

            var feature = input.Add();
            return new JsonObject
            {
                ["feature_name"] = feature.Name,
                ["angle"] = angle,
            };
        }

        throw new FusionCommandException("bend_line_sketch is required");
    }

    public static JsonObject FlatPattern(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);

        var flatPatterns = root.Features.FlatPatternFeatures;
        var input = flatPatterns.CreateInput(body);
        var feature = flatPatterns.Add(input);

        return new JsonObject
        {
            ["feature_name"] = feature.Name,
        };
    }

    public static JsonObject Unfold(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var bendIndices = ParamReader.IntList(p, "bend_indices");
        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);

        var unfolds = root.Features.UnfoldFeatures;

        var bends = new List<IFusionFace>();
        if (bendIndices.Length > 0)
        {
            foreach (var idx in bendIndices)
            {
                bends.Add(body.Faces[idx]);
            }
        }
        else
        {
            // Unfold all bends.
            bends.AddRange(body.Faces);
        }

        // Stationary face: the first planar face (reference surfaceType check).
        IFusionFace? stationary = null;
        foreach (var face in body.Faces)
        {
            if (face.PlaneGeometry is not null)
            {
                stationary = face;
                break;
            }
        }

        if (stationary is null)
        {
            throw new FusionCommandException("No planar face found for stationary face");
        }

        var input = unfolds.CreateInput(bends, stationary);
        var feature = unfolds.Add(input);
        return new JsonObject
        {
            ["feature_name"] = feature.Name,
        };
    }

    // ------------------------------------------------------------------
    // CAM
    // ------------------------------------------------------------------

    public static JsonObject CamListSetups(IFusionContext ctx, JsonObject p)
    {
        var cam = GetCam(ctx);
        var result = new JsonArray();

        for (var i = 0; i < cam.Setups.Count; i++)
        {
            var setup = cam.Setups.Item(i);
            var ops = new JsonArray();
            for (var j = 0; j < setup.Operations.Count; j++)
            {
                ops.Add(setup.Operations.Item(j).Name);
            }

            result.Add(new JsonObject
            {
                ["name"] = setup.Name,
                ["operations"] = ops,
                ["is_valid"] = setup.IsValid,
            });
        }

        return new JsonObject
        {
            ["setups"] = result,
            ["count"] = result.Count,
        };
    }

    public static JsonObject CamListOperations(IFusionContext ctx, JsonObject p)
    {
        var setupName = ParamReader.Str(p, "setup_name", string.Empty);
        var cam = GetCam(ctx);
        var setup = FindSetup(cam, setupName);

        var result = new JsonArray();
        for (var i = 0; i < setup.Operations.Count; i++)
        {
            var op = setup.Operations.Item(i);
            result.Add(new JsonObject
            {
                ["name"] = op.Name,
                ["has_toolpath"] = op.HasToolpath,
                ["is_valid"] = op.IsValid,
            });
        }

        return new JsonObject
        {
            ["setup"] = setupName,
            ["operations"] = result,
            ["count"] = result.Count,
        };
    }

    public static JsonObject CamGetOperationInfo(IFusionContext ctx, JsonObject p)
    {
        var setupName = ParamReader.Str(p, "setup_name", string.Empty);
        var operationName = ParamReader.Str(p, "operation_name", string.Empty);
        var cam = GetCam(ctx);
        var setup = FindSetup(cam, setupName);
        var op = FindOperation(setup, operationName);

        var info = new JsonObject
        {
            ["name"] = op.Name,
            ["is_valid"] = op.IsValid,
            ["has_toolpath"] = op.HasToolpath,
        };

        var tool = op.Tool;
        if (tool is not null)
        {
            info["tool"] = new JsonObject
            {
                ["description"] = tool.Description,
            };
        }

        var parameters = new JsonObject();
        foreach (var parameter in op.Parameters)
        {
            if (parameter.TryGetExpression(out var expression))
            {
                parameters[parameter.Name] = expression;
            }
        }

        info["parameters"] = parameters;
        return info;
    }

    public static JsonObject CamCreateSetup(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var name = ParamReader.StrOrNull(p, "name");
        var operationType = ParamReader.Str(p, "operation_type", "milling");
        var stockMode = ParamReader.Str(p, "stock_mode", "relative_box");
        var stockOffsetSides = ParamReader.Num(p, "stock_offset_sides", 0);
        var stockOffsetTop = ParamReader.Num(p, "stock_offset_top", 0);
        var stockOffsetBottom = ParamReader.Num(p, "stock_offset_bottom", 0);

        var cam = GetCam(ctx);
        var body = DesignQueries.BodyByName(ctx, bodyName);

        var opType = operationType switch
        {
            "milling" => FusionCamOperationType.Milling,
            "turning" => FusionCamOperationType.Turning,
            "cutting" => FusionCamOperationType.Cutting,
            _ => throw new FusionCommandException(
                $"Unknown operation_type '{operationType}' " +
                "— use milling/turning/cutting"),
        };

        var setupInput = cam.Setups.CreateInput(opType);
        setupInput.SetModels(new IFusionBody[] { body });
        if (name is not null)
        {
            setupInput.Name = name;
        }

        var setup = cam.Setups.Add(setupInput);

        // Stock parameters live on the created setup, not the input.
        // "Relative size box" (Fusion 2705) has the id 'default'.
        var stockModeMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["relative_box"] = "default",
            ["fixed_box"] = "fixedbox",
            ["relative_cylinder"] = "relativecylinder",
            ["fixed_cylinder"] = "fixedcylinder",
            ["from_solid"] = "solid",
        };

        var appliedStock = new JsonObject();
        var failedStock = new JsonObject();
        if (stockMode.Length > 0)
        {
            var modeId = stockModeMap.TryGetValue(stockMode, out var mapped) ? mapped : stockMode;
            if (SetCamParameter(setup, "job_stockMode", $"'{modeId}'"))
            {
                appliedStock["stock_mode"] = modeId;
            }
            else
            {
                failedStock["stock_mode"] = stockMode;
            }
        }

        foreach (var (paramName, value) in new[]
                 {
                     ("job_stockOffset", stockOffsetSides),
                     ("job_stockOffsetTop", stockOffsetTop),
                     ("job_stockOffsetBottom", stockOffsetBottom),
                 })
        {
            if (value != 0)
            {
                if (SetCamParameter(setup, paramName, NumToString(value)))
                {
                    appliedStock[paramName] = value;
                }
                else
                {
                    failedStock[paramName] = value;
                }
            }
        }

        var result = new JsonObject
        {
            ["name"] = setup.Name,
            ["body"] = bodyName,
            ["operation_type"] = operationType,
        };

        if (appliedStock.Count > 0)
        {
            result["stock_applied"] = appliedStock;
        }

        if (failedStock.Count > 0)
        {
            result["stock_failed"] = failedStock;
            result["warning"] =
                "Some stock parameters could not be applied — parameter " +
                "names vary by Fusion build; inspect via cam_get_operation_info";
        }

        return result;
    }

    public static JsonObject CamCreateOperation(IFusionContext ctx, JsonObject p)
    {
        var setupName = ParamReader.Str(p, "setup_name", string.Empty);
        var strategy = ParamReader.Str(p, "strategy", string.Empty);
        var name = ParamReader.StrOrNull(p, "name");
        var toolNumber = ParamReader.IntOrNull(p, "tool_number");
        var toolDiameter = ParamReader.NumOrNull(p, "tool_diameter");
        var stepdown = ParamReader.NumOrNull(p, "stepdown");
        var stepover = ParamReader.NumOrNull(p, "stepover");
        var feedRate = ParamReader.NumOrNull(p, "feed_rate");
        var spindleSpeed = ParamReader.NumOrNull(p, "spindle_speed");
        var coolant = ParamReader.Str(p, "coolant", "flood");

        var cam = GetCam(ctx);
        var setup = FindSetup(cam, setupName);

        if (toolDiameter is not null)
        {
            throw new FusionCommandException(
                "tool_diameter cannot be set on an operation — tool geometry " +
                "comes from a tool in the CAM tool library. Select the tool " +
                "via tool_number instead.");
        }

        var opInput = setup.Operations.CreateInput(strategy);
        if (name is not null)
        {
            opInput.Name = name;
        }

        var op = setup.Operations.Add(opInput);
        if (op is null)
        {
            throw new FusionCommandException(
                $"Fusion rejected strategy '{strategy}' for setup '{setupName}'");
        }

        // Operation parameters only exist on the created operation,
        // keyed by strategy-dependent names.
        var applied = new JsonObject();
        var failed = new JsonObject();

        void TrySet(string paramName, string? expression, bool byString, JsonNode? valueJson)
        {
            if (expression is null)
            {
                return;
            }

            var text = byString ? $"'{expression}'" : expression;
            if (SetCamParameter(op, paramName, text))
            {
                applied[paramName] = valueJson;
            }
            else
            {
                failed[paramName] = valueJson;
            }
        }

        TrySet("tool_number", toolNumber?.ToString(CultureInfo.InvariantCulture), false,
            toolNumber is null ? null : JsonValue.Create(toolNumber.Value));
        TrySet("maximumStepdown", NumToStringNullable(stepdown), false,
            stepdown is null ? null : JsonValue.Create(stepdown.Value));
        TrySet("maximumStepover", NumToStringNullable(stepover), false,
            stepover is null ? null : JsonValue.Create(stepover.Value));
        TrySet("tool_feedCutting", NumToStringNullable(feedRate), false,
            feedRate is null ? null : JsonValue.Create(feedRate.Value));
        TrySet("tool_spindleSpeed", NumToStringNullable(spindleSpeed), false,
            spindleSpeed is null ? null : JsonValue.Create(spindleSpeed.Value));
        TrySet("tool_coolant", coolant, true, JsonValue.Create(coolant));

        var result = new JsonObject
        {
            ["name"] = op.Name,
            ["setup"] = setupName,
            ["strategy"] = strategy,
        };

        if (applied.Count > 0)
        {
            result["parameters_applied"] = applied;
        }

        if (failed.Count > 0)
        {
            result["parameters_failed"] = failed;
            result["warning"] =
                "Some parameters are not available for this strategy — " +
                "check available names via cam_get_operation_info";
        }

        return result;
    }

    /// <summary>Reference _set_cam_parameter (setup/operation level, via the facade).</summary>
    private static bool SetCamParameter(IFusionCamSetup setup, string paramName, string expression)
        => setup.SetParameter(paramName, expression);

    private static bool SetCamParameter(IFusionCamOperation operation, string paramName, string expression)
        => operation.SetParameter(paramName, expression);

    private static string? NumToStringNullable(double? value)
        => value is null ? null : value.Value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>.NET 4.8 has no Math.Clamp — reference int(value) + max/min clamping.</summary>
    private static int ClampInt(int value, int min, int max)
        => value < min ? min : value > max ? max : value;

    public static JsonObject CamGenerateToolpath(IFusionContext ctx, JsonObject p)
    {
        var setupName = ParamReader.StrOrNull(p, "setup_name");
        var operationName = ParamReader.StrOrNull(p, "operation_name");
        var generateAll = ParamReader.Bool(p, "generate_all", false);

        var cam = GetCam(ctx);

        if (generateAll)
        {
            WaitCamFuture(ctx, cam.GenerateAllToolpaths(false));
            return new JsonObject
            {
                ["generated"] = true,
                ["scope"] = "all",
            };
        }

        if (operationName is not null && setupName is not null)
        {
            var setup = FindSetup(cam, setupName);
            var op = FindOperation(setup, operationName);
            WaitCamFuture(ctx, cam.GenerateToolpath(op));
            return new JsonObject
            {
                ["generated"] = true,
                ["scope"] = "operation",
                ["operation"] = operationName,
            };
        }

        if (setupName is not null)
        {
            var setup = FindSetup(cam, setupName);
            // The whole setup: the operations collection is itself a target
            // (reference ObjectCollection of all setup operations).
            WaitCamFuture(ctx, cam.GenerateToolpath(setup.Operations));
            return new JsonObject
            {
                ["generated"] = true,
                ["scope"] = "setup",
                ["setup"] = setupName,
            };
        }

        throw new FusionCommandException(
            "Provide setup_name, operation_name, or generate_all=true");
    }

    public static JsonObject CamPostProcess(IFusionContext ctx, JsonObject p)
    {
        var setupName = ParamReader.Str(p, "setup_name", string.Empty);
        var operationName = ParamReader.StrOrNull(p, "operation_name");
        var postProcessor = ParamReader.Str(p, "post_processor", "fanuc");
        var outputFolder = ParamReader.StrOrNull(p, "output_folder");
        var outputUnits = ParamReader.Str(p, "output_units", "mm");

        var cam = GetCam(ctx);
        var setup = FindSetup(cam, setupName);

        if (outputFolder is null)
        {
            outputFolder = Desktop();
        }

        // A full .cps path is used as-is; a short name resolves against the
        // build's legacy local post folder when one exists.
        string postConfig;
        if (postProcessor.EndsWith(".cps", StringComparison.Ordinal)
            || postProcessor.Contains(Path.DirectorySeparatorChar)
            || postProcessor.Contains('/'))
        {
            postConfig = postProcessor;
        }
        else
        {
            var postFolder = cam.GenericPostFolder;
            if (string.IsNullOrEmpty(postFolder))
            {
                throw new FusionCommandException(
                    "This Fusion build has no local generic post folder " +
                    "(posts are cloud-library based). Pass the full path to " +
                    "the .cps file in 'post_processor' instead of a short " +
                    "name.");
            }

            postConfig = Path.Combine(postFolder, $"{postProcessor}.cps");
        }

        if (!File.Exists(postConfig))
        {
            throw new FusionCommandException($"Post processor not found: {postConfig}");
        }

        var units = outputUnits == "mm"
            ? FusionCamOutputUnits.Millimeters
            : FusionCamOutputUnits.Inches;

        var postInput = cam.CreatePostProcessInput(setupName, postConfig, outputFolder, units);
        postInput.OpenInEditor = false;

        if (operationName is not null)
        {
            var op = FindOperation(setup, operationName);
            cam.PostProcess(op, postInput);
        }
        else
        {
            cam.PostProcess(setup, postInput);
        }

        return new JsonObject
        {
            ["setup"] = setupName,
            ["post_processor"] = postProcessor,
            ["output_folder"] = outputFolder,
            ["units"] = outputUnits,
        };
    }
}
