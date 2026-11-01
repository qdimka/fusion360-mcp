using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// T5 command handlers: ports of the reference command_handler.py scene /
/// sketch / design-type / render_view / delete_all / undo
/// methods. Each takes the facade context plus the command's params object
/// and returns the tool-specific result payload (CommandHandler adds the
/// ok marker and mutation deltas). All application errors are
/// <see cref="FusionCommandException" /> with the reference message texts so
/// ErrorClassifier maps them to the same codes.
/// </summary>
public static class T5Handlers
{
    // ------------------------------------------------------------------
    // Shared helpers (reference _camera_info / _bbox_dict / _VIEW_DIRS /
    // _orient_camera)
    // ------------------------------------------------------------------

    /// <summary>Reference _VIEW_DIRS: canonical camera presets (eye_dir, up_vec) in Fusion Z-up world.</summary>
    public static readonly IReadOnlyDictionary<string, (FusionPoint EyeDir, FusionPoint UpVec)> ViewDirs =
        new Dictionary<string, (FusionPoint, FusionPoint)>
        {
            ["iso"] = (new FusionPoint(1, -1, 1), new FusionPoint(0, 0, 1)),
            ["iso_ne"] = (new FusionPoint(1, 1, 1), new FusionPoint(0, 0, 1)),
            ["iso_nw"] = (new FusionPoint(-1, 1, 1), new FusionPoint(0, 0, 1)),
            ["iso_sw"] = (new FusionPoint(-1, -1, 1), new FusionPoint(0, 0, 1)),
            ["front"] = (new FusionPoint(0, -1, 0), new FusionPoint(0, 0, 1)),
            ["back"] = (new FusionPoint(0, 1, 0), new FusionPoint(0, 0, 1)),
            ["top"] = (new FusionPoint(0, 0, 1), new FusionPoint(0, 1, 0)),
            ["bottom"] = (new FusionPoint(0, 0, -1), new FusionPoint(0, 1, 0)),
            ["right"] = (new FusionPoint(1, 0, 0), new FusionPoint(0, 0, 1)),
            ["left"] = (new FusionPoint(-1, 0, 0), new FusionPoint(0, 0, 1)),
        };

    /// <summary>Reference _camera_info: {eye, target, up_vector} or null when the viewport has no camera.</summary>
    public static JsonObject? CameraInfo(IFusionContext ctx)
    {
        var camera = ctx.Viewport?.Camera;
        if (camera is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["eye"] = PointArray(camera.Eye),
            ["target"] = PointArray(camera.Target),
            ["up_vector"] = PointArray(camera.UpVector),
        };
    }

    /// <summary>Reference _bbox_dict: {min, max} arrays, or null.</summary>
    public static JsonNode? BboxJson(IFusionBoundingBox? box)
    {
        if (box is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["min"] = PointArray(box.Min),
            ["max"] = PointArray(box.Max),
        };
    }

    private static JsonArray PointArray(FusionPoint p)
        => new() { p.X, p.Y, p.Z };

    /// <summary>Reference _orient_camera: place the camera at a preset view relative to the model centroid.</summary>
    private static void OrientCamera(IFusionViewport viewport, (FusionPoint EyeDir, FusionPoint UpVec) spec, IFusionRootComponent? root)
    {
        var target = new FusionPoint(0, 0, 0);
        var distance = 20.0;

        if (root is not null && root.BRepBodies.Count > 0)
        {
            try
            {
                var bbox = root.BoundingBox;
                if (bbox is not null)
                {
                    target = new FusionPoint(
                        (bbox.Min.X + bbox.Max.X) * 0.5,
                        (bbox.Min.Y + bbox.Max.Y) * 0.5,
                        (bbox.Min.Z + bbox.Max.Z) * 0.5);
                    var dx = bbox.Max.X - bbox.Min.X;
                    var dy = bbox.Max.Y - bbox.Min.Y;
                    var dz = bbox.Max.Z - bbox.Min.Z;
                    distance = Math.Max(dx, Math.Max(dy, Math.Max(dz, 1.0))) * 2.5;
                }
            }
            catch
            {
                // Best-effort orientation (reference try/except pass).
            }
        }

        var eye = new FusionPoint(
            target.X + spec.EyeDir.X * distance,
            target.Y + spec.EyeDir.Y * distance,
            target.Z + spec.EyeDir.Z * distance);
        var up = spec.UpVec;

        var cam = viewport.Camera;
        cam.Eye = eye;
        cam.Target = target;
        cam.UpVector = up;
        cam.IsSmoothTransition = false;
        viewport.Camera = cam;
    }

    // ------------------------------------------------------------------
    // Scene / query
    // ------------------------------------------------------------------

