using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Fusion360.Mcp.AddIn;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// T7 group-3 fakes: sheet metal, construction geometry, assembly,
/// inspection, mesh, appearance/materials, parameters, export, CAM.
/// Mirrors the src/Fusion360.Mcp.AddIn/Context/IFusionT7.cs design;
/// every fake records the calls it serves so T7 handler tests can assert
/// on the translation without Fusion.
/// </summary>

// ------------------------------------------------------------------------
// Inspection: measure manager / distance / angle / physical properties
// ------------------------------------------------------------------------

public sealed class FakeMeasureManager : IFusionMeasureManager
{
    /// <summary>Default: 1.0 cm between (0,0,0) and (1,0,0); set null to force the "not configured" error.</summary>
    public FakeDistanceMeasureResult? DistanceResult { get; set; } = new()
    {
        Value = 1.0,
        PointOnEntityOne = new FusionPoint(0, 0, 0),
        PointOnEntityTwo = new FusionPoint(1, 0, 0),
    };

    /// <summary>Default: 90 degrees (pi/2 radians); set null to force the "not configured" error.</summary>
    public FakeAngleMeasureResult? AngleResult { get; set; } = new() { ValueRadians = Math.PI / 2 };

    public List<(IFusionMeasureEntity One, IFusionMeasureEntity Two)> MeasureDistanceCalls { get; } = new();
    public List<(IFusionFace FaceOne, IFusionFace FaceTwo)> MeasureAngleCalls { get; } = new();
    public IFusionMeasureEntity? LastDistanceEntityOne { get; private set; }
    public IFusionMeasureEntity? LastDistanceEntityTwo { get; private set; }
    public IFusionFace? LastAngleFaceOne { get; private set; }
    public IFusionFace? LastAngleFaceTwo { get; private set; }

    public IFusionDistanceMeasureResult MeasureMinimumDistance(IFusionMeasureEntity entityOne, IFusionMeasureEntity entityTwo)
    {
        MeasureDistanceCalls.Add((entityOne, entityTwo));
        LastDistanceEntityOne = entityOne;
        LastDistanceEntityTwo = entityTwo;
        return DistanceResult ?? throw new FusionCommandException("MeasureManager not configured");
    }

    public IFusionAngleMeasureResult MeasureAngle(IFusionFace faceOne, IFusionFace faceTwo)
    {
        MeasureAngleCalls.Add((faceOne, faceTwo));
        LastAngleFaceOne = faceOne;
        LastAngleFaceTwo = faceTwo;
        return AngleResult ?? throw new FusionCommandException("MeasureManager not configured");
    }
}

public sealed class FakeDistanceMeasureResult : IFusionDistanceMeasureResult
{
    public double Value { get; set; }
    public FusionPoint PointOnEntityOne { get; set; }
    public FusionPoint PointOnEntityTwo { get; set; }
}

public sealed class FakeAngleMeasureResult : IFusionAngleMeasureResult
{
    public double ValueRadians { get; set; }
}

public sealed class FakePhysicalProperties : IFusionPhysicalProperties
{
    public double MassKg { get; set; }
    public double VolumeCm3 { get; set; }
    public double AreaCm2 { get; set; }
    public double Density { get; set; }
    public FusionPoint CenterOfMass { get; set; }
}

// ------------------------------------------------------------------------
// Inspection: interference
// ------------------------------------------------------------------------

public sealed class FakeInterferenceInput : IFusionInterferenceInput
{
    public FakeInterferenceInput(IReadOnlyList<IFusionBody>? bodies = null)
        => Bodies = bodies ?? Array.Empty<IFusionBody>();

    public bool IncludeCoincidentFaces { get; set; }
    public IReadOnlyList<IFusionBody> Bodies { get; }
}

public sealed class FakeInterferenceResult : IFusionInterferenceResult
{
    public string BodyOne { get; set; } = "";
    public string BodyTwo { get; set; } = "";
    public double InterferenceVolumeCm3 { get; set; }
}

// ------------------------------------------------------------------------
// Inspection: section analysis
// ------------------------------------------------------------------------

