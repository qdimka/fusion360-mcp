using System;
using System.Text.Json.Nodes;
using Fusion360.Mcp.AddIn;
using Xunit;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// P4 (docs/TOOL_CONTRACTS.md §1.2/§5.2): the <c>partial_effects</c> marker
/// on failed mutations. A handler that moved the model before throwing
/// carries <c>partial_effects: true</c> plus the <c>deltas</c> that
/// enumerate the change; a clean failure and every TIMEOUT state
/// <c>partial_effects: false</c> explicitly (the bridge cancels timed-out
/// commands, so the model was not modified — no KOMPAS
/// <c>outcome_unknown</c> analogue; the bridge-side marker is covered by
/// EventBridgeTests.Timeout_CancelsTheItem_AndDrainSkipsTheLateExecution).
/// </summary>
public class PartialEffectsTests
{
    public PartialEffectsTests()
    {
        ModelRevision.ResetForTests();
    }

    private static SceneSnapshot Snap(int bodies, double mass, FusionPoint? min = null, FusionPoint? max = null)
        => new(bodies, mass, min, max);

    private static JsonNode Frame(string type, string? paramsJson = null)
    {
        var root = new JsonObject { ["type"] = type };
        root["params"] = paramsJson is null ? new JsonObject() : JsonNode.Parse(paramsJson)!;
        return root;
    }

    private static JsonObject Run(FakeFusionContext ctx, string type, string? paramsJson = null)
        => new CommandHandler(ctx).ExecuteCommand(Frame(type, paramsJson)).Root["result"]!.AsObject();

    [Fact]
    public void MutationThatMovedTheModelAndThenThrew_StatesPartialEffectsTrueWithDeltas()
    {
        var ctx = new FakeFusionContext();
        // P1 snapshot sequence: call 1 = pre-mutation, call 2 = the scene
        // state the catch path reads after the handler threw.
        ctx.SnapshotSequence.Add(Snap(0, 0.0));
        ctx.SnapshotSequence.Add(Snap(1, 4.0, new FusionPoint(0, 0, 0), new FusionPoint(2, 1, 2)));

        // The fake records the body first and THEN throws — a mutation
        // that moved the model partway and only then failed.
        ctx.Design.RootComponent.BRepManagerImpl.ThrowAfterMutation =
            new InvalidOperationException("BRep kernel error: face not closed");

        var result = Run(ctx, "create_box", """{"length":2,"width":1,"height":2}""");

        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.True(result["partial_effects"]!.GetValue<bool>(),
            "a failure that moved the model must state partial_effects: true");

        // The change is enumerated in the failure's own deltas.
        var deltas = result["deltas"]!.AsObject()!;
        Assert.Equal(0, deltas["body_count_before"]!.GetValue<int>());
        Assert.Equal(1, deltas["body_count_after"]!.GetValue<int>());
        Assert.Equal(1, deltas["body_count_delta"]!.GetValue<int>());
        Assert.Equal(0.0, deltas["mass_g_before"]!.GetValue<double>());
        Assert.Equal(4.0, deltas["mass_g_after"]!.GetValue<double>());
        Assert.Equal(4.0, deltas["mass_g_delta"]!.GetValue<double>());
        Assert.NotNull(deltas["bbox_after"]!);
        Assert.Null(deltas["bbox_before"]!);
    }

    [Fact]
    public void MutationThatChangedOnlyMassAndThenThrew_StatesPartialEffectsTrueWithMassDelta()
    {
        var ctx = new FakeFusionContext();
        // Same body count, different total mass: a mass-only partial effect.
        ctx.SnapshotSequence.Add(Snap(1, 4.0));
        ctx.SnapshotSequence.Add(Snap(1, 9.0));

        ctx.Design.RootComponent.BRepManagerImpl.ThrowAfterMutation =
            new InvalidOperationException("BRep kernel error: mass recompute failed");

        var result = Run(ctx, "create_box", """{"length":2,"width":1,"height":2}""");

        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.True(result["partial_effects"]!.GetValue<bool>(),
            "a mass-only model move is a partial effect");

        var deltas = result["deltas"]!.AsObject()!;
        Assert.Equal(0, deltas["body_count_delta"]!.GetValue<int>());
        Assert.Equal(4.0, deltas["mass_g_before"]!.GetValue<double>());
        Assert.Equal(9.0, deltas["mass_g_after"]!.GetValue<double>());
        Assert.Equal(5.0, deltas["mass_g_delta"]!.GetValue<double>()); // the mass change is the delta the failure enumerates
    }