    public static JsonObject GetSceneInfo(IFusionContext ctx, JsonObject p)
    {
        var design = ctx.Design;
        var root = design.RootComponent;

        var bodies = new JsonArray();
        var reportedBodies = new List<string>();
        for (var i = 0; i < root.BRepBodies.Count; i++)
        {
            var b = root.BRepBodies.Item(i);
            bodies.Add(new JsonObject
            {
                ["name"] = b.Name,
                ["volume"] = b.Volume,
                ["area"] = b.Area,
                ["material"] = b.MaterialName,
                ["is_visible"] = b.IsVisible,
            });
            reportedBodies.Add(b.Name);
        }

        var sketches = new JsonArray();
        var reportedSketches = new List<string>();
        for (var i = 0; i < root.Sketches.Count; i++)
        {
            var s = root.Sketches.Item(i);
            sketches.Add(new JsonObject
            {
                ["name"] = s.Name,
                ["profile_count"] = s.ProfilesCount,
                ["is_visible"] = s.IsVisible,
            });
            reportedSketches.Add(s.Name);
        }

        // P3 (ADR-0011): the names this read reports are now part of the
        // agent's context — if they later stop resolving, the lookups
        // classify as STALE_REFERENCE instead of NOT_FOUND.
        var allNames = new List<string>(reportedBodies.Count + reportedSketches.Count);
        allNames.AddRange(reportedBodies);
        allNames.AddRange(reportedSketches);
        ModelRevision.RecordReportedNames(design.DocumentIdentity, allNames);

        return new JsonObject
        {
            ["design_name"] = design.DocumentName,
            ["design_type"] = design.ProductType,
            ["bodies"] = bodies,
            ["sketches"] = sketches,
            ["bodies_count"] = root.BRepBodies.Count,
            ["sketches_count"] = root.Sketches.Count,
            ["features_count"] = root.FeaturesCount,
            ["timeline_count"] = design.Timeline?.Count ?? 0,
            ["camera"] = CameraInfo(ctx),
            // P3 (ADR-0011): the document revision the caller should echo
            // back as expected_revision on the next mutation.
            ["model_revision"] = ModelRevision.Current(design.DocumentIdentity),
        };
    }

    public static JsonObject GetObjectInfo(IFusionContext ctx, JsonObject p)
    {
        var name = p["name"]?.GetValue<string>() ?? string.Empty;
        var root = ctx.Design.RootComponent;

        for (var i = 0; i < root.BRepBodies.Count; i++)
        {
            var b = root.BRepBodies.Item(i);
            if (b.Name == name)
            {
                ModelRevision.RecordReportedNames(ctx.Design.DocumentIdentity, new[] { name });
                return new JsonObject
                {
                    ["found"] = true,
                    ["type"] = "body",
                    ["name"] = name,
                    ["volume"] = b.Volume,
                    ["area"] = b.Area,
                    ["material"] = b.MaterialName,
                    ["is_visible"] = b.IsVisible,
                    ["faces_count"] = b.FacesCount,
                    ["edges_count"] = b.EdgesCount,
                    ["vertices_count"] = b.VerticesCount,
                    ["bounding_box"] = BboxJson(b.BoundingBox),
                    ["model_revision"] = ModelRevision.Current(ctx.Design.DocumentIdentity),
                };
            }
        }

        for (var i = 0; i < root.Sketches.Count; i++)
        {
            var s = root.Sketches.Item(i);
            if (s.Name == name)
            {
                ModelRevision.RecordReportedNames(ctx.Design.DocumentIdentity, new[] { name });
                return new JsonObject
                {
                    ["found"] = true,
                    ["type"] = "sketch",
                    ["name"] = name,
                    ["is_visible"] = s.IsVisible,
                    ["profile_count"] = s.ProfilesCount,
                    ["curve_count"] = s.SketchCurves.Count,
                    ["model_revision"] = ModelRevision.Current(ctx.Design.DocumentIdentity),
                };
            }
        }

        return new JsonObject
        {
            ["found"] = false,
            ["name"] = name,
            ["model_revision"] = ModelRevision.Current(ctx.Design.DocumentIdentity),
        };
    }