public sealed class FakeAnalysisCollection : IFusionAnalysisCollection
{
    public List<FakeSectionAnalysisInput> Added { get; } = new();
    public int CreateInputCalls { get; private set; }

    public IFusionSectionAnalysisInput CreateInput()
    {
        CreateInputCalls++;
        return new FakeSectionAnalysisInput();
    }

    public void Add(IFusionSectionAnalysisInput input)
    {
        if (input is FakeSectionAnalysisInput fake)
        {
            Added.Add(fake);
        }
    }
}

public sealed class FakeSectionAnalysisInput : IFusionSectionAnalysisInput
{
    public IFusionConstructionPlane? Plane { get; set; }
    public double? DistanceCm { get; private set; }
    public void SetDistanceCm(double distanceCm) => DistanceCm = distanceCm;
}

// ------------------------------------------------------------------------
// Construction geometry: axis collection / input (plane side is in
// FakeFusionContext.cs next to the existing fake)
// ------------------------------------------------------------------------

public sealed class FakeConstructionAxisCollection : IFusionConstructionAxisCollection
{
    public List<FakeConstructionAxis> Axes { get; } = new();
    private int _addedCount;

    public int Count => Axes.Count;
    public IFusionDeletable Item(int index) => Axes[index];

    public IFusionConstructionAxisInput CreateInput() => new FakeConstructionAxisInput();

    public IFusionConstructionAxis Add(IFusionConstructionAxisInput input)
    {
        _addedCount++;
        var axis = new FakeConstructionAxis($"CAxis{_addedCount}");
        Axes.Add(axis);
        return axis;
    }
}

public sealed class FakeConstructionAxisInput : IFusionConstructionAxisInput
{
    /// <summary>Which SetBy* was called ("two_points"/"two_planes"/"edge"/"perpendicular_at_point").</summary>
    public string? SetBy { get; private set; }
    public IReadOnlyList<FusionPoint>? Points { get; private set; }
    public IFusionConstructionPlane? PlaneOne { get; private set; }
    public IFusionConstructionPlane? PlaneTwo { get; private set; }
    public IFusionBodyEdge? Edge { get; private set; }
    public IFusionConstructionPlane? PerpendicularPlane { get; private set; }
    public FusionPoint? PerpendicularPoint { get; private set; }

    public void SetByTwoPoints(FusionPoint p1, FusionPoint p2)
    {
        SetBy = "two_points";
        Points = new[] { p1, p2 };
    }

    public void SetByTwoPlanes(IFusionConstructionPlane one, IFusionConstructionPlane two)
    {
        SetBy = "two_planes";
        PlaneOne = one;
        PlaneTwo = two;
    }

    public void SetByEdge(IFusionBodyEdge edge)
    {
        SetBy = "edge";
        Edge = edge;
    }

    public void SetByPerpendicularAtPoint(IFusionConstructionPlane plane, FusionPoint point)
    {
        SetBy = "perpendicular_at_point";
        PerpendicularPlane = plane;
        PerpendicularPoint = point;
    }
}

/// <summary>Construction point marker (joint anchor / UCS origin).</summary>
public sealed class FakeConstructionPoint : IFusionConstructionPoint
{
}

// ------------------------------------------------------------------------
// User coordinate systems
// ------------------------------------------------------------------------

public sealed class FakeUcsCollection : IFusionUcsCollection
{
    public List<FakeUcs> Added { get; } = new();
    public List<FakeUcsInput> CreatedInputs { get; } = new();

    public IFusionUcsInput CreateInput(IFusionUcsGeometry geometry)
    {
        var input = new FakeUcsInput { Geometry = geometry };
        CreatedInputs.Add(input);
        return input;
    }

    public IFusionUcs Add(IFusionUcsInput input)
    {
        var ucs = new FakeUcs { Name = $"UCS{Added.Count + 1}", Input = input as FakeUcsInput };
        Added.Add(ucs);
        return ucs;
    }
}

public sealed class FakeUcs : IFusionUcs
{
    public string Name { get; set; } = "";
    public FakeUcsInput? Input { get; set; }
}

