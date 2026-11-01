using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Catalog;
using Fusion360.Mcp.Core.Errors;
using Fusion360.Mcp.Core.Mock;

namespace Fusion360.Mcp.Core.Tests;

/// <summary>
/// Mock layer: every tool in the catalog gets a mock response, mutations
/// carry synthetic deltas, and the __mock_error__ sentinel produces
/// classified errors (port of the reference mock.py behaviour).
/// </summary>
public class MockResponsesTests
{
    [Fact]
    public void EveryCatalogTool_HasMockResponse()
    {
        // Criterion: mock data for every tool in the catalog.
        foreach (var tool in ToolCatalog.All)
        {
            var result = MockResponses.MockCommand(tool.Name);
            Assert.False(result.TryGetPropertyValue("warning", out _),
                $"tool {tool.Name} hit the default-mock fallback — no dedicated handler");
            Assert.True(result.TryGetPropertyValue("ok", out var ok) && ok!.GetValue<bool>());
            Assert.Equal("mock", result["mode"]?.GetValue<string>());
        }
    }

    [Fact]
    public void EveryMutationCommand_GetsSyntheticDeltas()
    {
        var mutated = ToolCatalog.All.Where(t => MockResponses.MutationCommands.Contains(t.Name)).ToList();
        Assert.NotEmpty(mutated);

        foreach (var tool in mutated)
        {
            var result = MockResponses.MockCommand(tool.Name);
            var deltas = result["deltas"] as JsonObject
                ?? throw new System.InvalidOperationException($"tool {tool.Name} missing deltas");

            Assert.True(deltas.ContainsKey("body_count_delta"), $"{tool.Name} deltas lack body_count_delta");
            Assert.True(deltas.ContainsKey("mass_g_delta"), $"{tool.Name} deltas lack mass_g_delta");
            Assert.True(deltas.ContainsKey("bbox_after"), $"{tool.Name} deltas lack bbox_after");
        }
    }

    [Fact]
    public void Ping_IsNotAMutation()
    {
        var result = MockResponses.MockCommand("ping");
        Assert.Equal("pong", result["status"]?.GetValue<string>());
        Assert.False(result.ContainsKey("deltas"));
    }

    [Fact]
    public void UnknownCommand_FallsBackToDefaultMock()
    {
        var result = MockResponses.MockCommand("frobnicate");

        Assert.Equal("no mock handler for this command", result["warning"]?.GetValue<string>());
        Assert.NotNull(result["params_received"]);
        Assert.True(result.ContainsKey("ok"));
    }

    [Theory]
    [InlineData("No profiles in sketch", ErrorKind.ProfileNotClosed)]
    [InlineData("Sketch 'A' not found", ErrorKind.SketchNotFound)]
    [InlineData("Boolean subtract failed — empty result", ErrorKind.BooleanNoOp)]
    [InlineData("no active design", ErrorKind.NoActiveDesign)]
    [InlineData("totally new failure", ErrorKind.Unknown)]
    public void MockErrorSentinel_ForceClassifiedError(string message, ErrorKind expectedKind)
    {
        var result = MockResponses.MockCommand("extrude", new JsonObject
        {
            ["height"] = 3.0,
            ["__mock_error__"] = message,
        });

        Assert.False(result["ok"]!.GetValue<bool>());
        Assert.Equal(expectedKind.ToWireString(), result["error_kind"]?.GetValue<string>());
        Assert.Equal(message, result["error_message"]?.GetValue<string>());
        Assert.Contains($"MockError: {message}", result["traceback"]?.GetValue<string>() ?? string.Empty);
        Assert.Equal("mock", result["mode"]?.GetValue<string>());

        // Classified errors carry hints for known kinds.
        if (expectedKind != ErrorKind.Unknown)
        {
            Assert.True(result["hints"] is JsonArray hints && hints.Count > 0,
                $"expected hints for {expectedKind}");
        }
    }

    [Fact]
    public void CreateParameter_MirrorsUnitNormalisation()
    {
        // "unit" is required by the schema; omitted → "mm", blank → unitless.
        var withUnit = MockResponses.MockCommand("create_parameter", new JsonObject
        {
            ["name"] = "width",
            ["value"] = 10,
            ["unit"] = "mm",
        });
        Assert.Equal("mm", withUnit["unit"]?.GetValue<string>());
        Assert.Equal("10 mm", withUnit["expression"]?.GetValue<string>());

        var missingUnit = MockResponses.MockCommand("create_parameter", new JsonObject
        {
            ["name"] = "width",
            ["value"] = 10,
        });
        Assert.Equal("mm", missingUnit["unit"]?.GetValue<string>());

        var blankUnit = MockResponses.MockCommand("create_parameter", new JsonObject
        {
            ["name"] = "width",
            ["value"] = 10,
            ["unit"] = "  ",
        });
        Assert.Equal("", blankUnit["unit"]?.GetValue<string>());
        Assert.Equal("10", blankUnit["expression"]?.GetValue<string>());
    }

