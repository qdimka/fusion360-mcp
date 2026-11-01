using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Catalog;
using Fusion360.Mcp.Core.Mock;
using Fusion360.Mcp.Server;

namespace Fusion360.Mcp.Server.Tests;

/// <summary>
/// Fast in-process proof that mock mode "responds to any tool": every one of
/// the 93 catalog tools answers with a well-formed <c>ok:true</c> /
/// <c>mode:mock</c> result for empty arguments, and the forced-error sentinel
/// produces a classified failure.
/// </summary>
public class MockToolBackendTests
{
    [Fact]
    public void AllCatalogTools_RespondOk_WithEmptyArgs()
    {
        var backend = new MockToolBackend();
        var failures = new List<string>();

        foreach (var tool in ToolCatalog.All)
        {
            var result = backend.Invoke(tool.Name, new JsonObject());

            bool ok = result.TryGetPropertyValue("ok", out var okNode) && okNode is JsonValue ov && ov.GetValue<bool>();
            bool mock = result.TryGetPropertyValue("mode", out var modeNode) && modeNode is JsonValue mv && mv.GetValue<string>() == "mock";
            if (!ok || !mock)
            {
                failures.Add($"'{tool.Name}' ok={ok} mock={mock}");
            }
        }

        Assert.True(failures.Count == 0,
            $"Tools that did not return ok:true + mode:mock ({failures.Count}): {string.Join("; ", failures)}");
    }

    [Fact]
    public void AllCatalogTools_CarriesModeMockMarker()
    {
        // The acceptance criterion: "--mode mock returns a mock result marker
        // for every tool". Spot-assert the marker is literally present.
        var backend = new MockToolBackend();
        foreach (var name in ToolCatalog.All.Select(t => t.Name).Take(10))
        {
            var result = backend.Invoke(name, new JsonObject());
            var json = result.ToJsonString();
            Assert.Contains("\"mode\":\"mock\"", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ForcedError_Sentinel_ProducesClassifiedFailure()
    {
        var backend = new MockToolBackend();
        var args = new JsonObject { [MockResponses.MockErrorSentinel] = "boom" };

        var result = backend.Invoke("get_scene_info", args);

        Assert.True(result.TryGetPropertyValue("ok", out var ok) && ok is JsonValue ov && ov.GetValue<bool>() == false,
            "forced error must set ok:false");
        Assert.True(result.TryGetPropertyValue("error_kind", out _), "forced error must carry error_kind");
        Assert.True(result.TryGetPropertyValue("mode", out var mode) && mode is JsonValue mv && mv.GetValue<string>() == "mock");
    }

    [Fact]
    public void MutationTool_ReturnsSyntheticDeltas()
    {
        var backend = new MockToolBackend();
        var result = backend.Invoke("extrude", new JsonObject { ["sketch"] = "S1", ["distance"] = 2.0 });

        Assert.True(result.TryGetPropertyValue("deltas", out var deltas), "mutation tools return deltas");
        Assert.NotNull(deltas);
        Assert.Contains("mass_g", deltas!.ToJsonString());
    }

    [Fact]
    public void Reset_IsNoOpInMockMode()
    {
        // Must not throw — there is nothing to drop.
        var backend = new MockToolBackend();
        backend.Reset();
        // backend still usable after reset
        var result = backend.Invoke("ping", new JsonObject());
        Assert.True(result.TryGetPropertyValue("mode", out var m) && m is JsonValue mv && mv.GetValue<string>() == "mock");
    }
}