    /// <summary>
    /// Port of reference <c>get_bounding_box</c>: axis-aligned bounding box for a
    /// body or component, values in cm. Tries a body first (root bodies and
    /// occurrence-proxy bodies), then falls back to the union box of all bodies
    /// contained in a component.
    /// </summary>
    public static JsonObject GetBoundingBox(IFusionContext ctx, JsonObject p)
    {
        var name = p["name"]?.GetValue<string>() ?? string.Empty;

        // Try a body first (covers root bodies + bodies inside components).
        try
        {
            var body = DesignQueries.BodyByName(ctx, name);
            var bb = body.BoundingBox;
            if (bb is not null)
            {
                return BoundingBoxPayload("body", name, bb.Min, bb.Max);
            }
        }
        catch (FusionCommandException)
        {
            // Not a body — fall through to the component lookup below.
        }

        // Fall back to a component: union bounding box of all contained bodies.
        IFusionComponent comp;
        try
        {
            comp = DesignQueries.ComponentByName(ctx, name);
        }
        catch (FusionCommandException)
        {
            return new JsonObject { ["found"] = false, ["name"] = name };
        }

        var mnX = double.PositiveInfinity;
        var mnY = double.PositiveInfinity;
        var mnZ = double.PositiveInfinity;
        var mxX = double.NegativeInfinity;
        var mxY = double.NegativeInfinity;
        var mxZ = double.NegativeInfinity;
        var any = false;

        void Extend(IFusionBRepBodyCollection bodies)
        {
            for (var i = 0; i < bodies.Count; i++)
            {
                var box = bodies.Item(i).BoundingBox;
                if (box is null)
                {
                    continue;
                }

                if (box.Min.X < mnX) { mnX = box.Min.X; }
                if (box.Min.Y < mnY) { mnY = box.Min.Y; }
                if (box.Min.Z < mnZ) { mnZ = box.Min.Z; }
                if (box.Max.X > mxX) { mxX = box.Max.X; }
                if (box.Max.Y > mxY) { mxY = box.Max.Y; }
                if (box.Max.Z > mxZ) { mxZ = box.Max.Z; }
                any = true;
            }
        }

        Extend(comp.BRepBodies);
        for (var o = 0; o < comp.AllOccurrences.Count; o++)
        {
            Extend(comp.AllOccurrences.Item(o).BRepBodies);
        }

        if (!any)
        {
            return new JsonObject
            {
                ["found"] = true,
                ["type"] = "component",
                ["name"] = name,
                ["empty"] = true,
            };
        }

        return BoundingBoxPayload("component", name,
            new FusionPoint(mnX, mnY, mnZ),
            new FusionPoint(mxX, mxY, mxZ));

        static JsonObject BoundingBoxPayload(string type, string bodyName, FusionPoint mn, FusionPoint mx)
        {
            return new JsonObject
            {
                ["found"] = true,
                ["type"] = type,
                ["name"] = bodyName,
                ["min"] = PointArray(mn),
                ["max"] = PointArray(mx),
                ["size"] = new JsonArray { mx.X - mn.X, mx.Y - mn.Y, mx.Z - mn.Z },
                ["center"] = new JsonArray
                {
                    (mn.X + mx.X) * 0.5,
                    (mn.Y + mx.Y) * 0.5,
                    (mn.Z + mx.Z) * 0.5,
                },
            };
        }
    }

    public static JsonObject ListComponents(IFusionContext ctx, JsonObject p)
    {
        var root = ctx.Design.RootComponent;
        var components = new JsonArray
        {
            new JsonObject { ["name"] = root.Name, ["is_root"] = true },
        };

        for (var i = 0; i < root.AllOccurrences.Count; i++)
        {
            var occ = root.AllOccurrences.Item(i);
            components.Add(new JsonObject
            {
                ["name"] = occ.Component.Name,
                ["is_root"] = false,
                ["is_visible"] = occ.IsVisible,
            });
        }

        return new JsonObject
        {
            ["components"] = components,
            ["count"] = components.Count,
        };
    }

    public static JsonObject GetParameters(IFusionContext ctx, JsonObject p)
    {
        var design = ctx.Design;
        var parameters = new JsonArray();
        foreach (var param in design.UserParameters)
        {
            parameters.Add(new JsonObject
            {
                ["name"] = param.Name,
                ["value"] = param.Value,
                ["expression"] = param.Expression,
                ["unit"] = param.Unit,
                ["comment"] = param.Comment,
            });
        }

        return new JsonObject
        {
            ["parameters"] = parameters,
            ["count"] = parameters.Count,
        };
    }

    // ------------------------------------------------------------------
    // Sketch
    // ------------------------------------------------------------------

    public static JsonObject CreateSketch(IFusionContext ctx, JsonObject p)
    {
        var plane = ParamReader.Str(p, "plane", "xy");
        var zOffset = ParamReader.NumOrNull(p, "z_offset");
        var root = ctx.Design.RootComponent;
        var basePlane = DesignQueries.ConstructionPlane(root, plane);

        IFusionConstructionPlane host = basePlane;
        if (zOffset is not null && zOffset.Value != 0.0)
        {
            host = root.CreateOffsetPlane(basePlane, zOffset.Value);
        }

        var sketch = root.Sketches.Add(host);
        return new JsonObject
        {
            ["sketch_name"] = sketch.Name,
            ["plane"] = plane,
            ["z_offset"] = zOffset,
        };
    }