public sealed class FakeUcsInput : IFusionUcsInput
{
    public IFusionUcsGeometry? Geometry { get; set; }
    public double? AngleXRadians { get; private set; }
    public double? AngleYRadians { get; private set; }
    public double? AngleZRadians { get; private set; }

    public void SetAngleXRadians(double radians) => AngleXRadians = radians;
    public void SetAngleYRadians(double radians) => AngleYRadians = radians;
    public void SetAngleZRadians(double radians) => AngleZRadians = radians;
}

public sealed class FakeUcsGeometry : IFusionUcsGeometry
{
    public FakeUcsGeometry(IFusionSketchPoint? point = null) => Point = point;
    public IFusionSketchPoint? Point { get; }
}

// ------------------------------------------------------------------------
// Assembly: joints / as-built joints / rigid groups
// ------------------------------------------------------------------------

public sealed class FakeJointCollection : IFusionJointCollection
{
    public List<FakeJoint> Added { get; } = new();
    public List<FakeJoint> Items { get; } = new();

    public int Count => Items.Count;
    public IFusionDeletable Item(int index) => Items[index];

    public IFusionJointInput CreateInput(IFusionJointGeometry geometryOne, IFusionJointGeometry geometryTwo)
        => new FakeJointInput { GeometryOne = geometryOne, GeometryTwo = geometryTwo };

    public IFusionJoint Add(IFusionJointInput input)
    {
        var joint = new FakeJoint { Name = $"Joint{Added.Count + 1}", Input = input as FakeJointInput };
        Added.Add(joint);
        Items.Add(joint);
        return joint;
    }
}

public sealed class FakeJoint : IFusionJoint, IFusionDeletable
{
    public string Name { get; set; } = "";
    public FakeJointInput? Input { get; set; }
    public bool Deleted { get; private set; }
    public void DeleteMe() => Deleted = true;
}

public sealed class FakeJointInput : IFusionJointInput
{
    public IFusionJointGeometry? GeometryOne { get; set; }
    public IFusionJointGeometry? GeometryTwo { get; set; }
    public bool RigidJointMotion { get; private set; }
    public void SetAsRigidJointMotion() => RigidJointMotion = true;
}

public sealed class FakeJointGeometry : IFusionJointGeometry
{
    public IFusionOccurrence? Occurrence { get; set; }
    public IFusionConstructionPoint? Point { get; set; }
}

public sealed class FakeAsBuiltJointCollection : IFusionAsBuiltJointCollection
{
    public List<FakeAsBuiltJoint> Added { get; } = new();
    public List<FakeAsBuiltJoint> Items { get; } = new();

    public int Count => Items.Count;
    public IFusionDeletable Item(int index) => Items[index];

    public IFusionAsBuiltJointInput CreateInput(IFusionOccurrence componentOne, IFusionOccurrence componentTwo)
        => new FakeAsBuiltJointInput { OccurrenceOne = componentOne, OccurrenceTwo = componentTwo };

    public IFusionAsBuiltJoint Add(IFusionAsBuiltJointInput input)
    {
        var joint = new FakeAsBuiltJoint { Name = $"AsBuiltJoint{Added.Count + 1}", Input = input as FakeAsBuiltJointInput };
        Added.Add(joint);
        Items.Add(joint);
        return joint;
    }
}

public sealed class FakeAsBuiltJointInput : IFusionAsBuiltJointInput
{
    public IFusionOccurrence? OccurrenceOne { get; set; }
    public IFusionOccurrence? OccurrenceTwo { get; set; }
}

public sealed class FakeAsBuiltJoint : IFusionAsBuiltJoint, IFusionDeletable
{
    public string Name { get; set; } = "";
    public FakeAsBuiltJointInput? Input { get; set; }
    public bool Deleted { get; private set; }
    public void DeleteMe() => Deleted = true;
}

public sealed class FakeRigidGroupCollection : IFusionRigidGroupCollection
{
    public List<IReadOnlyList<IFusionOccurrence>> AddedGroups { get; } = new();
    public List<bool> IncludeChildrens { get; } = new();
    public List<FakeRigidGroup> Items { get; } = new();