    [Fact]
    public void CreateHole_BodyNameFallsBackToIndex()
    {
        var byName = MockResponses.MockCommand("create_hole", new JsonObject { ["body_name"] = "Shaft" });
        Assert.Equal("Shaft", byName["body_name"]?.GetValue<string>());

        var byIndex = MockResponses.MockCommand("create_hole", new JsonObject { ["body_index"] = 2 });
        Assert.Equal("Body3", byIndex["body_name"]?.GetValue<string>());

        var default_ = MockResponses.MockCommand("create_hole");
        Assert.Equal("Body1", default_["body_name"]?.GetValue<string>());
    }

    [Fact]
    public void Export_DerivesFormatFromPath()
    {
        var explicit_ = MockResponses.MockCommand("export", new JsonObject
        {
            ["body_name"] = "Body1",
            ["file_path"] = "~/Desktop/out.step",
            ["format"] = "step",
        });
        Assert.Equal("step", explicit_["format"]?.GetValue<string>());
        Assert.Equal("~/Desktop/out.step", explicit_["file_path"]?.GetValue<string>());

        var fromPath = MockResponses.MockCommand("export", new JsonObject
        {
            ["body_name"] = "Body1",
            ["file_path"] = "~/Desktop/out.stl",
        });
        Assert.Equal("stl", fromPath["format"]?.GetValue<string>());

        var noFormat = MockResponses.MockCommand("export", new JsonObject { ["body_name"] = "Body1" });
        Assert.Equal("f3d", noFormat["format"]?.GetValue<string>());
        Assert.Equal("~/Desktop/Body1.f3d", noFormat["file_path"]?.GetValue<string>());
    }

    [Fact]
    public void RenderView_CarriesMockPng()
    {
        var result = MockResponses.MockCommand("render_view", new JsonObject { ["view"] = "iso" });

        Assert.Equal("iso", result["view"]?.GetValue<string>());
        Assert.Equal(1024, result["width"]!.GetValue<int>());
        Assert.Equal(768, result["height"]!.GetValue<int>());
        Assert.Equal("png", result["image_format"]?.GetValue<string>());
        var bytes = result["bytes"]!.GetValue<int>();
        Assert.Equal(result["image_base64"]!.GetValue<string>()!.Length, bytes);
    }

    [Fact]
    public void SetParameter_ReportsNullUnitAndExpression()
    {
        // Mock mode holds no design: report null rather than invent "mm"
        // (reference _set_parameter semantics).
        var result = MockResponses.MockCommand("set_parameter", new JsonObject
        {
            ["name"] = "width",
            ["value"] = 12,
        });

        // JSON null is stored as a null node reference in STJ — the
        // property must be PRESENT with a null value (not absent).
        Assert.True(result.TryGetPropertyValue("unit", out var unit) && unit is null);
        Assert.True(result.TryGetPropertyValue("expression", out var expression) && expression is null);
    }

    // ------------------------------------------------------------------
    // P1 (ADR-0009): the mock carries the optional verification block
    // ------------------------------------------------------------------

    [Fact]
    public void EveryMutationCommand_GetsMockVerification()
    {
        // A mock has no live scene: every mutation reports the lowest rung
        // with a named reason — never the block itself hidden.
        foreach (var tool in ToolCatalog.All.Where(t => MockResponses.MutationCommands.Contains(t.Name)))
        {
            var result = MockResponses.MockCommand(tool.Name);
            var verification = result["verification"] as JsonObject
                ?? throw new System.InvalidOperationException($"{tool.Name} missing the verification block");

            Assert.Equal("call_returned", verification["level"]!.GetValue<string>());
            Assert.IsType<JsonArray>(verification["checks"]!);
            var aspects = verification["unverified_aspects"]!.AsArray();
            Assert.True(aspects.Count > 0, $"{tool.Name}: a call_returned mock must name why nothing is verified");
        }
    }

    [Fact]
    public void NonMutationCommands_HaveNoVerificationBlock()
    {
        Assert.False(MockResponses.MockCommand("ping").ContainsKey("verification"));
        Assert.False(MockResponses.MockCommand("get_scene_info").ContainsKey("verification"));
        Assert.False(MockResponses.MockCommand("measure_distance").ContainsKey("verification"));
    }
}
