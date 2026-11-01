using Fusion360.Mcp.AddIn;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// In-memory IFusionFeatures for the T6 handler tests. Every input fake
/// records the parameters it was called with so tests can assert the
/// handler translated the request correctly; Add() returns a result fake
/// whose Name is what handlers surface as "feature_name".
/// </summary>
public sealed class FakeFeature : IFusionFeature
{
    public string Name { get; set; } = "Feature";
    public IFusionBody? FirstBody { get; set; }
    IFusionBody IFusionFeature.FirstBody => FirstBody!;
}

public sealed class FakeHoleFeature : IFusionHoleFeature
{
    public string Name { get; set; } = "Hole1";
    public double HoleDiameterCm { get; set; }
    public IFusionBody? FirstBody { get; set; }
    public List<string> BodyNameList { get; } = new();
    IFusionBody IFusionFeature.FirstBody => FirstBody!;
    IReadOnlyList<string> IFusionHoleFeature.BodyNames => BodyNameList;
}

public sealed class FakeBaseFeature : IFusionBaseFeature
{
    public int StartEditCalls { get; private set; }
    public int FinishEditCalls { get; private set; }
    public void StartEdit() => StartEditCalls++;
    public void FinishEdit() => FinishEditCalls++;
}

public sealed class FakePath : IFusionPath
{
}

public sealed class FakeProfile : IFusionProfile
{
    public int Index { get; set; }
}

public sealed class FakeProfileCollection : IFusionProfileCollection
{
    public List<FakeProfile> Profiles { get; } = new();
    public IFusionProfile Item(int index) => Profiles[index];
    public int Count => Profiles.Count;
}

public sealed class FakeThreadDataQuery : IFusionThreadDataQuery
{
    public string ThreadType { get; set; } = "M";
}

// ------------------------------------------------------------------------
// Input + collection fakes (one pair per reference feature collection)
// ------------------------------------------------------------------------

public sealed class FakeExtrudeFeatures : IFusionExtrudeFeatures
{
    public List<FakeExtrudeInput> Inputs { get; } = new();

