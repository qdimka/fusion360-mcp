using System.Text.Json.Nodes;
using Fusion360.Mcp.AddIn;
using Xunit;

namespace Fusion360.Mcp.AddIn.Tests;

/// <summary>
/// P3 (ADR-0011) lightweight revisioning against the real
/// <see cref="CommandHandler" /> dispatch with the in-memory
/// <see cref="FakeFusionContext" />: the per-document model_revision
/// counter (exposed in get_scene_info / get_object_info), the
/// expected_revision pre-check (REVISION_CONFLICT before the model
/// moves), revision_after on success and on failure, and the
/// STALE_REFERENCE vs BODY_NOT_FOUND/SKETCH_NOT_FOUND distinction in
/// name lookups (names the scene previously reported are "stale";
/// never-seen names are simply "not found").
/// </summary>
public class RevisionTests
{
    public RevisionTests()
    {
        ModelRevision.ResetForTests();
    }

    private static JsonNode Frame(string type, string? paramsJson = null)
    {
        var root = new JsonObject { ["type"] = type };
        root["params"] = paramsJson is null ? new JsonObject() : JsonNode.Parse(paramsJson)!;
        return root;
    }

    private static JsonObject Run(FakeFusionContext ctx, string type, string? paramsJson = null)
        => new CommandHandler(ctx).ExecuteCommand(Frame(type, paramsJson)).Root["result"]!.AsObject();

    [Fact]
    public void Mutation_Success_IncrementsModelRevision_AndReportsRevisionAfter()
    {
        var ctx = new FakeFusionContext();

        var scene = Run(ctx, "get_scene_info");
        Assert.Equal(0, scene["model_revision"]!.GetValue<long>());

        var created = Run(ctx, "create_box", """{"length":2,"width":2,"height":2}""");
        Assert.True(created["ok"]!.GetValue<bool>());
        Assert.Equal(1, created["revision_after"]!.GetValue<long>());

        // The scene read reports the new revision.
        var sceneAfter = Run(ctx, "get_scene_info");
        Assert.Equal(1, sceneAfter["model_revision"]!.GetValue<long>());

        var moved = Run(ctx, "move_body", """{"body_name":"Box1","z":1}""");
        Assert.Equal(2, moved["revision_after"]!.GetValue<long>());

        // get_object_info also exposes the document revision.
        var info = Run(ctx, "get_object_info", """{"name":"Box1"}""");
        Assert.True(info["found"]!.GetValue<bool>());
        Assert.Equal(2, info["model_revision"]!.GetValue<long>());
    }

    [Fact]
    public void StaleExpectedRevision_RejectedBeforeExecution_ModelUntouched()
    {
        var ctx = new FakeFusionContext();
        Run(ctx, "create_box"); // revision 1

        var rejected = Run(ctx, "create_box", """{"length":1,"width":1,"height":1,"expected_revision":0}""");
        Assert.False(rejected["ok"]!.GetValue<bool>());
        Assert.Equal("REVISION_CONFLICT", rejected["error_kind"]!.GetValue<string>());
        Assert.Equal(1, rejected["revision_after"]!.GetValue<long>());
        Assert.Contains(rejected["hints"]!.AsArray().Select(h => h?.GetValue<string>() ?? string.Empty),
            h => h.StartsWith("Re-read get_scene_info", StringComparison.Ordinal));
        Assert.DoesNotContain("deltas", rejected);

        // Acceptance criterion: the rejected mutation did not touch the
        // model — the fake BRep manager was never invoked and the
        // revision counter did not move.
        Assert.Equal(1, ctx.Design.RootComponent.BRepManagerImpl.CreateBoxCalls);
        Assert.Equal(1, ModelRevision.Current(ctx.Design.DocumentIdentity));

        // A matching expected_revision passes the guard.
        var accepted = Run(ctx, "create_box", """{"length":1,"width":1,"height":1,"expected_revision":1}""");
        Assert.True(accepted["ok"]!.GetValue<bool>());
        Assert.Equal(2, accepted["revision_after"]!.GetValue<long>());
        Assert.Equal(2, ctx.Design.RootComponent.BRepManagerImpl.CreateBoxCalls);
    }