    public int Count => Items.Count;
    public IFusionDeletable Item(int index) => Items[index];

    public void Add(IReadOnlyList<IFusionOccurrence> occurrences, bool includeChildren)
    {
        var group = new FakeRigidGroup
        {
            Name = $"RigidGroup{Items.Count + 1}",
            Occurrences = occurrences,
            IncludeChildren = includeChildren,
        };
        AddedGroups.Add(occurrences);
        IncludeChildrens.Add(includeChildren);
        Items.Add(group);
    }
}

public sealed class FakeRigidGroup : IFusionDeletable
{
    public string Name { get; set; } = "";
    public IReadOnlyList<IFusionOccurrence> Occurrences { get; set; } = Array.Empty<IFusionOccurrence>();
    public bool IncludeChildren { get; set; }
    public bool Deleted { get; private set; }
    public void DeleteMe() => Deleted = true;
}

// ------------------------------------------------------------------------
// Assembly: occurrences (add_new_component + delete_all sweeps)
// ------------------------------------------------------------------------

public sealed class FakeOccurrences : IFusionOccurrences
{
    public FakeOccurrences() : this(new List<FakeOccurrence>())
    {
    }

    public FakeOccurrences(List<FakeOccurrence> occurrences) => Occurrences = occurrences;

    /// <summary>The wrapped occurrence list (delete_all sweeps mutate through it).</summary>
    public List<FakeOccurrence> Occurrences { get; }

    /// <summary>Occurrences created via <see cref="AddNewComponent"/> (creation order).</summary>
    public List<FakeOccurrence> Created { get; } = new();

    /// <summary>Components created via <see cref="AddNewComponent"/> ("Component1", "Component2", ...).</summary>
    public List<FakeComponent> CreatedComponents { get; } = new();

    public int Count => Occurrences.Count;
    public IFusionDeletable Item(int index) => new FakeDeletableOccurrence(Occurrences[index]);

    public IFusionOccurrence OccurrenceAt(int index) => Occurrences[index];

    public IFusionOccurrence AddNewComponent()
    {
        var component = new FakeComponent { Name = $"Component{CreatedComponents.Count + 1}" };
        CreatedComponents.Add(component);
        var occ = new FakeOccurrence
        {
            Name = component.Name,
            Component = component,
            Owner = Occurrences,
        };
        Occurrences.Add(occ);
        Created.Add(occ);
        return occ;
    }
}

// ------------------------------------------------------------------------
// Mesh
// ------------------------------------------------------------------------

public sealed class FakeMeshBodyCollection : IFusionMeshBodyCollection
{
    public List<FakeMeshBody> Bodies { get; } = new();
    public bool ReturnEmptyImport { get; set; }
    public List<(string Path, FusionMeshUnits Units)> Imports { get; } = new();

    public int Count => Bodies.Count;
    public IFusionMeshBody Item(int index) => Bodies[index];

    public IFusionMeshBodyList Add(string filePath, FusionMeshUnits units)
    {
        Imports.Add((filePath, units));
        if (ReturnEmptyImport)
        {
            return new FakeMeshBodyList(new List<FakeMeshBody>());
        }

        var body = new FakeMeshBody { Name = Path.GetFileNameWithoutExtension(filePath) };
        Bodies.Add(body);
        return new FakeMeshBodyList(new List<FakeMeshBody> { body });
    }
}

public sealed class FakeMeshBodyList : IFusionMeshBodyList
{
    public List<FakeMeshBody> Bodies { get; }
    public FakeMeshBodyList(List<FakeMeshBody> bodies) => Bodies = bodies;
    public int Count => Bodies.Count;
    public IFusionMeshBody Item(int index) => Bodies[index];
}

public sealed class FakeMeshBody : IFusionMeshBody
{
    public string Name { get; set; } = "";
    public IFusionBoundingBox? BoundingBox { get; set; }

    /// <summary>When set, <see cref="CompareWith"/> returns it; null = "no polygon mesh data".</summary>
    public IReadOnlyList<double>? ComparisonResult { get; set; }

    public IReadOnlyList<double>? CompareWith(IFusionMeshBody other) => ComparisonResult;
}