    public IFusionExtrudeInput CreateInput(IFusionProfile profile, FusionFeatureOperation operation)
    {
        var input = new FakeExtrudeInput {
            Result = new FakeFeature { Name = "Extrude1", FirstBody = new FakeBody { Name = "Extrude1" } },
            Profile = profile,
            Operation = operation,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeExtrudeInput : IFusionExtrudeInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IFusionProfile? Profile { get; internal set; }
    public FusionFeatureOperation? Operation { get; internal set; }
    public bool? Reverse { get; private set; }
    public double? DistanceCm { get; private set; }
    public string? DistanceExpression { get; private set; }
    public bool? Mirror { get; private set; }
    public bool Symmetric { get; private set; }

    public void SetDistanceExtent(bool reverse, double distanceCm)
    {
        Reverse = reverse;
        DistanceCm = distanceCm;
    }

    public void SetDistanceExtentExpression(string expression)
    {
        DistanceExpression = expression;
    }

    public void SetSymmetricExtent(double distanceCm, bool mirror)
    {
        Symmetric = true;
        DistanceCm = distanceCm;
        Mirror = mirror;
    }

    public IFusionFeature Add() => Result;
}

public sealed class FakeRevolveFeatures : IFusionRevolveFeatures
{
    public List<FakeRevolveInput> Inputs { get; } = new();

    public IFusionRevolveInput CreateInput(IFusionProfile profile, IFusionRevolveAxis axis, FusionFeatureOperation operation)
    {
        var input = new FakeRevolveInput {
            Result = new FakeFeature { Name = "Revolve1" },
            Profile = profile,
            Axis = axis,
            Operation = operation,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeRevolveInput : IFusionRevolveInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IFusionProfile? Profile { get; internal set; }
    public IFusionRevolveAxis? Axis { get; internal set; }
    public FusionFeatureOperation? Operation { get; internal set; }
    public bool? Reverse { get; private set; }
    public double? AngleDegrees { get; private set; }

    public void SetAngleExtent(bool reverse, double angleDegrees)
    {
        Reverse = reverse;
        AngleDegrees = angleDegrees;
    }

    public IFusionFeature Add() => Result;
}

public sealed class FakeSweepFeatures : IFusionSweepFeatures
{
    public List<FakeSweepInput> Inputs { get; } = new();

    public IFusionSweepInput CreateInput(IFusionProfile profile, IFusionPath path, FusionFeatureOperation operation)
    {
        var input = new FakeSweepInput {
            Result = new FakeFeature { Name = "Sweep1" },
            Profile = profile,
            Path = path,
            Operation = operation,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeSweepInput : IFusionSweepInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IFusionProfile? Profile { get; internal set; }
    public IFusionPath? Path { get; internal set; }
    public FusionFeatureOperation? Operation { get; internal set; }
    public IFusionFeature Add() => Result;
}

public sealed class FakeLoftFeatures : IFusionLoftFeatures
{
    public List<FakeLoftInput> Inputs { get; } = new();

    public IFusionLoftInput CreateInput(FusionFeatureOperation operation)
    {
        var input = new FakeLoftInput { Result = new FakeFeature { Name = "Loft1" }, Operation = operation };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeLoftInput : IFusionLoftInput
{
    public FakeFeature Result { get; internal set; } = new();
    public FusionFeatureOperation? Operation { get; internal set; }
    public List<IFusionProfile> Sections { get; } = new();

    public void AddSection(IFusionProfile profile) => Sections.Add(profile);
    public IFusionFeature Add() => Result;
}

public sealed class FakeFilletFeatures : IFusionFilletFeatures
{
    public List<FakeFilletInput> Inputs { get; } = new();

    public IFusionFilletInput CreateInput()
    {
        var input = new FakeFilletInput { Result = new FakeFeature { Name = "Fillet1" } };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeFilletInput : IFusionFilletInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionBodyEdge>? Edges { get; private set; }
    public double? RadiusCm { get; private set; }
    public bool? IncludeAdjacent { get; private set; }

    public void AddConstantRadiusEdgeSet(IReadOnlyList<IFusionBodyEdge> edges, double radiusCm, bool includeAdjacent)
    {
        Edges = edges;
        RadiusCm = radiusCm;
        IncludeAdjacent = includeAdjacent;
    }

    public IFusionFeature Add() => Result;
}

public sealed class FakeChamferFeatures : IFusionChamferFeatures
{
    public List<FakeChamferInput> Inputs { get; } = new();

    public IFusionChamferInput CreateInput(IReadOnlyList<IFusionBodyEdge> edges, bool includeAdjacent)
    {
        var input = new FakeChamferInput {
            Result = new FakeFeature { Name = "Chamfer1" },
            Edges = edges,
            IncludeAdjacent = includeAdjacent,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeChamferInput : IFusionChamferInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionBodyEdge>? Edges { get; internal set; }
    public bool? IncludeAdjacent { get; internal set; }
    public double? DistanceCm { get; private set; }

    public void SetToEqualDistance(double distanceCm) => DistanceCm = distanceCm;
    public IFusionFeature Add() => Result;
}

public sealed class FakeShellFeatures : IFusionShellFeatures
{
    public List<FakeShellInput> Inputs { get; } = new();

    public IFusionShellInput CreateInput(IReadOnlyList<IFusionBody> bodies)
    {
        var input = new FakeShellInput { Result = new FakeFeature { Name = "Shell1" }, Bodies = bodies };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeShellInput : IFusionShellInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionBody>? Bodies { get; internal set; }
    public IReadOnlyList<IFusionFace> FacesToRemove { get; set; } = new List<IFusionFace>();
    public double InsideThicknessCm { get; set; }

    public IFusionFeature Add() => Result;
}

public sealed class FakeMirrorFeatures : IFusionMirrorFeatures
{
    public List<FakeMirrorInput> Inputs { get; } = new();

    public IFusionMirrorInput CreateInput(IReadOnlyList<IFusionBody> bodies, IFusionConstructionPlane plane)
    {
        var input = new FakeMirrorInput {
            Result = new FakeFeature { Name = "Mirror1" },
            Bodies = bodies,
            Plane = plane,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeMirrorInput : IFusionMirrorInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionBody>? Bodies { get; internal set; }
    public IFusionConstructionPlane? Plane { get; internal set; }
    public IFusionFeature Add() => Result;
}

public sealed class FakeHoleFeatures : IFusionHoleFeatures
{
    public List<FakeHoleInput> Inputs { get; } = new();

    public IFusionHoleInput CreateSimpleInput(double diameterCm)
    {
        var input = new FakeHoleInput { RequestedDiameterCm = diameterCm };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeHoleInput : IFusionHoleInput
{
    public FakeHoleFeature Result { get; internal set; } = new();
    public double RequestedDiameterCm { get; internal set; }
    public IFusionSketchPoint? PositionPoint { get; private set; }
    public double? DepthCm { get; private set; }
    public IReadOnlyList<IFusionBody> ParticipantBodies { get; set; } = Array.Empty<IFusionBody>();

    public void SetPositionBySketchPoint(IFusionSketchPoint point) => PositionPoint = point;
    public void SetDistanceExtent(double depthCm) => DepthCm = depthCm;

    public IFusionHoleFeature Add()
    {
        Result.HoleDiameterCm = RequestedDiameterCm;
        foreach (var body in ParticipantBodies)
        {
            Result.BodyNameList.Add(body.Name);
        }

        return Result;
    }
}

public sealed class FakeRectPatternFeatures : IFusionRectPatternFeatures
{
    public List<FakeRectPatternInput> Inputs { get; } = new();

    public IFusionRectPatternInput CreateInput(IReadOnlyList<IFusionBody> bodies, IFusionConstructionAxis xAxis, int xCount, double xSpacingCm)
    {
        var input = new FakeRectPatternInput {
            Result = new FakeFeature { Name = "RectPattern1" },
            Bodies = bodies,
            XAxis = xAxis,
            XCount = xCount,
            XSpacingCm = xSpacingCm,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeRectPatternInput : IFusionRectPatternInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionBody>? Bodies { get; internal set; }
    public IFusionConstructionAxis? XAxis { get; internal set; }
    public int? XCount { get; internal set; }
    public double? XSpacingCm { get; internal set; }
    public IFusionConstructionAxis? YAxis { get; private set; }
    public int? YCount { get; private set; }
    public double? YSpacingCm { get; private set; }

    public void SetDirectionTwo(IFusionConstructionAxis yAxis, int yCount, double ySpacingCm)
    {
        YAxis = yAxis;
        YCount = yCount;
        YSpacingCm = ySpacingCm;
    }

    public IFusionFeature Add() => Result;
}

public sealed class FakeCirclePatternFeatures : IFusionCirclePatternFeatures
{
    public List<FakeCirclePatternInput> Inputs { get; } = new();

    public IFusionCirclePatternInput CreateInput(IReadOnlyList<IFusionBody> bodies, IFusionConstructionAxis axis)
    {
        var input = new FakeCirclePatternInput {
            Result = new FakeFeature { Name = "CircularPattern1" },
            Bodies = bodies,
            Axis = axis,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeCirclePatternInput : IFusionCirclePatternInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionBody>? Bodies { get; internal set; }
    public IFusionConstructionAxis? Axis { get; internal set; }
    public int Quantity { get; set; }
    public double TotalAngleDegrees { get; set; }

    public IFusionFeature Add() => Result;
}

public sealed class FakeThreadFeatures : IFusionThreadFeatures
{
    public List<FakeThreadInput> Inputs { get; } = new();
    public FakeThreadDataQuery ThreadDataQueryImpl { get; } = new();

    public IFusionThreadDataQuery ThreadDataQuery => ThreadDataQueryImpl;

    public IFusionThreadInput CreateInput(IFusionFace face, IFusionThreadDataQuery data)
    {
        var input = new FakeThreadInput { Result = new FakeFeature { Name = "Thread1" }, Face = face };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeThreadInput : IFusionThreadInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IFusionFace? Face { get; internal set; }
    public bool IsModeled { get; set; }
    public bool IsFullLength { get; set; }
    public double ThreadLengthCm { get; set; }

    public IFusionFeature Add() => Result;
}

public sealed class FakeDraftFeatures : IFusionDraftFeatures
{
    public List<FakeDraftInput> Inputs { get; } = new();

    public IFusionDraftInput CreateInput(IReadOnlyList<IFusionFace> faces, IFusionConstructionPlane pullPlane, bool isTangentChain)
    {
        var input = new FakeDraftInput {
            Result = new FakeFeature { Name = "Draft1" },
            Faces = faces,
            PullPlane = pullPlane,
            IsTangentChain = isTangentChain,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeDraftInput : IFusionDraftInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionFace>? Faces { get; internal set; }
    public IFusionConstructionPlane? PullPlane { get; internal set; }
    public bool? IsTangentChain { get; internal set; }
    public bool? TwoSided { get; private set; }
    public double? AngleDegrees { get; private set; }

    public void SetSingleAngle(bool twoSided, double angleDegrees)
    {
        TwoSided = twoSided;
        AngleDegrees = angleDegrees;
    }

    public IFusionFeature Add() => Result;
}

public sealed class FakeSplitBodyFeatures : IFusionSplitBodyFeatures
{
    public List<FakeSplitBodyInput> Inputs { get; } = new();

    public IFusionSplitBodyInput CreateBodyInput(IFusionBody body, IFusionBody tool, bool extendTool)
    {
        var input = new FakeSplitBodyInput {
            Result = new FakeFeature { Name = "SplitBody1" },
            Body = body,
            ToolBody = tool,
            ExtendTool = extendTool,
        };
        Inputs.Add(input);
        return input;
    }

    public IFusionSplitBodyInput CreatePlaneInput(IFusionBody body, IFusionConstructionPlane plane, bool extendTool)
    {
        var input = new FakeSplitBodyInput {
            Result = new FakeFeature { Name = "SplitBody1" },
            Body = body,
            Plane = plane,
            ExtendTool = extendTool,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeSplitBodyInput : IFusionSplitBodyInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IFusionBody? Body { get; internal set; }
    public IFusionBody? ToolBody { get; internal set; }
    public IFusionConstructionPlane? Plane { get; internal set; }
    public bool? ExtendTool { get; internal set; }
    public IFusionFeature Add() => Result;
}

public sealed class FakeSplitFaceFeatures : IFusionSplitFaceFeatures
{
    public List<FakeSplitFaceInput> Inputs { get; } = new();

    public IFusionSplitFaceInput CreateInput(IReadOnlyList<IFusionFace> faces, IFusionConstructionPlane plane, bool extendTool)
    {
        var input = new FakeSplitFaceInput {
            Result = new FakeFeature { Name = "SplitFace1" },
            Faces = faces,
            Plane = plane,
            ExtendTool = extendTool,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeSplitFaceInput : IFusionSplitFaceInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionFace>? Faces { get; internal set; }
    public IFusionConstructionPlane? Plane { get; internal set; }
    public bool? ExtendTool { get; internal set; }
    public IFusionFeature Add() => Result;
}

public sealed class FakeOffsetFeatures : IFusionOffsetFeatures
{
    public List<FakeOffsetInput> Inputs { get; } = new();

    public IFusionOffsetInput CreateInput(IReadOnlyList<IFusionFace> faces, double distanceCm, FusionFeatureOperation operation)
    {
        var input = new FakeOffsetInput {
            Result = new FakeFeature { Name = "Offset1" },
            Faces = faces,
            DistanceCm = distanceCm,
            Operation = operation,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeOffsetInput : IFusionOffsetInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionFace>? Faces { get; internal set; }
    public double? DistanceCm { get; internal set; }
    public FusionFeatureOperation? Operation { get; internal set; }
    public IFusionFeature Add() => Result;
}

public sealed class FakeScaleFeatures : IFusionScaleFeatures
{
    public List<FakeScaleInput> Inputs { get; } = new();

    public IFusionScaleInput CreateUniformInput(IReadOnlyList<IFusionBody> bodies, FusionPoint anchor, double scale)
    {
        var input = new FakeScaleInput {
            Result = new FakeFeature { Name = "Scale1" },
            Bodies = bodies,
            Anchor = anchor,
            UniformScale = scale,
        };
        Inputs.Add(input);
        return input;
    }

    public IFusionScaleInput CreateAxisInput(IReadOnlyList<IFusionBody> bodies, FusionPoint anchor, double scaleX, double scaleY, double scaleZ)
    {
        var input = new FakeScaleInput {
            Result = new FakeFeature { Name = "Scale1" },
            Bodies = bodies,
            Anchor = anchor,
            AxisScale = new FusionPoint(scaleX, scaleY, scaleZ),
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeScaleInput : IFusionScaleInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionBody>? Bodies { get; internal set; }
    public FusionPoint? Anchor { get; internal set; }
    public double? UniformScale { get; internal set; }
    public FusionPoint? AxisScale { get; internal set; }
    public IFusionFeature Add() => Result;
}

public sealed class FakeMoveFeatures : IFusionMoveFeatures
{
    public List<FakeMoveInput> Inputs { get; } = new();

    public IFusionMoveInput CreateInput(IReadOnlyList<IFusionBody> bodies, FusionPoint translationCm)
    {
        var input = new FakeMoveInput {
            Result = new FakeFeature { Name = "Move1" },
            Bodies = bodies,
            TranslationCm = translationCm,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeMoveInput : IFusionMoveInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionBody>? Bodies { get; internal set; }
    public FusionPoint? TranslationCm { get; internal set; }
    public IFusionFeature Add() => Result;
}

public sealed class FakeCombineFeatures : IFusionCombineFeatures
{
    public List<FakeCombineInput> Inputs { get; } = new();

    public IFusionCombineInput CreateInput(IFusionBody target, IReadOnlyList<IFusionBody> tools)
    {
        var input = new FakeCombineInput { Result = new FakeFeature { Name = "Combine1" }, Target = target };
        input.Tools = tools;
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeCombineInput : IFusionCombineInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IFusionBody? Target { get; internal set; }
    public IReadOnlyList<IFusionBody>? Tools { get; internal set; }
    public FusionFeatureOperation Operation { get; set; }
    public IFusionFeature Add() => Result;
}

public sealed class FakePatchFeatures : IFusionPatchFeatures
{
    public List<FakePatchInput> Inputs { get; } = new();

    public IFusionPatchInput CreateInput(IFusionProfile profile, FusionFeatureOperation operation)
    {
        var input = new FakePatchInput { Result = new FakeFeature { Name = "Patch1" }, Profile = profile };
        input.Operation = operation;
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakePatchInput : IFusionPatchInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IFusionProfile? Profile { get; internal set; }
    public FusionFeatureOperation Operation { get; set; }
    public FusionSurfaceContinuity BoundaryContinuity { get; set; }

    public IFusionFeature Add() => Result;
}

public sealed class FakeStitchFeatures : IFusionStitchFeatures
{
    public List<FakeStitchInput> Inputs { get; } = new();

    public IFusionStitchInput CreateInput(IReadOnlyList<IFusionBody> bodies, double toleranceCm, FusionFeatureOperation operation)
    {
        var input = new FakeStitchInput {
            Result = new FakeFeature { Name = "Stitch1" },
            Bodies = bodies,
            ToleranceCm = toleranceCm,
            Operation = operation,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeStitchInput : IFusionStitchInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionBody>? Bodies { get; internal set; }
    public double? ToleranceCm { get; internal set; }
    public FusionFeatureOperation? Operation { get; internal set; }
    public IFusionFeature Add() => Result;
}

public sealed class FakeThickenFeatures : IFusionThickenFeatures
{
    public List<FakeThickenInput> Inputs { get; } = new();

    public IFusionThickenInput CreateInput(IReadOnlyList<IFusionFace> faces, double thicknessCm, bool symmetric, FusionFeatureOperation operation)
    {
        var input = new FakeThickenInput {
            Result = new FakeFeature { Name = "Thicken1" },
            Faces = faces,
            ThicknessCm = thicknessCm,
            Symmetric = symmetric,
            Operation = operation,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeThickenInput : IFusionThickenInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IReadOnlyList<IFusionFace>? Faces { get; internal set; }
    public double? ThicknessCm { get; internal set; }
    public bool? Symmetric { get; internal set; }
    public FusionFeatureOperation? Operation { get; internal set; }
    public IFusionFeature Add() => Result;
}

public sealed class FakeRuledSurfaceFeatures : IFusionRuledSurfaceFeatures
{
    public List<FakeRuledInput> Inputs { get; } = new();

    public IFusionRuledInput CreateInput(IFusionBodyEdge edge, double distanceCm)
    {
        var input = new FakeRuledInput { Result = new FakeFeature { Name = "Ruled1" }, Edge = edge, DistanceCm = distanceCm };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeRuledInput : IFusionRuledInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IFusionBodyEdge? Edge { get; internal set; }
    public double? DistanceCm { get; internal set; }
    public IFusionFeature Add() => Result;
}

public sealed class FakeTrimFeatures : IFusionTrimFeatures
{
    public List<FakeTrimInput> Inputs { get; } = new();

    public IFusionTrimInput CreateInput(IFusionBody body, IFusionBody tool)
    {
        var input = new FakeTrimInput { Result = new FakeFeature { Name = "Trim1" }, Body = body, Tool = tool };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeTrimInput : IFusionTrimInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IFusionBody? Body { get; internal set; }
    public IFusionBody? Tool { get; internal set; }
    public IFusionFeature Add() => Result;
}

public sealed class FakeBaseFeatures : IFusionBaseFeatures
{
    public List<FakeBaseFeature> Added { get; } = new();

    public IFusionBaseFeature Add()
    {
        var feat = new FakeBaseFeature();
        Added.Add(feat);
        return feat;
    }
}

// ------------------------------------------------------------------------
// T7 sheet-metal fakes (flange / bend / flat pattern / unfold)
// ------------------------------------------------------------------------

public sealed class FakeFlangeFeatures : IFusionFlangeFeatures
{
    public List<FakeFlangeInput> Inputs { get; } = new();

    public IFusionFlangeInput CreateInput(IFusionBodyEdge edge)
    {
        var input = new FakeFlangeInput
        {
            Result = new FakeFeature { Name = "Flange1", FirstBody = new FakeBody { Name = "Flange1" } },
            Edge = edge,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeFlangeInput : IFusionFlangeInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IFusionBodyEdge? Edge { get; internal set; }
    public double? AngleDegrees { get; private set; }
    public double? HeightCm { get; private set; }
    public double? BendRadiusCm { get; private set; }

    public void SetAngleDegrees(double angleDegrees) => AngleDegrees = angleDegrees;
    public void SetHeightCm(double heightCm) => HeightCm = heightCm;
    public void SetBendRadiusCm(double bendRadiusCm) => BendRadiusCm = bendRadiusCm;
    public IFusionFeature Add() => Result;
}

public sealed class FakeBendFeatures : IFusionBendFeatures
{
    public List<FakeBendInput> Inputs { get; } = new();

    public IFusionBendInput CreateInput(IFusionBody body, IFusionSketchCurve bendLine)
    {
        var input = new FakeBendInput
        {
            Result = new FakeFeature { Name = "Bend1", FirstBody = new FakeBody { Name = "Bend1" } },
            Body = body,
            BendLine = bendLine,
        };
        Inputs.Add(input);
        return input;
    }
}

public sealed class FakeBendInput : IFusionBendInput
{
    public FakeFeature Result { get; internal set; } = new();
    public IFusionBody? Body { get; internal set; }
    public IFusionSketchCurve? BendLine { get; internal set; }
    public double? BendAngleDegrees { get; private set; }
    public double? BendRadiusCm { get; private set; }

    public void SetBendAngleDegrees(double bendAngleDegrees) => BendAngleDegrees = bendAngleDegrees;
    public void SetBendRadiusCm(double bendRadiusCm) => BendRadiusCm = bendRadiusCm;
    public IFusionFeature Add() => Result;
}

public sealed class FakeFlatPatternFeatures : IFusionFlatPatternFeatures
{
    public List<FakeFlatPatternInput> Inputs { get; } = new();
    public List<FakeFeature> Added { get; } = new();
    public int CreateInputCalls { get; private set; }
    public IFusionBody? LastBody { get; private set; }

    public IFusionFlatPatternInput CreateInput(IFusionBody body)
    {
        CreateInputCalls++;
        LastBody = body;
        var input = new FakeFlatPatternInput { Body = body };
        Inputs.Add(input);
        return input;
    }

    public IFusionFeature Add(IFusionFlatPatternInput input)
    {
        var feature = new FakeFeature { Name = "FlatPattern1", FirstBody = new FakeBody { Name = "FlatPattern1" } };
        Added.Add(feature);
        return feature;
    }
}

public sealed class FakeFlatPatternInput : IFusionFlatPatternInput
{
    public IFusionBody? Body { get; internal set; }
}

public sealed class FakeUnfoldFeatures : IFusionUnfoldFeatures
{
    public List<FakeUnfoldInput> Inputs { get; } = new();
    public List<FakeFeature> Added { get; } = new();
    public int CreateInputCalls { get; private set; }
    public IReadOnlyList<IFusionFace>? LastBends { get; private set; }
    public IFusionFace? LastStationary { get; private set; }

    public IFusionUnfoldInput CreateInput(IReadOnlyList<IFusionFace> bends, IFusionFace stationary)
    {
        CreateInputCalls++;
        LastBends = bends;
        LastStationary = stationary;
        var input = new FakeUnfoldInput { Bends = bends, Stationary = stationary };
        Inputs.Add(input);
        return input;
    }

    public IFusionFeature Add(IFusionUnfoldInput input)
    {
        var feature = new FakeFeature { Name = "Unfold1", FirstBody = new FakeBody { Name = "Unfold1" } };
        Added.Add(feature);
        return feature;
    }
}

public sealed class FakeUnfoldInput : IFusionUnfoldInput
{
    public IReadOnlyList<IFusionFace>? Bends { get; internal set; }
    public IFusionFace? Stationary { get; internal set; }
}

// ------------------------------------------------------------------------
// The root IFusionFeatures implementation
// ------------------------------------------------------------------------
public sealed class FakeFeatures : IFusionFeatures
{
    public FakeExtrudeFeatures Extrude { get; } = new();
    public FakeRevolveFeatures Revolve { get; } = new();
    public FakeSweepFeatures Sweep { get; } = new();
    public FakeLoftFeatures Loft { get; } = new();
    public FakeFilletFeatures Fillet { get; } = new();
    public FakeChamferFeatures Chamfer { get; } = new();
    public FakeShellFeatures Shell { get; } = new();
    public FakeMirrorFeatures Mirror { get; } = new();
    public FakeHoleFeatures Hole { get; } = new();
    public FakeRectPatternFeatures RectPattern { get; } = new();
    public FakeCirclePatternFeatures CirclePattern { get; } = new();
    public FakeThreadFeatures Thread { get; } = new();
    public FakeDraftFeatures Draft { get; } = new();
    public FakeSplitBodyFeatures SplitBody { get; } = new();
    public FakeSplitFaceFeatures SplitFace { get; } = new();
    public FakeOffsetFeatures Offset { get; } = new();
    public FakeScaleFeatures Scale { get; } = new();
    public FakeMoveFeatures Move { get; } = new();
    public FakeCombineFeatures Combine { get; } = new();
    public FakePatchFeatures Patch { get; } = new();
    public FakeStitchFeatures Stitch { get; } = new();
    public FakeThickenFeatures Thicken { get; } = new();
    public FakeRuledSurfaceFeatures Ruled { get; } = new();
    public FakeTrimFeatures Trim { get; } = new();
    public FakeBaseFeatures Base { get; } = new();
    public FakeFlangeFeatures Flange { get; } = new();
    public FakeBendFeatures Bend { get; } = new();
    public FakeFlatPatternFeatures FlatPattern { get; } = new();
    public FakeUnfoldFeatures Unfold { get; } = new();

    public int CreatePathCalls { get; private set; }
    public IFusionSketchCurve? LastPathCurve { get; private set; }

    public IFusionPath CreatePath(IFusionSketchCurve curve)
    {
        CreatePathCalls++;
        LastPathCurve = curve;
        return new FakePath();
    }

    IFusionExtrudeFeatures IFusionFeatures.ExtrudeFeatures => Extrude;
    IFusionRevolveFeatures IFusionFeatures.RevolveFeatures => Revolve;
    IFusionSweepFeatures IFusionFeatures.SweepFeatures => Sweep;
    IFusionLoftFeatures IFusionFeatures.LoftFeatures => Loft;
    IFusionFilletFeatures IFusionFeatures.FilletFeatures => Fillet;
    IFusionChamferFeatures IFusionFeatures.ChamferFeatures => Chamfer;
    IFusionShellFeatures IFusionFeatures.ShellFeatures => Shell;
    IFusionMirrorFeatures IFusionFeatures.MirrorFeatures => Mirror;
    IFusionHoleFeatures IFusionFeatures.HoleFeatures => Hole;
    IFusionRectPatternFeatures IFusionFeatures.RectangularPatternFeatures => RectPattern;
    IFusionCirclePatternFeatures IFusionFeatures.CircularPatternFeatures => CirclePattern;
    IFusionThreadFeatures IFusionFeatures.ThreadFeatures => Thread;
    IFusionDraftFeatures IFusionFeatures.DraftFeatures => Draft;
    IFusionSplitBodyFeatures IFusionFeatures.SplitBodyFeatures => SplitBody;
    IFusionSplitFaceFeatures IFusionFeatures.SplitFaceFeatures => SplitFace;
    IFusionOffsetFeatures IFusionFeatures.OffsetFeatures => Offset;
    IFusionScaleFeatures IFusionFeatures.ScaleFeatures => Scale;
    IFusionMoveFeatures IFusionFeatures.MoveFeatures => Move;
    IFusionCombineFeatures IFusionFeatures.CombineFeatures => Combine;
    IFusionPatchFeatures IFusionFeatures.PatchFeatures => Patch;
    IFusionStitchFeatures IFusionFeatures.StitchFeatures => Stitch;
    IFusionThickenFeatures IFusionFeatures.ThickenFeatures => Thicken;
    IFusionRuledSurfaceFeatures IFusionFeatures.RuledSurfaceFeatures => Ruled;
    IFusionTrimFeatures IFusionFeatures.TrimFeatures => Trim;
    IFusionBaseFeatures IFusionFeatures.BaseFeatures => Base;
    IFusionFlangeFeatures IFusionFeatures.FlangeFeatures => Flange;
    IFusionBendFeatures IFusionFeatures.BendFeatures => Bend;
    IFusionFlatPatternFeatures IFusionFeatures.FlatPatternFeatures => FlatPattern;
    IFusionUnfoldFeatures IFusionFeatures.UnfoldFeatures => Unfold;
}

// ------------------------------------------------------------------------
// Construction axis + BRep manager fakes
// ------------------------------------------------------------------------
public sealed class FakeConstructionAxis : IFusionConstructionAxis, IFusionDeletable
{
    public FakeConstructionAxis(string name) => Name = name;
    public string Name { get; }
    public bool Deleted { get; private set; }
    public void DeleteMe() => Deleted = true;
}

public sealed class FakeBRepManager : IFusionBRepManager
{
    public List<FakeBody> Created { get; } = new();
    public int CreateBoxCalls { get; private set; }
    public int CreateCylinderCalls { get; private set; }
    public int CreateSphereCalls { get; private set; }
    public int CreateTorusCalls { get; private set; }
    public FusionPoint? LastBoxCenter { get; private set; }

    /// <summary>
    /// P4 test hook: when set, the primitive creation records the mutation
    /// first (the body lands in <see cref="Created"/>) and THEN throws this
    /// exception — simulating a mutation that moved the model partway and
    /// only then failed.
    /// </summary>
    public Exception? ThrowAfterMutation { get; set; }

    private FakeBody Make(string kind)
    {
        var body = new FakeBody { Name = $"{kind}1" };
        Created.Add(body);
        return body;
    }

    public IFusionBody CreateBox(FusionPoint center, FusionPoint xAxis, FusionPoint yAxis, double lengthCm, double widthCm, double heightCm)
    {
        CreateBoxCalls++;
        LastBoxCenter = center;
        var body = Make("Box");
        if (ThrowAfterMutation is not null)
        {
            throw ThrowAfterMutation;
        }
        return body;
    }

    public IFusionBody CreateCylinderOrCone(FusionPoint basePoint, double baseRadiusCm, FusionPoint topPoint, double topRadiusCm)
    {
        CreateCylinderCalls++;
        return Make("Cylinder");
    }

    public IFusionBody CreateSphere(FusionPoint center, double radiusCm)
    {
        CreateSphereCalls++;
        return Make("Sphere");
    }

    public IFusionBody CreateTorus(FusionPoint center, FusionPoint axis, double majorRadiusCm, double minorRadiusCm)
    {
        CreateTorusCalls++;
        return Make("Torus");
    }
}
