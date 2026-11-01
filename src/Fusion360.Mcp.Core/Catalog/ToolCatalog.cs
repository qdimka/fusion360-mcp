using System.Reflection;
using System.Text.Json.Nodes;

namespace Fusion360.Mcp.Core.Catalog;

/// <summary>MCP tool annotations: side-effect profile for client auto-approval.</summary>
public sealed record ToolAnnotations(bool ReadOnlyHint, bool DestructiveHint, bool IdempotentHint);

/// <summary>
/// One tool in the catalog: name, title, description, JSON-Schema input
/// schema, and annotations. Data-declaration (PLAN.md D6) — the MCP
/// server exposes these as MCP tools, the mock layer keys off
/// <see cref="Name"/>, and the parity script proves this catalog agrees
/// with the reference Python tools.py.
/// </summary>
public sealed class ToolSchema
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public JsonObject InputSchema { get; init; } = new();
    public ToolAnnotations Annotations { get; init; } = new(false, false, false);

    /// <summary>
    /// The full MCP tool declaration as JSON — what the server sends in
    /// its tools list and what the parity checker compares against the
    /// reference catalog.
    /// </summary>
    public JsonObject ToMcpDeclaration()
    {
        var declaration = new JsonObject
        {
            ["name"] = Name,
            ["title"] = Title,
            ["description"] = Description,
            ["inputSchema"] = InputSchema.DeepClone(),
            ["annotations"] = new JsonObject
            {
                ["readOnlyHint"] = Annotations.ReadOnlyHint,
                ["destructiveHint"] = Annotations.DestructiveHint,
                ["idempotentHint"] = Annotations.IdempotentHint,
            },
        };

        return declaration;
    }
}

/// <summary>
/// Single source of truth for the tool catalog (PLAN.md D6).
///
/// The data lives in <c>Catalog/tools_catalog.json</c> — generated from
/// the reference catalog by scripts/generate_tool_catalog.py and embedded
/// in this assembly. Every consumer (MCP server tool list, mock
/// responses, parity script) reads from here, so the schema cannot
/// silently drift between components.
/// </summary>
public static class ToolCatalog
{
    private const string ResourceName = "Fusion360.Mcp.Core.Catalog.tools_catalog.json";

    /// <summary>All tools, in reference-catalog order.</summary>
    public static IReadOnlyList<ToolSchema> All { get; } = Load();

    /// <summary>Number of supported tools in the port catalog.</summary>
    public static int Count => All.Count;

    /// <summary>Looks up a tool by exact name (case-sensitive), or null.</summary>
    public static ToolSchema? GetByName(string name)
    {
        foreach (var tool in All)
        {
            if (tool.Name == name)
            {
                return tool;
            }
        }

        return null;
    }

    /// <summary>
    /// The whole catalog as JSON (array of MCP tool declarations) —
    /// consumed by the parity checker and by server tool-list building.
    /// </summary>
    public static string ExportCatalogJson()
    {
        var array = new JsonArray();
        foreach (var tool in All)
        {
            array.Add(tool.ToMcpDeclaration());
        }

        return array.ToJsonString();
    }

    private static IReadOnlyList<ToolSchema> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} not found in {assembly.FullName}.");

        using var reader = new StreamReader(resource);
        var json = reader.ReadToEnd();

        var array = JsonNode.Parse(json) as JsonArray
            ?? throw new InvalidOperationException("tools_catalog.json root is not a JSON array.");

        var tools = new List<ToolSchema>(array.Count);
        foreach (var entry in array)
        {
            if (entry is not JsonObject tool)
            {
                throw new FormatException("Tool catalog entry is not a JSON object.");
            }

            tools.Add(new ToolSchema
            {
                Name = tool["name"]?.GetValue<string>()
                    ?? throw new FormatException("Tool catalog entry missing 'name'."),
                Title = tool["title"]?.GetValue<string>()
                    ?? throw new FormatException($"Tool '{tool["name"]}' missing 'title'."),
                Description = tool["description"]?.GetValue<string>()
                    ?? throw new FormatException($"Tool '{tool["name"]}' missing 'description'."),
                InputSchema = tool["inputSchema"] as JsonObject
                    ?? throw new FormatException($"Tool '{tool["name"]}' missing 'inputSchema' object."),
                Annotations = tool["annotations"] is JsonObject ann
                    ? new ToolAnnotations(
                        ann["readOnlyHint"]?.GetValue<bool>() ?? false,
                        ann["destructiveHint"]?.GetValue<bool>() ?? false,
                        ann["idempotentHint"]?.GetValue<bool>() ?? false)
                    : new ToolAnnotations(false, false, false),
            });
        }

        return tools;
    }
}