    [Fact]
    public void FailedMutation_RevisionAfterCarriesCurrentRevision()
    {
        var ctx = new FakeFusionContext();
        Run(ctx, "create_box"); // revision 1

        // A failing mutation (missing body) moves nothing.
        var failed = Run(ctx, "move_body", """{"body_name":"GhostBody","z":1}""");
        Assert.False(failed["ok"]!.GetValue<bool>());
        Assert.Equal("BODY_NOT_FOUND", failed["error_kind"]!.GetValue<string>());
        Assert.Equal(1, failed["revision_after"]!.GetValue<long>());
        Assert.Equal(1, ModelRevision.Current(ctx.Design.DocumentIdentity));
    }

    [Fact]
    public void ReportedNameThatVanished_ClassifiesAsStaleReference()
    {
        var ctx = new FakeFusionContext();
        ctx.Design.RootComponent.AddBody(new FakeBody { Name = "Box1" });
        Run(ctx, "get_scene_info"); // reports "Box1" to the agent

        // The body vanishes from the scene (user deleted/renamed it, or
        // another agent acted between the read and the mutation).
        ctx.Design.RootComponent.Bodies.Clear();
        Run(ctx, "get_scene_info");

        var failed = Run(ctx, "move_body", """{"body_name":"Box1","z":1}""");
        Assert.False(failed["ok"]!.GetValue<bool>());
        Assert.Equal("STALE_REFERENCE", failed["error_kind"]!.GetValue<string>());
        Assert.Contains(failed["hints"]!.AsArray().Select(h => h?.GetValue<string>() ?? string.Empty),
            h => h.StartsWith("Re-read the scene with get_scene_info", StringComparison.Ordinal));
    }

    [Fact]
    public void NeverReportedName_ClassifiesAsBodyNotFound()
    {
        var ctx = new FakeFusionContext();

        var failed = Run(ctx, "move_body", """{"body_name":"GhostBody","z":1}""");
        Assert.Equal("BODY_NOT_FOUND", failed["error_kind"]!.GetValue<string>());
    }

    [Fact]
    public void ReportedSketchThatVanished_ClassifiesAsStaleReference()
    {
        var ctx = new FakeFusionContext();
        ctx.Design.RootComponent.AddSketch(new FakeSketch { Name = "Sketch1" });
        Run(ctx, "get_scene_info"); // reports "Sketch1" to the agent

        ctx.Design.RootComponent.Sketches.Clear();

        var failed = Run(ctx, "auto_constrain", """{"sketch_name":"Sketch1"}""");
        Assert.Equal("STALE_REFERENCE", failed["error_kind"]!.GetValue<string>());
    }

    [Fact]
    public void RevisionCounter_IsPerDocument()
    {
        var designA = new FakeFusionContext();
        var designB = new FakeFusionContext { };
        designA.Design.DocumentName = "SameName.fuse";
        designB.Design.DocumentName = "SameName.fuse";

        var a = Run(designA, "create_box");
        var b = Run(designB, "create_box");

        Assert.Equal(1, a["revision_after"]!.GetValue<long>());
        Assert.Equal(1, b["revision_after"]!.GetValue<long>());
        Assert.Equal(2, Run(designA, "create_box")["revision_after"]!.GetValue<long>());
        Assert.Equal(1, ModelRevision.Current(designB.Design.DocumentIdentity));
    }

    [Fact]
    public void ReportedNames_AreScopedByDocumentIdentity()
    {
        var designA = new FakeFusionContext();
        var designB = new FakeFusionContext();
        designA.Design.DocumentName = "SameName.fuse";
        designB.Design.DocumentName = "SameName.fuse";
        designA.Design.RootComponent.AddBody(new FakeBody { Name = "SharedBodyName" });
        Run(designA, "get_scene_info");
        designA.Design.RootComponent.Bodies.Clear();

        var missingInOtherDocument = Run(
            designB, "move_body", """{"body_name":"SharedBodyName","z":1}""");

        Assert.Equal("BODY_NOT_FOUND", missingInOtherDocument["error_kind"]!.GetValue<string>());
    }

    [Fact]
    public void RevisionCounter_SurvivesDisplayNameChange()
    {
        var ctx = new FakeFusionContext();
        var first = Run(ctx, "create_box");
        ctx.Design.DocumentName = "Renamed.fuse";

        var second = Run(ctx, "create_box", """{"expected_revision":1}""");

        Assert.Equal(1, first["revision_after"]!.GetValue<long>());
        Assert.True(second["ok"]!.GetValue<bool>());
        Assert.Equal(2, second["revision_after"]!.GetValue<long>());
    }
}