// ------------------------------------------------------------------------
// Appearance / materials
// ------------------------------------------------------------------------

public sealed class FakeAppearance : IFusionAppearance
{
    public string Name { get; set; } = "";

    /// <summary>Last SetColor call as (R, G, B, A); null when no color was set.</summary>
    public (int R, int G, int B, int A)? LastColor { get; private set; }

    public void SetColor(int red, int green, int blue, int alpha)
        => LastColor = (red, green, blue, alpha);
}

public sealed class FakeAppearanceCollection : IFusionAppearanceCollection
{
    public List<FakeAppearance> Items { get; } = new();

    /// <summary>Names passed to Add/AddByCopy, in call order.</summary>
    public List<string> Added { get; } = new();

    public IFusionAppearance? ItemByName(string name) => Items.FirstOrDefault(a => a.Name == name);

    public IFusionAppearance AddByCopy(IFusionAppearance source, string name)
    {
        var clone = new FakeAppearance { Name = name };
        Items.Add(clone);
        Added.Add(name);
        return clone;
    }

    public IFusionAppearance Add(string name)
    {
        var appearance = new FakeAppearance { Name = name };
        Items.Add(appearance);
        Added.Add(name);
        return appearance;
    }
}

public sealed class FakeMaterialLibrary : IFusionMaterialLibrary
{
    public string Name { get; set; } = "";
    public List<FakeAppearance> AppearancesList { get; } = new();
    public int AppearancesCount => AppearancesList.Count;
    IReadOnlyList<IFusionAppearance> IFusionMaterialLibrary.Appearances
        => AppearancesList.Cast<IFusionAppearance>().ToList();
}

public sealed class FakeMaterialLibraries : IFusionMaterialLibraries
{
    public List<FakeMaterialLibrary> Libraries { get; } = new()
    {
        new FakeMaterialLibrary
        {
            Name = "Fusion Appearance Library",
            AppearancesList =
            {
                new FakeAppearance { Name = "Aluminum" },
                new FakeAppearance { Name = "Plastic" },
                new FakeAppearance { Name = "Steel" },
            },
        },
    };

    public int Count => Libraries.Count;
    public IFusionMaterialLibrary? Item(int index) => Libraries[index];
    public IFusionMaterialLibrary? ItemByName(string name) => Libraries.FirstOrDefault(l => l.Name == name);
}

// ------------------------------------------------------------------------
// Parameters
// ------------------------------------------------------------------------

public sealed class FakeUserParameterCollection : IFusionUserParameterCollection
{
    public FakeUserParameterCollection(List<FakeParameter> parameters) => Parameters = parameters;

    /// <summary>The wrapped parameter list (delete_all/get_parameters state).</summary>
    public List<FakeParameter> Parameters { get; }

    /// <summary>Parameters created via <see cref="Add"/>, with their original (name, value, unit, comment) args.</summary>
    public List<(string Name, double Value, string? Unit, string? Comment)> Added { get; } = new();

    /// <summary>Parameters removed via <see cref="Delete"/>, in call order.</summary>
    public List<IFusionParameter> Deleted { get; } = new();

    public int Count => Parameters.Count;
    public IFusionParameter Item(int index) => Parameters[index];
    public IFusionParameter? ItemByName(string name) => Parameters.FirstOrDefault(p => p.Name == name);

    public IFusionParameter Add(string name, double value, string? unit, string? comment)
    {
        var parameter = new FakeParameter
        {
            Name = name,
            Value = value,
            Unit = unit ?? "",
            Comment = comment ?? "",
            Expression = string.IsNullOrEmpty(unit)
                ? null
                : $"{value.ToString(CultureInfo.InvariantCulture)} {unit}",
        };
        Parameters.Add(parameter);
        Added.Add((name, value, unit, comment));
        return parameter;
    }

    public void Delete(IFusionParameter parameter)
    {
        Parameters.Remove((FakeParameter)parameter);
        Deleted.Add(parameter);
    }

