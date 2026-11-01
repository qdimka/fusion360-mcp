using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Units;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// T6 command handlers: ports of the reference command_handler.py
/// Features / Body Operations / Direct Primitives / Surface Operations
/// sections (extrude … trim_surface, 30 commands). Each takes the facade
/// context plus the command's params object and returns the tool-specific
/// result payload (CommandHandler adds the ok marker and mutation deltas).
/// All application errors are <see cref="FusionCommandException" /> with
/// the reference message texts so ErrorClassifier maps them to the same
/// codes (BOOLEAN_NO_OP / SELF_INTERSECTION / REGEN_FAILED / …).
/// </summary>
public static class T6Handlers
{
    // ------------------------------------------------------------------
    // Shared helpers (reference _operation_type / _select_edges /
    // _select_faces / _construction_axis)
    // ------------------------------------------------------------------

    /// <summary>Port of reference <c>_operation_type</c> string table.</summary>
    public static FusionFeatureOperation OperationType(string name) => name switch
    {
        "new_body" => FusionFeatureOperation.NewBody,
        "join" => FusionFeatureOperation.Join,
        "cut" => FusionFeatureOperation.Cut,
        "intersect" => FusionFeatureOperation.Intersect,
        _ => throw new FusionCommandException($"Unknown operation '{name}' — use new_body/join/cut/intersect"),
    };

    /// <summary>Port of reference <c>_construction_axis</c>: x/y/z only, with the reference error text.</summary>
    public static IFusionConstructionAxis ConstructionAxis(IFusionRootComponent root, string axis) => axis switch
    {
        "x" => root.XConstructionAxis,
        "y" => root.YConstructionAxis,
        "z" => root.ZConstructionAxis,
        _ => throw new FusionCommandException($"Unknown axis '{axis}' — use x, y, or z"),
    };

    /// <summary>Port of reference <c>_select_edges</c> (all/top/bottom/vertical).</summary>
    public static IReadOnlyList<IFusionBodyEdge> SelectEdges(IFusionBody body, string selection)
    {
        var result = new List<IFusionBodyEdge>();
        var bbox = body.BoundingBox;

        if (selection == "all")
        {
            result.AddRange(body.Edges);
        }
        else if (selection is "top" or "bottom")
        {
            if (bbox is null)
            {
                throw new FusionCommandException($"No bounding box on body '{body.Name}' — cannot select {selection} edges");
            }

            if (selection == "top")
            {
                var threshold = bbox.Max.Z - 0.001;
                foreach (var edge in body.Edges)
                {
                    if (edge.PointOnEdge.Z > threshold)
                    {
                        result.Add(edge);
                    }
                }
            }
            else
            {
                var threshold = bbox.Min.Z + 0.001;
                foreach (var edge in body.Edges)
                {
                    if (edge.PointOnEdge.Z < threshold)
                    {
                        result.Add(edge);
                    }
                }
            }
        }
        else if (selection == "vertical")
        {
            foreach (var edge in body.Edges)
            {
                var sp = edge.StartVertex;
                var ep = edge.EndVertex;
                if (Math.Abs(sp.X - ep.X) < 0.001 && Math.Abs(sp.Y - ep.Y) < 0.001)
                {
                    result.Add(edge);
                }
            }
        }
        else
        {
            throw new FusionCommandException($"Unknown edge_selection '{selection}' — use all/top/bottom/vertical");
        }

        if (result.Count == 0)
        {
            throw new FusionCommandException($"No edges matched selection '{selection}'");
        }

        return result;
    }

    /// <summary>Port of reference <c>_select_faces</c> (all/top/bottom/vertical).</summary>
    public static IReadOnlyList<IFusionFace> SelectFaces(IFusionBody body, string selection)
    {
        var result = new List<IFusionFace>();
        var bbox = body.BoundingBox;

        if (selection == "all")
        {
            result.AddRange(body.Faces);
        }
        else if (selection is "top" or "bottom")
        {
            if (bbox is null)
            {
                throw new FusionCommandException($"No bounding box on body '{body.Name}' — cannot select {selection} faces");
            }

            var threshold = selection == "top" ? bbox.Max.Z - 0.001 : bbox.Min.Z + 0.001;
            foreach (var face in body.Faces)
            {
                var fbox = face.BoundingBox;
                if (fbox is null)
                {
                    continue;
                }

                var matches = selection == "top" ? fbox.Max.Z > threshold : fbox.Min.Z < threshold;
                if (matches)
                {
                    result.Add(face);
                }
            }
        }
        else if (selection == "vertical")
        {
            foreach (var face in body.Faces)
            {
                // Vertical face: outward normal roughly horizontal. The
                // evaluator read can fail (reference try/except pass).
                var (ok, normal) = face.GetNormalAtPoint(face.PointOnFace);
                if (ok && Math.Abs(normal.Z) < 0.1)
                {
                    result.Add(face);
                }
            }
        }
        else
        {
            throw new FusionCommandException($"Unknown face_selection '{selection}' — use all/top/bottom/vertical");
        }

        if (result.Count == 0)
        {
            throw new FusionCommandException($"No faces matched selection '{selection}'");
        }

        return result;
    }

