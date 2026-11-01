using Fusion360.Mcp.Core.Catalog;

namespace Fusion360.Mcp.Core.Tests;

/// <summary>
/// ToolCatalog invariants — mirrors the reference tests/test_tools.py
/// checks against the C# data declarations (D6 single source of truth).
/// The live parity against the reference tools.py is enforced by
/// scripts/check_tool_parity.py (run in CI/locally; this test guards
/// the shape of the embedded catalog itself).
/// </summary>
public class ToolCatalogTests
{
    // This port intentionally excludes the reference's arbitrary-code tool.
    private const int PortToolCount = 92;

    [Fact]
    public void Catalog_ContainsTheSupportedPortSurface()
    {
        Assert.Equal(PortToolCount, ToolCatalog.Count);
        Assert.Equal(PortToolCount, ToolCatalog.All.Count);
        Assert.Null(ToolCatalog.GetByName("execute_code"));
    }

    [Fact]
    public void ToolEffects_CoversAllNonReadOnlyToolsAndRevisionScope()
    {
        var operationTools = ToolCatalog.All
            .Where(tool => !tool.Annotations.ReadOnlyHint)
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        var revisionTools = operationTools.Except(new[]
        {
            "export_stl", "export_step", "export_f3d", "export_view_sheet", "export", "cam_post_process",
        }).ToHashSet(StringComparer.Ordinal);

        Assert.True(operationTools.SetEquals(ToolEffects.OperationCommands));
        Assert.True(revisionTools.SetEquals(ToolEffects.DocumentMutationCommands));
        foreach (var tool in ToolCatalog.All)
        {
            var properties = tool.InputSchema["properties"]!.AsObject();
            Assert.Equal(operationTools.Contains(tool.Name), properties.ContainsKey("operation_id"));
            Assert.Equal(revisionTools.Contains(tool.Name), properties.ContainsKey("expected_revision"));
        }
    }

    [Fact]
    public void AllToolsHaveRequiredKeys()
    {
        foreach (var tool in ToolCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name), $"tool with missing name: {tool}");
            Assert.False(string.IsNullOrWhiteSpace(tool.Title), $"tool {tool.Name} missing title");
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"tool {tool.Name} missing description");
            Assert.NotNull(tool.InputSchema);
            Assert.Equal("object", tool.InputSchema["type"]?.GetValue<string>());
            Assert.NotNull(tool.Annotations);
        }
    }

    [Fact]
    public void NoDuplicateToolNames()
    {
        var names = ToolCatalog.All.Select(t => t.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void RequiredFieldsExistInProperties()
    {
        // Every field listed in "required" must also appear in "properties"
        // (reference test_required_fields_exist_in_properties).
        foreach (var tool in ToolCatalog.All)
        {
            var required = tool.InputSchema.TryGetPropertyValue("required", out var requiredNode)
                ? requiredNode!.AsArray().Select(n => n?.GetValue<string>() ?? string.Empty).ToList()
                : new List<string>();
            var properties = tool.InputSchema.TryGetPropertyValue("properties", out var propsNode)
                ? propsNode!.AsObject()
                : new System.Text.Json.Nodes.JsonObject();

            foreach (var field in required)
            {
                Assert.True(properties.ContainsKey(field),
                    $"tool {tool.Name}: required field '{field}' not in properties");
            }
        }
    }

    [Fact]
    public void ToolNames_AreSnakeCase()
    {
        foreach (var tool in ToolCatalog.All)
        {
            Assert.Matches("^[a-z][a-z0-9_]*$", tool.Name);
        }
    }

    [Fact]
    public void GetByName_IsCaseSensitiveAndExact()
    {
        Assert.NotNull(ToolCatalog.GetByName("ping"));
        Assert.NotNull(ToolCatalog.GetByName("extrude"));
        Assert.Null(ToolCatalog.GetByName("nonexistent_tool"));
        Assert.Null(ToolCatalog.GetByName("PING"));
        Assert.Null(ToolCatalog.GetByName("ping "));
        Assert.Null(ToolCatalog.GetByName(""));
    }

    [Fact]
    public void Annotations_MatchReferenceSets()
    {
        // Port of the reference _READ_ONLY / _DESTRUCTIVE / _IDEMPOTENT sets.
        var readOnly = new HashSet<string>
        {
            "get_scene_info", "get_object_info", "get_bounding_box", "list_components",
            "get_parameters", "get_physical_properties", "measure_distance", "measure_angle",
            "check_interference", "compare_meshes", "ping", "cam_list_setups",
            "cam_list_operations", "cam_get_operation_info", "get_design_type", "render_view",
        };
        var destructive = new HashSet<string> { "delete_all", "delete_parameter" };
        var idempotent = new HashSet<string>
        {
            "ping", "get_scene_info", "get_object_info", "get_bounding_box", "list_components",
            "get_parameters", "get_physical_properties", "measure_distance", "measure_angle",
            "check_interference", "compare_meshes", "set_parameter", "set_appearance",
            "set_color", "cam_list_setups", "cam_list_operations", "cam_get_operation_info",
            "get_design_type", "set_design_type", "rename_body", "render_view",
        };

        foreach (var tool in ToolCatalog.All)
        {
            Assert.True(readOnly.Contains(tool.Name) == tool.Annotations.ReadOnlyHint,
                $"readOnlyHint mismatch for {tool.Name}");
            Assert.True(destructive.Contains(tool.Name) == tool.Annotations.DestructiveHint,
                $"destructiveHint mismatch for {tool.Name}");
            Assert.True(idempotent.Contains(tool.Name) == tool.Annotations.IdempotentHint,
                $"idempotentHint mismatch for {tool.Name}");
        }
    }

    [Fact]
    public void ExportCatalogJson_IsAValidArrayOfDeclarations()
    {
        var json = ToolCatalog.ExportCatalogJson();
        var array = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsArray();

        Assert.Equal(ToolCatalog.Count, array.Count());
        foreach (var entry in array)
        {
            Assert.Contains("name", entry!.AsObject());
            Assert.Contains("inputSchema", entry.AsObject());
            Assert.Contains("annotations", entry.AsObject());
        }
    }

    [Fact]
    public void OperationId_IsAnOptionalExtension_OnEveryMutationTool_Only()
    {
        // P2 / ADR-0010: the C#-side catalog extension — an optional
        // `operation_id` on every effectful tool (never required, and never
        // on read-only tools). The reference catalog has no such property,
        // so this is asserted against the C# SSOT; scripts/check_tool_parity.py
        // is the lockstep gate with the reference.
        var mutations = ToolEffects.OperationCommands;

        foreach (var tool in ToolCatalog.All)
        {
            var hasOperationId = tool.InputSchema.TryGetPropertyValue("properties", out var propsNode)
                && propsNode!.AsObject().ContainsKey("operation_id");
            var isRequired = tool.InputSchema.TryGetPropertyValue("required", out var requiredNode)
                && requiredNode!.AsArray().Any(n => n?.GetValue<string>() == "operation_id");

            if (mutations.Contains(tool.Name))
            {
                Assert.True(hasOperationId, $"mutation tool {tool.Name} must carry the optional operation_id extension");
            }
            else
            {
                Assert.False(hasOperationId, $"non-mutation tool {tool.Name} must not carry operation_id");
            }

            Assert.False(isRequired, $"'operation_id' must never be required (tool {tool.Name})");
        }
    }
}