    public IEnumerator<IFusionParameter> GetEnumerator()
        => Parameters.Cast<IFusionParameter>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// ------------------------------------------------------------------------
// Export
// ------------------------------------------------------------------------

public sealed class FakeExportManager : IFusionExportManager
{
    public List<string> StlExports { get; } = new();
    public List<string> StepExports { get; } = new();
    public List<string> FusionArchiveExports { get; } = new();
    public List<IFusionExportTarget> StlTargets { get; } = new();
    public List<IFusionExportTarget> StepTargets { get; } = new();

    public void ExportStl(IFusionExportTarget entity, string filePath)
    {
        StlExports.Add(filePath);
        StlTargets.Add(entity);
    }

    public void ExportStep(string filePath, IFusionExportTarget entity)
    {
        StepExports.Add(filePath);
        StepTargets.Add(entity);
    }

    public void ExportFusionArchive(string filePath)
    {
        FusionArchiveExports.Add(filePath);
    }
}

// ------------------------------------------------------------------------
// CAM
// ------------------------------------------------------------------------

public sealed class FakeCamProduct : IFusionCamProduct
{
    public FakeCamSetupCollection Setups { get; } = new();
    public List<bool> GenerateAllCalls { get; } = new();
    public List<IFusionCamToolpathTarget> GenerateToolpathTargets { get; } = new();
    public List<(IFusionCamPostTarget Target, IFusionCamPostInput Input)> PostProcessCalls { get; } = new();
    public List<FakeCamPostInput> CreatedPostInputs { get; } = new();
    public string? GenericPostFolder { get; set; }
    public FakeCamFuture? NextFuture { get; set; }

    private FakeCamFuture TakeFuture()
    {
        var future = NextFuture ?? new FakeCamFuture();
        NextFuture = null;
        return future;
    }

    public FakeCamFuture GenerateAllToolpaths(bool overwrite)
    {
        GenerateAllCalls.Add(overwrite);
        return TakeFuture();
    }

    public FakeCamFuture GenerateToolpath(IFusionCamToolpathTarget target)
    {
        GenerateToolpathTargets.Add(target);
        return TakeFuture();
    }

    public void PostProcess(IFusionCamPostTarget target, IFusionCamPostInput input)
        => PostProcessCalls.Add((target, input));

    public IFusionCamPostInput CreatePostProcessInput(string setupName, string postConfigPath, string outputFolder, FusionCamOutputUnits units)
    {
        var input = new FakeCamPostInput
        {
            SetupName = setupName,
            PostConfigPath = postConfigPath,
            OutputFolder = outputFolder,
            Units = units,
        };
        CreatedPostInputs.Add(input);
        return input;
    }

    IFusionCamSetupCollection IFusionCamProduct.Setups => Setups;
    IFusionCamFuture IFusionCamProduct.GenerateAllToolpaths(bool overwrite)
    {
        GenerateAllCalls.Add(overwrite);
        return TakeFuture();
    }

    IFusionCamFuture IFusionCamProduct.GenerateToolpath(IFusionCamToolpathTarget target)
    {
        GenerateToolpathTargets.Add(target);
        return TakeFuture();
    }
}

public sealed class FakeCamSetupCollection : IFusionCamSetupCollection
{
    public List<FakeCamSetup> Setups { get; } = new();

    public int Count => Setups.Count;
    public IFusionCamSetup Item(int index) => Setups[index];

    public IFusionCamSetupInput CreateInput(FusionCamOperationType operationType)
        => new FakeCamSetupInput { OperationType = operationType };

    public IFusionCamSetup Add(IFusionCamSetupInput input)
    {
        var setup = new FakeCamSetup
        {
            Name = (input as FakeCamSetupInput)?.Name ?? $"Setup{Setups.Count + 1}",
            Models = (input as FakeCamSetupInput)?.Models,
            Input = input as FakeCamSetupInput,
        };
        Setups.Add(setup);
        return setup;
    }
}

public sealed class FakeCamSetupInput : IFusionCamSetupInput
{
    public FusionCamOperationType OperationType { get; internal set; }
    public IReadOnlyList<IFusionBody>? Models { get; private set; }
    public string? Name { get; set; }
    public void SetModels(IReadOnlyList<IFusionBody> bodies) => Models = bodies;
}

public sealed class FakeCamSetup : IFusionCamSetup
{
    public string Name { get; set; } = "";
    public bool IsValid { get; set; } = true;
    public FakeCamOperationCollection Operations { get; } = new();
    public IReadOnlyList<IFusionBody>? Models { get; set; }
    public FakeCamSetupInput? Input { get; set; }