    /// <summary>Reference body-by-name-or-index pattern (<c>if body_name else item(body_index)</c>).</summary>
    private static IFusionBody BodyByNameOrIndex(IFusionContext ctx, string? bodyName, int bodyIndex)
        => string.IsNullOrEmpty(bodyName)
            ? ctx.Design.RootComponent.BRepBodies.Item(bodyIndex)
            : DesignQueries.BodyByName(ctx, bodyName!);

    // ------------------------------------------------------------------
    // Features
    // ------------------------------------------------------------------

    public static JsonObject Extrude(IFusionContext ctx, JsonObject p)
    {
        var height = ParamReader.Num(p, "height", 0);
        var profileIndex = ParamReader.Int(p, "profile_index", 0);
        var operation = ParamReader.Str(p, "operation", "new_body");
        var direction = ParamReader.Str(p, "direction", "positive");

        var root = ctx.Design.RootComponent;
        var sketch = DesignQueries.LastSketch(ctx);
        if (sketch.ProfilesCount == 0)
        {
            throw new FusionCommandException("No profiles in sketch");
        }

        var profile = sketch.Profiles.Item(profileIndex);
        var input = root.Features.ExtrudeFeatures.CreateInput(profile, OperationType(operation));
        if (direction == "symmetric")
        {
            input.SetSymmetricExtent(height, true);
        }
        else
        {
            input.SetDistanceExtent(direction == "negative", height);
        }

        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["height"] = height,
            ["operation"] = operation,
            ["direction"] = direction,
        };
    }

    public static JsonObject Revolve(IFusionContext ctx, JsonObject p)
    {
        var angle = ParamReader.Num(p, "angle", 0);
        var profileIndex = ParamReader.Int(p, "profile_index", 0);
        var aoX = ParamReader.Num(p, "axis_origin_x", 0);
        var aoY = ParamReader.Num(p, "axis_origin_y", 0);
        var aoZ = ParamReader.Num(p, "axis_origin_z", 0);
        var dirX = ParamReader.Num(p, "axis_direction_x", 1);
        var dirY = ParamReader.Num(p, "axis_direction_y", 0);
        var dirZ = ParamReader.Num(p, "axis_direction_z", 0);
        var operation = ParamReader.Str(p, "operation", "new_body");

        var root = ctx.Design.RootComponent;
        var sketch = DesignQueries.LastSketch(ctx);
        if (sketch.ProfilesCount == 0)
        {
            throw new FusionCommandException("No profiles in sketch");
        }

        var profile = sketch.Profiles.Item(profileIndex);

        // Determine the axis entity first (required for createInput): a
        // default construction axis when the direction is canonical,
        // otherwise a construction line in the sketch (reference fallback).
        var isX = Math.Abs(dirX) > 0.99 && Math.Abs(dirY) < 0.01;
        var isY = Math.Abs(dirY) > 0.99 && Math.Abs(dirX) < 0.01;
        var isZ = Math.Abs(dirZ) > 0.99 && Math.Abs(dirX) < 0.01;
        IFusionRevolveAxis axisEntity;
        if (isX && Math.Abs(dirZ) < 0.01)
        {
            axisEntity = root.XConstructionAxis;
        }
        else if (isY && Math.Abs(dirZ) < 0.01)
        {
            axisEntity = root.YConstructionAxis;
        }
        else if (isZ && Math.Abs(dirY) < 0.01)
        {
            axisEntity = root.ZConstructionAxis;
        }
        else
        {
            var origin = new FusionPoint(aoX, aoY, aoZ);
            var end = new FusionPoint(aoX + dirX * 10, aoY + dirY * 10, aoZ + dirZ * 10);
            var line = sketch.Curves.Lines.AddByTwoPoints(origin, end);
            line.MarkAsConstruction();
            axisEntity = line;
        }

        var input = root.Features.RevolveFeatures.CreateInput(profile, axisEntity, OperationType(operation));
        input.SetAngleExtent(false, angle);
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["angle"] = angle,
            ["operation"] = operation,
        };
    }

    public static JsonObject Sweep(IFusionContext ctx, JsonObject p)
    {
        var profileIndex = ParamReader.Int(p, "profile_index", 0);
        var pathSketchName = ParamReader.Str(p, "path_sketch_name", string.Empty);
        var pathCurveIndex = ParamReader.Int(p, "path_curve_index", 0);
        var operation = ParamReader.Str(p, "operation", "new_body");

        var root = ctx.Design.RootComponent;
        var sketch = DesignQueries.LastSketch(ctx);
        var pathSketch = DesignQueries.SketchByName(ctx, pathSketchName);
        if (sketch.ProfilesCount == 0)
        {
            throw new FusionCommandException("No profiles in sketch");
        }

        var profile = sketch.Profiles.Item(profileIndex);
        var pathCurve = pathSketch.SketchCurves[pathCurveIndex];
        var path = root.Features.CreatePath(pathCurve);

        var input = root.Features.SweepFeatures.CreateInput(profile, path, OperationType(operation));
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["operation"] = operation,
        };
    }

    public static JsonObject Loft(IFusionContext ctx, JsonObject p)
    {
        var profileSketchNames = ParamReader.StringList(p, "profile_sketch_names");
        var operation = ParamReader.Str(p, "operation", "new_body");

        var root = ctx.Design.RootComponent;
        var input = root.Features.LoftFeatures.CreateInput(OperationType(operation));
        foreach (var sketchName in profileSketchNames)
        {
            var sketch = DesignQueries.SketchByName(ctx, sketchName);
            if (sketch.ProfilesCount == 0)
            {
                throw new FusionCommandException($"No profiles in sketch '{sketchName}'");
            }

            input.AddSection(sketch.Profiles.Item(0));
        }

        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["operation"] = operation,
            ["profile_count"] = profileSketchNames.Length,
        };
    }

    public static JsonObject Fillet(IFusionContext ctx, JsonObject p)
    {
        var radius = ParamReader.Num(p, "radius", 0);
        var bodyName = ParamReader.StrOrNull(p, "body_name");
        var bodyIndex = ParamReader.Int(p, "body_index", 0);
        var edgeSelection = ParamReader.Str(p, "edge_selection", "all");

        var root = ctx.Design.RootComponent;
        var body = BodyByNameOrIndex(ctx, bodyName, bodyIndex);
        var edges = SelectEdges(body, edgeSelection);

        var input = root.Features.FilletFeatures.CreateInput();
        input.AddConstantRadiusEdgeSet(edges, radius, true);
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["radius"] = radius,
            ["edges_count"] = edges.Count,
        };
    }

    public static JsonObject Chamfer(IFusionContext ctx, JsonObject p)
    {
        var distance = ParamReader.Num(p, "distance", 0);
        var bodyName = ParamReader.StrOrNull(p, "body_name");
        var bodyIndex = ParamReader.Int(p, "body_index", 0);
        var edgeSelection = ParamReader.Str(p, "edge_selection", "all");

        var root = ctx.Design.RootComponent;
        var body = BodyByNameOrIndex(ctx, bodyName, bodyIndex);
        var edges = SelectEdges(body, edgeSelection);

        var input = root.Features.ChamferFeatures.CreateInput(edges, true);
        input.SetToEqualDistance(distance);
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["distance"] = distance,
            ["edges_count"] = edges.Count,
        };
    }

    public static JsonObject Shell(IFusionContext ctx, JsonObject p)
    {
        var thickness = ParamReader.Num(p, "thickness", 0);
        var bodyName = ParamReader.StrOrNull(p, "body_name");
        var bodyIndex = ParamReader.Int(p, "body_index", 0);
        var faceSelection = ParamReader.Str(p, "face_selection", "top");

        var root = ctx.Design.RootComponent;
        var body = BodyByNameOrIndex(ctx, bodyName, bodyIndex);

        // Reference inlines the top/bottom face pick (no "all"/"vertical").
        var faces = new List<IFusionFace>();
        var bbox = body.BoundingBox;
        if (faceSelection == "top")
        {
            if (bbox is null)
            {
                throw new FusionCommandException($"No bounding box on body '{body.Name}' — cannot select top faces");
            }

            var threshold = bbox.Max.Z - 0.001;
            foreach (var face in body.Faces)
            {
                if (face.BoundingBox is { } fbox && fbox.Max.Z > threshold)
                {
                    faces.Add(face);
                }
            }
        }
        else if (faceSelection == "bottom")
        {
            if (bbox is null)
            {
                throw new FusionCommandException($"No bounding box on body '{body.Name}' — cannot select bottom faces");
            }

            var threshold = bbox.Min.Z + 0.001;
            foreach (var face in body.Faces)
            {
                if (face.BoundingBox is { } fbox && fbox.Min.Z < threshold)
                {
                    faces.Add(face);
                }
            }
        }
        else
        {
            throw new FusionCommandException($"Unknown face_selection '{faceSelection}' — use top/bottom");
        }

        if (faces.Count == 0)
        {
            throw new FusionCommandException($"No faces matched '{faceSelection}'");
        }

        var input = root.Features.ShellFeatures.CreateInput(new[] { body });
        input.FacesToRemove = faces;
        input.InsideThicknessCm = thickness;
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["thickness"] = thickness,
            ["faces_removed"] = faces.Count,
        };
    }

    public static JsonObject Mirror(IFusionContext ctx, JsonObject p)
    {
        var mirrorPlane = ParamReader.Str(p, "mirror_plane", string.Empty);
        var bodyName = ParamReader.StrOrNull(p, "body_name");
        var bodyIndex = ParamReader.Int(p, "body_index", 0);

        var root = ctx.Design.RootComponent;
        var body = BodyByNameOrIndex(ctx, bodyName, bodyIndex);

        var input = root.Features.MirrorFeatures.CreateInput(new[] { body }, DesignQueries.ConstructionPlane(root, mirrorPlane));
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["mirror_plane"] = mirrorPlane,
        };
    }

    public static JsonObject CreateHole(IFusionContext ctx, JsonObject p)
    {
        var diameter = ParamReader.Num(p, "diameter", 0);
        var depth = ParamReader.Num(p, "depth", 0);
        var bodyName = ParamReader.StrOrNull(p, "body_name");
        var bodyIndex = ParamReader.Int(p, "body_index", 0);
        var faceSelection = ParamReader.Str(p, "face_selection", "top");
        var centerX = ParamReader.Num(p, "center_x", 0);
        var centerY = ParamReader.Num(p, "center_y", 0);

        var root = ctx.Design.RootComponent;
        var body = BodyByNameOrIndex(ctx, bodyName, bodyIndex);

        // Find the target face. Comparing bounding boxes is not enough: the
        // side faces of a box reach the body's max Z too, so the first
        // "hit" is usually a vertical face. Require a near-horizontal planar
        // face that points up (or down) — reference create_hole face-rank.
        if (faceSelection is not ("top" or "bottom"))
        {
            throw new FusionCommandException($"Unknown face_selection '{faceSelection}'");
        }

        var wantUp = faceSelection == "top";
        IFusionFace? targetFace = null;
        (double Rank, double Area)? bestKey = null;
        foreach (var face in body.Faces)
        {
            if (face.PlaneGeometry is null)
            {
                continue;
            }

            // Use the evaluator: it reports the face's outward normal,
            // whereas the underlying plane's normal ignores orientation.
            var (ok, normal) = face.GetNormalAtPoint(face.PointOnFace);
            if (!ok || !HoleGeometry.IsHorizontalFace(normal.Z, wantUp))
            {
                continue;
            }

            // Rank on the face's own extreme Z, not on an arbitrary point on
            // it: within the tilt tolerance the two are not the same.
            var fbox = face.BoundingBox;
            if (fbox is null)
            {
                continue;
            }

            var edgeZ = wantUp ? fbox.Max.Z : fbox.Min.Z;
            var key = HoleGeometry.FaceRankKey(edgeZ, face.Area, wantUp);
            if (bestKey is null
                || key.Rank > bestKey.Value.Rank
                || (key.Rank == bestKey.Value.Rank && key.Area > bestKey.Value.Area))
            {
                bestKey = (key.Rank, key.Area);
                targetFace = face;
            }
        }

        if (targetFace is null)
        {
            throw new FusionCommandException(
                $"No near-horizontal {faceSelection}-facing planar face on '{body.Name}'");
        }

        // Place the hole centre. center_x / center_y are model-space XY, so
        // the caller does not have to know the sketch's own coordinate
        // system. Solve the face's plane for Z rather than reusing an
        // arbitrary point on it (reference hole_geometry.plane_z_at).
        var plane = targetFace.PlaneGeometry!;
        var centerZ = HoleGeometry.PlaneZAt(
            (plane.Normal.X, plane.Normal.Y, plane.Normal.Z),
            (plane.Origin.X, plane.Origin.Y, plane.Origin.Z),
            centerX, centerY);

        var sketch = root.Sketches.AddOnFace(targetFace);
        var sketchPt = sketch.AddPointInModelSpace(new FusionPoint(centerX, centerY, centerZ));

        // createSimpleInput() takes the DIAMETER -- not the radius.
        var holeInput = root.Features.HoleFeatures.CreateSimpleInput(diameter);
        holeInput.SetPositionBySketchPoint(sketchPt);
        holeInput.SetDistanceExtent(depth);
        // Without this the hole cuts every body it happens to intersect.
        holeInput.ParticipantBodies = new[] { body };

        var feat = holeInput.Add();
        var cutBodies = new JsonArray();
        foreach (var name in feat.BodyNames)
        {
            cutBodies.Add(name);
        }

        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["diameter"] = diameter,
            ["depth"] = depth,
            ["actual_diameter"] = feat.HoleDiameterCm,
            ["cut_bodies"] = cutBodies,
            // Where the request resolved to, not a measurement of the feature.
            ["resolved_center"] = new JsonArray { centerX, centerY, centerZ },
        };
    }

    public static JsonObject RectangularPattern(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var xCount = ParamReader.Int(p, "x_count", 1);
        var xSpacing = ParamReader.Num(p, "x_spacing", 1.0);
        var yCount = ParamReader.Int(p, "y_count", 1);
        var ySpacing = ParamReader.Num(p, "y_spacing", 1.0);

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);

        var input = root.Features.RectangularPatternFeatures.CreateInput(new[] { body }, root.XConstructionAxis, xCount, xSpacing);
        input.SetDirectionTwo(root.YConstructionAxis, yCount, ySpacing);
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["x_count"] = xCount,
            ["y_count"] = yCount,
        };
    }

    public static JsonObject CircularPattern(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var count = ParamReader.Int(p, "count", 0);
        var axis = ParamReader.Str(p, "axis", "z");
        var totalAngle = ParamReader.Num(p, "total_angle", 360);

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);

        var input = root.Features.CircularPatternFeatures.CreateInput(new[] { body }, ConstructionAxis(root, axis));
        input.Quantity = count;
        input.TotalAngleDegrees = totalAngle;
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["count"] = count,
            ["total_angle"] = totalAngle,
        };
    }

    public static JsonObject CreateThread(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var faceIndex = ParamReader.Int(p, "face_index", 0);
        var isModeled = ParamReader.Bool(p, "is_modeled", false);
        var threadType = ParamReader.Str(p, "thread_type", "ISO Metric profile");
        var isFullLength = ParamReader.Bool(p, "is_full_length", true);
        var threadLength = ParamReader.NumOrNull(p, "thread_length");

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);
        var face = body.Faces[faceIndex];

        var threads = root.Features.ThreadFeatures;
        var threadData = threads.ThreadDataQuery;
        threadData.ThreadType = threadType;

        var input = threads.CreateInput(face, threadData);
        input.IsModeled = isModeled;
        input.IsFullLength = isFullLength;
        if (!isFullLength && threadLength is not null)
        {
            input.ThreadLengthCm = threadLength.Value;
        }

        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["thread_type"] = threadType,
        };
    }

    public static JsonObject DraftFaces(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var angle = ParamReader.Num(p, "angle", 0);
        var faceSelection = ParamReader.Str(p, "face_selection", "vertical");
        var pullDirectionPlane = ParamReader.Str(p, "pull_direction_plane", "xy");
        var isTangentChain = ParamReader.Bool(p, "is_tangent_chain", true);

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);
        var faces = SelectFaces(body, faceSelection);

        var input = root.Features.DraftFeatures.CreateInput(faces, DesignQueries.ConstructionPlane(root, pullDirectionPlane), isTangentChain);
        // The angle is set on the input object afterwards, not passed in.
        input.SetSingleAngle(false, angle);
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["angle"] = angle,
            ["face_count"] = faces.Count,
        };
    }

    public static JsonObject SplitBody(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var splittingPlane = ParamReader.Str(p, "splitting_plane", "xy");
        var splittingBody = ParamReader.StrOrNull(p, "splitting_body");
        var extendTool = ParamReader.Bool(p, "extend_tool", true);

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);

        var splits = root.Features.SplitBodyFeatures;
        IFusionSplitBodyInput input;
        if (splittingBody is not null)
        {
            var tool = DesignQueries.BodyByName(ctx, splittingBody);
            input = splits.CreateBodyInput(body, tool, extendTool);
        }
        else
        {
            input = splits.CreatePlaneInput(body, DesignQueries.ConstructionPlane(root, splittingPlane), extendTool);
        }

        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["splitting_plane"] = splittingPlane,
        };
    }

    public static JsonObject SplitFace(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var faceIndices = ParamReader.ListOrNull(p, "face_indices");
        var splittingPlane = ParamReader.Str(p, "splitting_plane", "xy");
        var extendTool = ParamReader.Bool(p, "extend_tool", true);

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);

        IReadOnlyList<IFusionFace> faces;
        if (faceIndices is not null)
        {
            var indices = ParamReader.IntList(p, "face_indices");
            faces = indices.Select(i => body.Faces[i]).ToList();
        }
        else
        {
            faces = body.Faces.ToList();
        }

        var input = root.Features.SplitFaceFeatures.CreateInput(faces, DesignQueries.ConstructionPlane(root, splittingPlane), extendTool);
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
        };
    }

    public static JsonObject OffsetFaces(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var distance = ParamReader.Num(p, "distance", 0);
        var faceSelection = ParamReader.Str(p, "face_selection", "top");
        var hasFaceIndices = ParamReader.ListOrNull(p, "face_indices") is not null;

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);

        IReadOnlyList<IFusionFace> faces;
        if (hasFaceIndices)
        {
            var indices = ParamReader.IntList(p, "face_indices");
            faces = indices.Select(i => body.Faces[i]).ToList();
        }
        else
        {
            faces = SelectFaces(body, faceSelection);
        }

        var input = root.Features.OffsetFeatures.CreateInput(faces, distance, FusionFeatureOperation.NewBody);
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["distance"] = distance,
        };
    }

    public static JsonObject ScaleBody(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var scale = ParamReader.Num(p, "scale", 0);
        var scaleX = ParamReader.NumOrNull(p, "scale_x");
        var scaleY = ParamReader.NumOrNull(p, "scale_y");
        var scaleZ = ParamReader.NumOrNull(p, "scale_z");
        var anchorX = ParamReader.Num(p, "anchor_x", 0);
        var anchorY = ParamReader.Num(p, "anchor_y", 0);
        var anchorZ = ParamReader.Num(p, "anchor_z", 0);

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);
        var anchor = new FusionPoint(anchorX, anchorY, anchorZ);

        var scales = root.Features.ScaleFeatures;
        IFusionScaleInput input;
        if (scaleX is not null && scaleY is not null && scaleZ is not null)
        {
            input = scales.CreateAxisInput(new[] { body }, anchor, scaleX.Value, scaleY.Value, scaleZ.Value);
        }
        else
        {
            input = scales.CreateUniformInput(new[] { body }, anchor, scale);
        }

        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["scale"] = scale,
        };
    }

    public static JsonObject SuppressFeature(IFusionContext ctx, JsonObject p) =>
        SetSuppression(ctx, ParamReader.Str(p, "feature_name", string.Empty), true);

    public static JsonObject UnsuppressFeature(IFusionContext ctx, JsonObject p) =>
        SetSuppression(ctx, ParamReader.Str(p, "feature_name", string.Empty), false);

    /// <summary>Reference suppress_feature / unsuppress_feature: find the timeline item by entity name and toggle it.</summary>
    private static JsonObject SetSuppression(IFusionContext ctx, string featureName, bool suppress)
    {
        var timeline = ctx.Design.Timeline;
        if (timeline is not null)
        {
            for (var i = 0; i < timeline.Count; i++)
            {
                var item = timeline.Item(i);
                if (item.Entity is { } entity && entity.Name == featureName)
                {
                    item.IsSuppressed = suppress;
                    return suppress
                        ? new JsonObject { ["suppressed"] = true, ["feature"] = featureName }
                        : new JsonObject { ["unsuppressed"] = true, ["feature"] = featureName };
                }
            }
        }

        throw new FusionCommandException($"Feature '{featureName}' not found in timeline");
    }

    // ------------------------------------------------------------------
    // Body operations
    // ------------------------------------------------------------------

    public static JsonObject MoveBody(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var x = ParamReader.Num(p, "x", 0);
        var y = ParamReader.Num(p, "y", 0);
        var z = ParamReader.Num(p, "z", 0);

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);

        var input = root.Features.MoveFeatures.CreateInput(new[] { body }, new FusionPoint(x, y, z));
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["body"] = bodyName,
            ["translation"] = new JsonArray { x, y, z },
        };
    }

    public static JsonObject BooleanOperation(IFusionContext ctx, JsonObject p)
    {
        var targetName = ParamReader.Str(p, "target_body", string.Empty);
        var toolName = ParamReader.Str(p, "tool_body", string.Empty);
        var operation = ParamReader.Str(p, "operation", "join");

        var root = ctx.Design.RootComponent;
        var target = DesignQueries.BodyByName(ctx, targetName);
        var tool = DesignQueries.BodyByName(ctx, toolName);

        var input = root.Features.CombineFeatures.CreateInput(target, new[] { tool });
        input.Operation = operation switch
        {
            "join" => FusionFeatureOperation.Join,
            "cut" => FusionFeatureOperation.Cut,
            "intersect" => FusionFeatureOperation.Intersect,
            _ => throw new FusionCommandException($"Unknown boolean op '{operation}' — use join/cut/intersect"),
        };

        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["operation"] = operation,
            ["target"] = targetName,
            ["tool"] = toolName,
        };
    }

    // ------------------------------------------------------------------
    // Direct primitives (TemporaryBRepManager)
    // ------------------------------------------------------------------

    /// <summary>Reference direct-primitive body insertion: baseFeature + bRepBodies.add(body, feat) + finish.</summary>
    private static void AddPrimitiveBody(IFusionRootComponent root, IFusionBody body)
    {
        var baseFeature = root.Features.BaseFeatures.Add();
        baseFeature.StartEdit();
        root.BRepBodies.Add(body, baseFeature);
        baseFeature.FinishEdit();
    }

    private static FusionPoint AxisVector(string axis, string command)
    {
        var vec = axis switch
        {
            "x" => new FusionPoint(1, 0, 0),
            "y" => new FusionPoint(0, 1, 0),
            "z" => new FusionPoint(0, 0, 1),
            _ => throw new FusionCommandException($"Unknown axis '{axis}' — use x, y, or z ({command})"),
        };

        return vec;
    }

    public static JsonObject CreateBox(IFusionContext ctx, JsonObject p)
    {
        var length = ParamReader.Num(p, "length", 0);
        var width = ParamReader.Num(p, "width", 0);
        var height = ParamReader.Num(p, "height", 0);
        var centerX = ParamReader.Num(p, "center_x", 0);
        var centerY = ParamReader.Num(p, "center_y", 0);
        var centerZ = ParamReader.Num(p, "center_z", 0);

        var root = ctx.Design.RootComponent;

        // Reference OrientedBoundingBox3D path: the centre sits at
        // center + (0, 0, height/2) so the box base rests on the Z plane.
        var center = new FusionPoint(centerX, centerY, centerZ + height / 2);
        var boxBody = root.BRepManager.CreateBox(center, new FusionPoint(1, 0, 0), new FusionPoint(0, 1, 0), length, width, height);
        AddPrimitiveBody(root, boxBody);

        return new JsonObject
        {
            ["created"] = true,
            ["length"] = length,
            ["width"] = width,
            ["height"] = height,
        };
    }

    public static JsonObject CreateCylinder(IFusionContext ctx, JsonObject p)
    {
        var radius = ParamReader.Num(p, "radius", 0);
        var height = ParamReader.Num(p, "height", 0);
        var baseX = ParamReader.Num(p, "base_x", 0);
        var baseY = ParamReader.Num(p, "base_y", 0);
        var baseZ = ParamReader.Num(p, "base_z", 0);
        var axis = ParamReader.Str(p, "axis", "z");

        var root = ctx.Design.RootComponent;
        var axisVec = AxisVector(axis, "create_cylinder");
        var basePt = new FusionPoint(baseX, baseY, baseZ);
        var topPt = new FusionPoint(
            baseX + axisVec.X * height,
            baseY + axisVec.Y * height,
            baseZ + axisVec.Z * height);

        var cylBody = root.BRepManager.CreateCylinderOrCone(basePt, radius, topPt, radius);
        AddPrimitiveBody(root, cylBody);

        return new JsonObject
        {
            ["created"] = true,
            ["radius"] = radius,
            ["height"] = height,
        };
    }

    public static JsonObject CreateSphere(IFusionContext ctx, JsonObject p)
    {
        var radius = ParamReader.Num(p, "radius", 0);
        var centerX = ParamReader.Num(p, "center_x", 0);
        var centerY = ParamReader.Num(p, "center_y", 0);
        var centerZ = ParamReader.Num(p, "center_z", 0);

        var root = ctx.Design.RootComponent;
        var sphereBody = root.BRepManager.CreateSphere(new FusionPoint(centerX, centerY, centerZ), radius);
        AddPrimitiveBody(root, sphereBody);

        return new JsonObject
        {
            ["created"] = true,
            ["radius"] = radius,
        };
    }

    public static JsonObject CreateTorus(IFusionContext ctx, JsonObject p)
    {
        var majorRadius = ParamReader.Num(p, "major_radius", 0);
        var minorRadius = ParamReader.Num(p, "minor_radius", 0);
        var centerX = ParamReader.Num(p, "center_x", 0);
        var centerY = ParamReader.Num(p, "center_y", 0);
        var centerZ = ParamReader.Num(p, "center_z", 0);
        var axis = ParamReader.Str(p, "axis", "z");

        var root = ctx.Design.RootComponent;
        var axisVec = AxisVector(axis, "create_torus");
        var torusBody = root.BRepManager.CreateTorus(new FusionPoint(centerX, centerY, centerZ), axisVec, majorRadius, minorRadius);
        AddPrimitiveBody(root, torusBody);

        return new JsonObject
        {
            ["created"] = true,
            ["major_radius"] = majorRadius,
            ["minor_radius"] = minorRadius,
        };
    }

    // ------------------------------------------------------------------
    // Surface operations
    // ------------------------------------------------------------------

    public static JsonObject PatchSurface(IFusionContext ctx, JsonObject p)
    {
        var sketchName = ParamReader.Str(p, "sketch_name", string.Empty);
        var profileIndex = ParamReader.Int(p, "profile_index", 0);
        var continuity = ParamReader.Str(p, "continuity", "connected");

        var root = ctx.Design.RootComponent;
        var sketch = DesignQueries.SketchByName(ctx, sketchName);
        if (sketch.ProfilesCount == 0)
        {
            throw new FusionCommandException("No profiles in sketch");
        }

        var profile = sketch.Profiles.Item(profileIndex);
        var input = root.Features.PatchFeatures.CreateInput(profile, FusionFeatureOperation.NewBody);
        // Reference cont_map.get(continuity, cont_map["connected"]) — unknown
        // values fall back to connected.
        input.BoundaryContinuity = continuity switch
        {
            "tangent" => FusionSurfaceContinuity.Tangent,
            "curvature" => FusionSurfaceContinuity.Curvature,
            _ => FusionSurfaceContinuity.Connected,
        };

        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["continuity"] = continuity,
        };
    }

    public static JsonObject StitchSurfaces(IFusionContext ctx, JsonObject p)
    {
        var bodyNames = ParamReader.StringList(p, "body_names");
        var tolerance = ParamReader.Num(p, "tolerance", 0.01);

        var root = ctx.Design.RootComponent;
        var bodies = bodyNames.Select(name => DesignQueries.BodyByName(ctx, name)).ToList();

        var input = root.Features.StitchFeatures.CreateInput(bodies, tolerance, FusionFeatureOperation.NewBody);
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["body_count"] = bodyNames.Length,
        };
    }

    public static JsonObject ThickenSurface(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var thickness = ParamReader.Num(p, "thickness", 0);
        var direction = ParamReader.Str(p, "direction", "symmetric");

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);
        var faces = body.Faces.ToList();

        var input = root.Features.ThickenFeatures.CreateInput(faces, thickness, direction == "symmetric", FusionFeatureOperation.NewBody);
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["thickness"] = thickness,
        };
    }

    public static JsonObject RuledSurface(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var edgeIndex = ParamReader.Int(p, "edge_index", 0);
        var distance = ParamReader.Num(p, "distance", 1.0);

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);
        var edge = body.Edges[edgeIndex];

        var input = root.Features.RuledSurfaceFeatures.CreateInput(edge, distance);
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
            ["distance"] = distance,
        };
    }

    public static JsonObject TrimSurface(IFusionContext ctx, JsonObject p)
    {
        var bodyName = ParamReader.Str(p, "body_name", string.Empty);
        var toolName = ParamReader.Str(p, "tool_name", string.Empty);

        var root = ctx.Design.RootComponent;
        var body = DesignQueries.BodyByName(ctx, bodyName);
        var tool = DesignQueries.BodyByName(ctx, toolName);

        var input = root.Features.TrimFeatures.CreateInput(body, tool);
        var feat = input.Add();
        return new JsonObject
        {
            ["feature_name"] = feat.Name,
        };
    }
}