    public static JsonObject DrawRectangle(IFusionContext ctx, JsonObject p)
    {
        var sketch = DesignQueries.LastSketch(ctx);
        var width = ParamReader.Num(p, "width", 0);
        var height = ParamReader.Num(p, "height", 0);
        var ox = ParamReader.Num(p, "origin_x", 0);
        var oy = ParamReader.Num(p, "origin_y", 0);
        var oz = ParamReader.Num(p, "origin_z", 0);

        var p1 = new FusionPoint(ox, oy, oz);
        var p2 = new FusionPoint(ox + width, oy + height, oz);
        sketch.Curves.Lines.AddTwoPointRectangle(p1, p2);
        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["width"] = width,
            ["height"] = height,
        };
    }

    public static JsonObject DrawCircle(IFusionContext ctx, JsonObject p)
    {
        var sketch = DesignQueries.LastSketch(ctx);
        var radius = ParamReader.Num(p, "radius", 0);
        var cx = ParamReader.Num(p, "center_x", 0);
        var cy = ParamReader.Num(p, "center_y", 0);
        var cz = ParamReader.Num(p, "center_z", 0);

        var center = new FusionPoint(cx, cy, cz);
        sketch.Curves.Circles.AddByCenterRadius(center, radius);
        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["radius"] = radius,
            ["center"] = PointArray(center),
        };
    }

    public static JsonObject DrawLine(IFusionContext ctx, JsonObject p)
    {
        var sketch = DesignQueries.LastSketch(ctx);
        var sx = ParamReader.Num(p, "start_x", 0);
        var sy = ParamReader.Num(p, "start_y", 0);
        var sz = ParamReader.Num(p, "start_z", 0);
        var ex = ParamReader.Num(p, "end_x", 0);
        var ey = ParamReader.Num(p, "end_y", 0);
        var ez = ParamReader.Num(p, "end_z", 0);

        var sp = new FusionPoint(sx, sy, sz);
        var ep = new FusionPoint(ex, ey, ez);
        sketch.Curves.Lines.AddByTwoPoints(sp, ep);
        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["start"] = PointArray(sp),
            ["end"] = PointArray(ep),
        };
    }

    public static JsonObject DrawArc(IFusionContext ctx, JsonObject p)
    {
        var sketch = DesignQueries.LastSketch(ctx);
        var cx = ParamReader.Num(p, "center_x", 0);
        var cy = ParamReader.Num(p, "center_y", 0);
        var cz = ParamReader.Num(p, "center_z", 0);
        var sx = ParamReader.Num(p, "start_x", 0);
        var sy = ParamReader.Num(p, "start_y", 0);
        var sz = ParamReader.Num(p, "start_z", 0);
        var sweepAngle = ParamReader.Num(p, "sweep_angle", 0);

        var center = new FusionPoint(cx, cy, cz);
        var start = new FusionPoint(sx, sy, sz);
        var sweepRadians = Math.PI * sweepAngle / 180.0;
        sketch.Curves.Arcs.AddByCenterStartSweep(center, start, sweepRadians);
        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["sweep_angle"] = sweepAngle,
        };
    }

    public static JsonObject DrawSpline(IFusionContext ctx, JsonObject p)
    {
        var sketch = DesignQueries.LastSketch(ctx);
        var splineType = ParamReader.Str(p, "spline_type", "fit_points");
        var degree = ParamReader.Int(p, "degree", 3);
        var raw = ParamReader.ListOrEmpty(p, "points");

        var points = new List<FusionPoint>(raw.Count);
        foreach (var node in raw)
        {
            if (node is JsonArray arr)
            {
                var x = arr[0]?.GetValue<double>() ?? 0;
                var y = arr.Count > 1 ? arr[1]?.GetValue<double>() ?? 0 : 0;
                var z = arr.Count > 2 ? arr[2]?.GetValue<double>() ?? 0 : 0;
                points.Add(new FusionPoint(x, y, z));
            }
        }

        if (splineType == "fit_points")
        {
            sketch.Curves.FittedSplines.Add(points);
        }
        else
        {
            // control_points (reference default branch)
            sketch.Curves.ControlPointSplines.Add(points, degree);
        }

        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["spline_type"] = splineType,
            ["points_count"] = points.Count,
        };
    }

    public static JsonObject CreatePolygon(IFusionContext ctx, JsonObject p)
    {
        var sketch = DesignQueries.LastSketch(ctx);
        var sides = ParamReader.Int(p, "sides", 3);
        var radius = ParamReader.Num(p, "radius", 0);
        var cx = ParamReader.Num(p, "center_x", 0);
        var cy = ParamReader.Num(p, "center_y", 0);
        var cz = ParamReader.Num(p, "center_z", 0);

        for (var i = 0; i < sides; i++)
        {
            var a1 = 2 * Math.PI * i / sides;
            var a2 = 2 * Math.PI * (i + 1) / sides;
            var p1 = new FusionPoint(cx + radius * Math.Cos(a1), cy + radius * Math.Sin(a1), cz);
            var p2 = new FusionPoint(cx + radius * Math.Cos(a2), cy + radius * Math.Sin(a2), cz);
            sketch.Curves.Lines.AddByTwoPoints(p1, p2);
        }

        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["sides"] = sides,
            ["radius"] = radius,
        };
    }

    public static JsonObject AddConstraint(IFusionContext ctx, JsonObject p)
    {
        var constraintType = ParamReader.Str(p, "constraint_type", string.Empty);
        var e1Idx = ParamReader.IntOrNull(p, "entity_one");
        var e2Idx = ParamReader.IntOrNull(p, "entity_two");
        var symmetryLine = ParamReader.IntOrNull(p, "symmetry_line");
        var sketchName = ParamReader.StrOrNull(p, "sketch_name");

        var sketch = sketchName is null
            ? DesignQueries.LastSketch(ctx)
            : DesignQueries.SketchByName(ctx, sketchName);
        var curves = sketch.SketchCurves;
        var c1 = e1Idx is not null ? curves[e1Idx.Value] : null;
        var c2 = e2Idx is not null ? curves[e2Idx.Value] : null;
        var constraints = sketch.Constraints;

        switch (constraintType)
        {
            case "coincident": constraints.AddCoincident(c1, c2); break;
            case "parallel": constraints.AddParallel(c1, c2); break;
            case "perpendicular": constraints.AddPerpendicular(c1, c2); break;
            case "tangent": constraints.AddTangent(c1, c2); break;
            case "equal": constraints.AddEqual(c1, c2); break;
            case "fix": constraints.AddFix(c1); break;
            case "horizontal": constraints.AddHorizontal(c1); break;
            case "vertical": constraints.AddVertical(c1); break;
            case "concentric": constraints.AddConcentric(c1, c2); break;
            case "collinear": constraints.AddCollinear(c1, c2); break;
            case "smooth": constraints.AddSmooth(c1, c2); break;
            case "midpoint":
                constraints.AddMidPoint(sketch.Points.Item(e1Idx!.Value), c2);
                break;
            case "symmetry":
                constraints.AddSymmetry(c1, c2, curves[symmetryLine!.Value]);
                break;
            default:
                throw new FusionCommandException($"Unknown constraint type: {constraintType}");
        }

        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["constraint_type"] = constraintType,
        };
    }

    public static JsonObject AutoConstrain(IFusionContext ctx, JsonObject p)
    {
        var sketchName = ParamReader.StrOrNull(p, "sketch_name");
        var resultOption = ParamReader.Int(p, "result_option", 1);
        var sketch = sketchName is null
            ? DesignQueries.LastSketch(ctx)
            : DesignQueries.SketchByName(ctx, sketchName);

        if (resultOption is not (1 or 2 or 3))
        {
            throw new FusionCommandException(
                $"Invalid result_option {resultOption} — use 1 (thorough), 2 (fast), or 3 (may adjust geometry within tolerance).");
        }

        var result = sketch.AutoConstrain(resultOption);
        if (result is null)
        {
            throw new FusionCommandException(
                "autoConstrain returned no result — the sketch is not eligible for geometry adjustment. " +
                "Retry with result_option 1 or 2.");
        }

        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["is_fully_constrained"] = result.IsFullyConstrained,
            ["constraints_added"] = result.AddedConstraints,
            ["dimensions_added"] = result.AddedDimensions,
            ["entities_moved"] = result.MovedGeometry,
            ["result_option"] = resultOption,
        };
    }

    public static JsonObject AddDimension(IFusionContext ctx, JsonObject p)
    {
        var dimensionType = ParamReader.Str(p, "dimension_type", string.Empty);
        var value = ParamReader.Num(p, "value", 0);
        var e1Idx = ParamReader.IntOrNull(p, "entity_one");
        var e2Idx = ParamReader.IntOrNull(p, "entity_two");
        var sketchName = ParamReader.StrOrNull(p, "sketch_name");

        var sketch = sketchName is null
            ? DesignQueries.LastSketch(ctx)
            : DesignQueries.SketchByName(ctx, sketchName);
        var curves = sketch.SketchCurves;
        var c1 = e1Idx is not null ? curves[e1Idx.Value] : null;
        var c2 = e2Idx is not null ? curves[e2Idx.Value] : null;
        var textPoint = new FusionPoint(0, 0, 0);
        var dims = sketch.Dimensions;

        IFusionSketchDimension dim = dimensionType switch
        {
            "distance" => dims.AddDistanceDimension(c1!, c2!, DimensionOrientations.Aligned, textPoint),
            "horizontal" => dims.AddDistanceDimension(c1!, c2!, DimensionOrientations.Horizontal, textPoint),
            "vertical" => dims.AddDistanceDimension(c1!, c2!, DimensionOrientations.Vertical, textPoint),
            "angular" => dims.AddAngularDimension(c1!, c2!, textPoint),
            "radial" => dims.AddRadialDimension(c1!, textPoint),
            "diameter" => dims.AddDiameterDimension(c1!, textPoint),
            _ => throw new FusionCommandException($"Unknown dimension type: {dimensionType}"),
        };

        dim.SetParameterValue(value);
        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["dimension_type"] = dimensionType,
            ["value"] = value,
        };
    }

    public static JsonObject OffsetCurve(IFusionContext ctx, JsonObject p)
    {
        var curveIndex = ParamReader.Int(p, "curve_index", 0);
        var offsetDistance = ParamReader.Num(p, "offset_distance", 0);
        var dx = ParamReader.Num(p, "direction_x", 1);
        var dy = ParamReader.Num(p, "direction_y", 0);
        var sketchName = ParamReader.StrOrNull(p, "sketch_name");
        var sketch = sketchName is null
            ? DesignQueries.LastSketch(ctx)
            : DesignQueries.SketchByName(ctx, sketchName);

        var curve = sketch.SketchCurves[curveIndex];
        sketch.Offset(curve, new FusionPoint(dx, dy, 0), offsetDistance);
        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["offset_distance"] = offsetDistance,
        };
    }

    public static JsonObject TrimCurve(IFusionContext ctx, JsonObject p)
    {
        var curveIndex = ParamReader.Int(p, "curve_index", 0);
        var px = ParamReader.Num(p, "point_x", 0);
        var py = ParamReader.Num(p, "point_y", 0);
        var sketchName = ParamReader.StrOrNull(p, "sketch_name");
        var sketch = sketchName is null
            ? DesignQueries.LastSketch(ctx)
            : DesignQueries.SketchByName(ctx, sketchName);

        sketch.SketchCurves[curveIndex].Trim(new FusionPoint(px, py, 0));
        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["trimmed"] = true,
        };
    }

    public static JsonObject ExtendCurve(IFusionContext ctx, JsonObject p)
    {
        var curveIndex = ParamReader.Int(p, "curve_index", 0);
        var px = ParamReader.Num(p, "point_x", 0);
        var py = ParamReader.Num(p, "point_y", 0);
        var sketchName = ParamReader.StrOrNull(p, "sketch_name");
        var sketch = sketchName is null
            ? DesignQueries.LastSketch(ctx)
            : DesignQueries.SketchByName(ctx, sketchName);

        sketch.SketchCurves[curveIndex].Extend(new FusionPoint(px, py, 0));
        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["extended"] = true,
        };
    }

    public static JsonObject ProjectGeometry(IFusionContext ctx, JsonObject p)
    {
        var sourceName = ParamReader.Str(p, "source_name", string.Empty);
        var sketchName = ParamReader.StrOrNull(p, "sketch_name");
        var sketch = sketchName is null
            ? DesignQueries.LastSketch(ctx)
            : DesignQueries.SketchByName(ctx, sketchName);
        var body = DesignQueries.BodyByName(ctx, sourceName);

        var projected = 0;
        foreach (var edge in body.Edges)
        {
            projected += sketch.ProjectEdge(edge);
        }

        return new JsonObject
        {
            ["sketch"] = sketch.Name,
            ["source"] = sourceName,
            ["projected_curves"] = projected,
        };
    }

    // ------------------------------------------------------------------
    // Design type safety
    // ------------------------------------------------------------------

    public static JsonObject GetDesignType(IFusionContext ctx, JsonObject p)
    {
        var dt = ctx.Design.DesignType;
        return new JsonObject
        {
            ["design_type"] = DesignTypes.Label(dt),
            ["design_type_id"] = dt,
        };
    }

    public static JsonObject SetDesignType(IFusionContext ctx, JsonObject p)
    {
        var designType = ParamReader.Str(p, "design_type", string.Empty);
        var design = ctx.Design;
        var current = design.DesignType;

        if (designType == "parametric")
        {
            var target = DesignTypes.Parametric;
            if (current == target)
            {
                return new JsonObject
                {
                    ["changed"] = false,
                    ["design_type"] = "parametric",
                    ["message"] = "Already in parametric mode",
                };
            }

            design.DesignType = target;
            ctx.DoEvents();
            if (design.DesignType != target)
            {
                throw new FusionCommandException(
                    "Failed to switch to parametric mode. Try 'Capture Design History' in the Fusion UI.");
            }

            return new JsonObject { ["changed"] = true, ["design_type"] = "parametric" };
        }

        if (designType == "direct")
        {
            var target = DesignTypes.Direct;
            if (current == target)
            {
                return new JsonObject
                {
                    ["changed"] = false,
                    ["design_type"] = "direct",
                    ["message"] = "Already in direct mode",
                };
            }

            design.DesignType = target;
            ctx.DoEvents();
            return new JsonObject { ["changed"] = true, ["design_type"] = "direct" };
        }

        throw new FusionCommandException($"Invalid design_type '{designType}'. Use 'parametric' or 'direct'.");
    }

    // ------------------------------------------------------------------
    // Scene-wide
    // ------------------------------------------------------------------

    public static JsonObject DeleteAll(IFusionContext ctx, JsonObject p)
    {
        var design = ctx.Design;
        var root = design.RootComponent;
        var deleted = new Dictionary<string, int>
        {
            ["timeline"] = 0, ["joints"] = 0, ["occurrences"] = 0,
            ["construction"] = 0, ["bodies"] = 0, ["sketches"] = 0,
        };
        var errors = new JsonArray();

        void Sweep(IFusionDeletableCollection collection, string stage, string counter)
        {
            for (var i = collection.Count - 1; i >= 0; i--)
            {
                var name = "?";
                try
                {
                    var item = collection.Item(i);
                    name = item.Name;
                    item.DeleteMe();
                    deleted[counter]++;
                }
                catch (Exception ex)
                {
                    errors.Add(new JsonObject
                    {
                        ["stage"] = stage,
                        ["index"] = i,
                        ["name"] = name,
                        ["error"] = $"{ex.GetType().Name}: {ex.Message}",
                    });
                }
            }
        }

        // Parametric: unwind newest-first. TimelineObject has no deleteMe —
        // that method lives on the wrapped entity.
        var timeline = design.Timeline;
        if (timeline is not null && timeline.Count > 0)
        {
            for (var i = timeline.Count - 1; i >= 0; i--)
            {
                var name = "?";
                try
                {
                    var item = timeline.Item(i);
                    name = item.Name;
                    var entity = item.Entity;
                    if (entity is null)
                    {
                        errors.Add(new JsonObject
                        {
                            ["stage"] = "timeline",
                            ["index"] = i,
                            ["name"] = name,
                            ["error"] = "timeline item exposes no entity",
                        });
                        continue;
                    }

                    entity.DeleteMe();
                    deleted["timeline"]++;
                }
                catch (Exception ex)
                {
                    errors.Add(new JsonObject
                    {
                        ["stage"] = "timeline",
                        ["index"] = i,
                        ["name"] = name,
                        ["error"] = $"{ex.GetType().Name}: {ex.Message}",
                    });
                }
            }
        }

        // Direct mode: the sweeps below also catch anything the timeline
        // pass could not remove. Joints go before occurrences.
        Sweep(root.RigidGroups, "rigid_group", "joints");
        Sweep(root.AsBuiltJoints, "as_built_joint", "joints");
        Sweep(root.Joints, "joint", "joints");
        Sweep(root.Occurrences, "occurrence", "occurrences");
        Sweep(root.ConstructionPlanes, "construction_plane", "construction");
        Sweep(root.ConstructionAxes, "construction_axis", "construction");
        Sweep(root.ConstructionPoints, "construction_point", "construction");

        for (var i = root.BRepBodies.Count - 1; i >= 0; i--)
        {
            var name = "?";
            try
            {
                var body = root.BRepBodies.Item(i);
                name = body.Name;
                body.DeleteMe();
                deleted["bodies"]++;
            }
            catch (Exception ex)
            {
                errors.Add(new JsonObject
                {
                    ["stage"] = "body",
                    ["index"] = i,
                    ["name"] = name,
                    ["error"] = $"{ex.GetType().Name}: {ex.Message}",
                });
            }
        }

        for (var i = root.Sketches.Count - 1; i >= 0; i--)
        {
            var name = "?";
            try
            {
                var sk = root.Sketches.Item(i);
                name = sk.Name;
                sk.DeleteMe();
                deleted["sketches"]++;
            }
            catch (Exception ex)
            {
                errors.Add(new JsonObject
                {
                    ["stage"] = "sketch",
                    ["index"] = i,
                    ["name"] = name,
                    ["error"] = $"{ex.GetType().Name}: {ex.Message}",
                });
            }
        }

        var remaining = new JsonObject
        {
            ["bodies"] = root.BRepBodies.Count,
            ["sketches"] = root.Sketches.Count,
            ["occurrences"] = root.Occurrences.Count,
            ["joints"] = root.Joints.Count + root.AsBuiltJoints.Count + root.RigidGroups.Count,
            ["construction"] = root.ConstructionPlanes.Count + root.ConstructionAxes.Count + root.ConstructionPoints.Count,
            ["timeline"] = timeline?.Count ?? 0,
        };

        if (remaining["bodies"]!.GetValue<int>() != 0
            || remaining["sketches"]!.GetValue<int>() != 0
            || remaining["occurrences"]!.GetValue<int>() != 0
            || remaining["joints"]!.GetValue<int>() != 0
            || remaining["construction"]!.GetValue<int>() != 0)
        {
            throw new FusionCommandException(
                $"delete_all did not clear the design — remaining: {remaining.ToJsonString()}. " +
                $"Deleted: {deleted}. Failures: {errors.ToJsonString()}");
        }

        var deletedJson = new JsonObject();
        foreach (var kv in deleted)
        {
            deletedJson[kv.Key] = kv.Value;
        }

        return new JsonObject
        {
            ["deleted"] = deletedJson,
            ["remaining"] = remaining,
            ["errors"] = errors,
        };
    }

    public static JsonObject Undo(IFusionContext ctx, JsonObject p)
    {
        var design = ctx.Design;
        var typeBefore = design.DesignType;

        var cmdDef = ctx.Ui.ItemById("UndoCommand");
        if (cmdDef is null)
        {
            throw new FusionCommandException(
                "Undo command is not available in the current workspace. " +
                "The design was NOT modified — delete the failed feature explicitly instead.");
        }

        cmdDef.Execute();
        ctx.DoEvents();

        var typeAfter = design.DesignType;
        if (typeBefore != typeAfter)
        {
            var redoDef = ctx.Ui.ItemById("RedoCommand");
            if (redoDef is not null)
            {
                redoDef.Execute();
                ctx.DoEvents();
            }

            string Label(int id) => id == DesignTypes.Parametric ? "Parametric" : "Direct";
            throw new FusionCommandException(
                $"Undo aborted: would have changed design type from {Label(typeBefore)} to {Label(typeAfter)}. " +
                "The undo was automatically reversed (redo). Delete the failed feature explicitly instead.");
        }

        return new JsonObject
        {
            ["undone"] = true,
            ["design_type"] = typeAfter,
        };
    }

    // ------------------------------------------------------------------
    // Perception
    // ------------------------------------------------------------------

    public static JsonObject RenderView(IFusionContext ctx, JsonObject p)
    {
        var view = ParamReader.Str(p, "view", "current");
        var width = ParamReader.Int(p, "width", 1024);
        var height = ParamReader.Int(p, "height", 768);
        var fit = ParamReader.Bool(p, "fit", true);

        var viewport = ctx.Viewport;
        if (viewport is null)
        {
            throw new FusionCommandException("No active viewport");
        }

        // Clamp — an oversized capture would stall the main thread and
        // produce an enormous base64 payload.
        width = Math.Min(Math.Max(width, 16), 4096);
        height = Math.Min(Math.Max(height, 16), 4096);

        var repositioned = view != "current";
        if (repositioned)
        {
            if (!ViewDirs.TryGetValue(view, out var spec))
            {
                throw new FusionCommandException(
                    $"Unknown view '{view}'. Expected: current, {string.Join(", ", ViewDirs.Keys)}");
            }

            var cam = viewport.Camera;
            var origEye = cam.Eye;
            var origTarget = cam.Target;
            var origUp = cam.UpVector;
            var origType = cam.CameraType;
            OrientCamera(viewport, spec, ctx.Design.RootComponent);

            var data = Array.Empty<byte>();
            try
            {
                Capture(viewport, width, height, fit, out data);
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
                try
                {
                    viewport.Refresh();
                }
                catch
                {
                    // Best-effort refresh.
                }
            }

            return ImagePayload(view, width, height, data);
        }

        Capture(viewport, width, height, fit, out var image);
        return ImagePayload(view, width, height, image);
    }

    private static void Capture(IFusionViewport viewport, int width, int height, bool fit, out byte[] data)
    {
        if (fit)
        {
            try
            {
                viewport.Fit();
            }
            catch
            {
                // fit() can fail on empty designs; keep going.
            }
        }

        var path = Path.Combine(Path.GetTempPath(), "fusion_render_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var ok = viewport.SaveAsImageFile(path, width, height);
            if (!ok || !File.Exists(path))
            {
                throw new FusionCommandException("saveAsImageFile returned false");
            }

            data = File.ReadAllBytes(path);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private static JsonObject ImagePayload(string view, int width, int height, byte[] data) => new()
    {
        ["view"] = view,
        ["width"] = width,
        ["height"] = height,
        ["image_format"] = "png",
        ["image_base64"] = Convert.ToBase64String(data),
        ["bytes"] = data.Length,
    };
}