    [Fact]
    public void MutationThatChangedOnlyBBoxAndThenThrew_StatesPartialEffectsTrueWithBBoxDeltas()
    {
        var ctx = new FakeFusionContext();
        // Same body count and mass, different overall bbox: a bbox-only partial effect.
        ctx.SnapshotSequence.Add(Snap(1, 4.0, new FusionPoint(0, 0, 0), new FusionPoint(1, 1, 1)));
        ctx.SnapshotSequence.Add(Snap(1, 4.0, new FusionPoint(0, 0, 0), new FusionPoint(2, 1, 1)));

        ctx.Design.RootComponent.BRepManagerImpl.ThrowAfterMutation =
            new InvalidOperationException("BRep kernel error: face not closed");

        var result = Run(ctx, "create_box", """{"length":2,"width":1,"height":2}""");

        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.True(result["partial_effects"]!.GetValue<bool>(),
            "a bbox-only model move is a partial effect");

        var deltas = result["deltas"]!.AsObject()!;
        Assert.Equal(0, deltas["body_count_delta"]!.GetValue<int>());
        Assert.Equal(0.0, deltas["mass_g_delta"]!.GetValue<double>());
        var beforeMax = deltas["bbox_before"]!.AsObject()!["max"]!.AsArray()!;
        var afterMax = deltas["bbox_after"]!.AsObject()!["max"]!.AsArray()!;
        Assert.Equal(1.0, beforeMax[0]!.GetValue<double>());
        Assert.Equal(2.0, afterMax[0]!.GetValue<double>()); // the bbox move (max.X 1 -> 2) is enumerated in the failure's own bbox deltas
    }

    [Fact]
    public void MutationThatMovedNoMeasuredGeometry_ReportsUnknownOutcome()
    {
        var ctx = new FakeFusionContext();
        ctx.SnapshotSequence.Add(Snap(0, 0.0));
        ctx.SnapshotSequence.Add(Snap(0, 0.0));

        // A failing mutation (missing body) moves nothing.
        var failed = Run(ctx, "move_body", """{"body_name":"GhostBody","z":1}""");

        Assert.False(failed["ok"]!.GetValue<bool>());
        Assert.Equal("BODY_NOT_FOUND", failed["error_kind"]!.GetValue<string>());
        Assert.True(failed["outcome_unknown"]!.GetValue<bool>(),
            "body count, mass and bbox cannot prove that all document state stayed unchanged");
        Assert.False(failed.ContainsKey("partial_effects"));
        Assert.Null(failed["deltas"]);
    }

    [Fact]
    public void RevisionConflictRejection_StatesPartialEffectsFalse()
    {
        var ctx = new FakeFusionContext();
        Run(ctx, "create_box"); // revision 1

        var rejected = Run(ctx, "create_box", """{"length":1,"width":1,"height":1,"expected_revision":0}""");

        Assert.False(rejected["ok"]!.GetValue<bool>());
        Assert.Equal("REVISION_CONFLICT", rejected["error_kind"]!.GetValue<string>());
        Assert.False(rejected["partial_effects"]!.GetValue<bool>(),
            "the expected_revision pre-check never touched the model");
    }

    [Fact]
    public void FailedMutationWithoutSnapshots_ReportsUnknownOutcome()
    {
        var ctx = new FakeFusionContext(); // no Snapshot, no sequence — unreadable scene

        var failed = Run(ctx, "move_body", """{"body_name":"Ghost","z":1}""");

        Assert.False(failed["ok"]!.GetValue<bool>());
        Assert.True(failed["outcome_unknown"]!.GetValue<bool>(),
            "without a readable snapshot, the handler cannot prove the document stayed unchanged");
        Assert.False(failed.ContainsKey("partial_effects"));
    }

    [Fact]
    public void HasPartialEffects_DetectsBodyCountMassAndBBoxDrift()
    {
        var same = Snap(1, 4.0, new FusionPoint(0, 0, 0), new FusionPoint(1, 1, 1));
        var bboxDrift = Snap(1, 4.0, new FusionPoint(0, 0, 0), new FusionPoint(2, 1, 1));

        Assert.False(MutationCommands.HasPartialEffects(same, same),
            "an unchanged scene has no partial effects");
        Assert.True(MutationCommands.HasPartialEffects(same, bboxDrift),
            "a bbox-only drift counts as a partial effect");
        Assert.True(MutationCommands.HasPartialEffects(Snap(0, 0.0), Snap(1, 4.0)),
            "a body-count change counts as a partial effect");
        Assert.True(MutationCommands.HasPartialEffects(Snap(1, 4.0), Snap(1, 5.0)),
            "a mass change counts as a partial effect");
        Assert.False(MutationCommands.HasPartialEffects(Snap(0, 0.0), Snap(0, 0.0)));
        Assert.True(MutationCommands.HasPartialEffects(Snap(0, 0.0), bboxDrift),
            "no-bodies vs bodies: the bboxes differ");
    }
}