    public Dictionary<string, string> SetParameters { get; } = new();
    public bool SetParameterSucceeds { get; set; } = true;

    public bool SetParameter(string parameterName, string expression)
    {
        SetParameters[parameterName] = expression;
        return SetParameterSucceeds;
    }

    IFusionCamOperationCollection IFusionCamSetup.Operations => Operations;
}

public sealed class FakeCamOperationCollection : IFusionCamOperationCollection
{
    public List<FakeCamOperation> Operations { get; } = new();
    public List<FakeCamOperationInput> CreatedInputs { get; } = new();

    /// <summary>When true, <see cref="Add"/> returns null (the reference "op is None" path).</summary>
    public bool AddReturnsNull { get; set; }

    public int Count => Operations.Count;
    public IFusionCamOperation Item(int index) => Operations[index];

    public IFusionCamOperationInput CreateInput(string strategy)
    {
        var input = new FakeCamOperationInput { Strategy = strategy };
        CreatedInputs.Add(input);
        return input;
    }

    public IFusionCamOperation? Add(IFusionCamOperationInput input)
    {
        if (AddReturnsNull)
        {
            return null;
        }

        var operation = new FakeCamOperation
        {
            Name = (input as FakeCamOperationInput)?.Name ?? $"Op{Operations.Count + 1}",
            Input = input as FakeCamOperationInput,
        };
        Operations.Add(operation);
        return operation;
    }
}

public sealed class FakeCamOperationInput : IFusionCamOperationInput
{
    public string Strategy { get; internal set; } = "";
    public string? Name { get; set; }
}

public sealed class FakeCamOperation : IFusionCamOperation
{
    public string Name { get; set; } = "";
    public bool HasToolpath { get; set; }
    public bool IsValid { get; set; } = true;
    public FakeCamToolInfo? Tool { get; set; }
    public FakeCamOperationInput? Input { get; set; }
    public List<FakeCamParameter> ParametersList { get; } = new();

    /// <summary>SetParameter calls: name to last expression.</summary>
    public Dictionary<string, string> SetParameters { get; } = new();
    public bool SetParameterSucceeds { get; set; } = true;

    IReadOnlyList<IFusionCamParameter> IFusionCamOperation.Parameters => ParametersList;
    IFusionCamToolInfo? IFusionCamOperation.Tool => Tool;

    public bool SetParameter(string parameterName, string expression)
    {
        SetParameters[parameterName] = expression;
        return SetParameterSucceeds;
    }
}

public sealed class FakeCamParameter : IFusionCamParameter
{
    public string Name { get; set; } = "";
    public string? Expression { get; set; }

    /// <summary>When true, <see cref="TryGetExpression"/> fails (reference except path).</summary>
    public bool ExpressionFails { get; set; }

    public bool TryGetExpression(out string expression)
    {
        expression = Expression ?? string.Empty;
        return !ExpressionFails;
    }
}

public sealed class FakeCamFuture : IFusionCamFuture
{
    public int WaitCalls { get; private set; }
    public bool NeverComplete { get; set; }
    public int CompleteAfterChecks { get; set; }
    private int _checks;
    public bool IsCompleted => !NeverComplete && Interlocked.Increment(ref _checks) > CompleteAfterChecks;
    public void Wait() => WaitCalls++;
}

public sealed class FakeCamPostInput : IFusionCamPostInput
{
    /// <summary>The create(...) args this input was built from.</summary>
    public string SetupName { get; set; } = "";
    public string PostConfigPath { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public FusionCamOutputUnits Units { get; set; }
    public bool OpenInEditor { get; set; }
}

public sealed class FakeCamToolInfo : IFusionCamToolInfo
{
    public string Description { get; set; } = "";
}
