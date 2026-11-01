using System.Text.Json;
using System.Text.Json.Nodes;
using Fusion360.Mcp.Core.Catalog;
using Fusion360.Mcp.Core.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;

namespace Fusion360.Mcp.Server;

/// <summary>
/// Builds the Fusion 360 MCP server's tool/resource/prompt surface on top of
/// the official <c>ModelContextProtocol</c> C# SDK (PLAN.md D2). The tool
/// catalog, mock layer, and TCP client all come from Core, so the wire
/// surface is identical to the reference: 92 tools (the supported catalog's exact
/// JSON-Schema inputSchemas), 3 static resources + 2 resource templates, and
/// 3 prompts.
///
/// Tool calls are routed through an <see cref="IToolBackend"/> — the mock
/// layer (<c>--mode mock</c>) or the add-in TCP connection
/// (<c>--mode socket</c>).
/// </summary>
public sealed class Fusion360McpServer
{
    private readonly IToolBackend _backend;
    private readonly ResourceReader _resourceReader;

    public Fusion360McpServer(IToolBackend backend)
    {
        _backend = backend;
        _resourceReader = new ResourceReader(backend);
    }

    /// <summary>The backend this server routes tool calls through.</summary>
    public IToolBackend Backend => _backend;

    /// <summary>
    /// Assembles the SDK <see cref="McpServerOptions"/> with every tool,
    /// resource, and prompt registered.
    /// </summary>
    public McpServerOptions CreateOptions(string serverName = "fusion360-mcp-server", string? serverVersion = null)
    {
        var resolvedVersion = serverVersion
            ?? typeof(Fusion360McpServer).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = serverName, Title = serverName, Version = resolvedVersion },
            ServerInstructions =
                "Fusion 360 design tools. After a mutation, verify with get_scene_info; " +
                "read-only queries are safe to call freely.",
        };

        AddTools(options);
        AddResources(options);
        AddPrompts(options);
        return options;
    }

    private void AddTools(McpServerOptions options)
    {
        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        options.ToolCollection = tools;
        foreach (var schema in ToolCatalog.All)
        {
            var handler = new Func<RequestContext<CallToolRequestParams>, ValueTask<CallToolResult>>(HandleToolAsync);
            var tool = McpServerTool.Create(handler, new McpServerToolCreateOptions
            {
                Name = schema.Name,
                Title = schema.Title,
                Description = schema.Description,
                ReadOnly = schema.Annotations.ReadOnlyHint,
                Destructive = schema.Annotations.DestructiveHint,
                Idempotent = schema.Annotations.IdempotentHint,
            });

            // The SDK auto-generates an input schema from the handler
            // signature (which is just a request context, so it would be
            // empty); replace it with the catalog's exact JSON-Schema so the
            // client sees the same schema as the reference (PLAN.md D6).
            tool.ProtocolTool.InputSchema = ToJsonElement(schema.InputSchema);
            tool.ProtocolTool.Annotations = new ModelContextProtocol.Protocol.ToolAnnotations
            {
                ReadOnlyHint = schema.Annotations.ReadOnlyHint,
                DestructiveHint = schema.Annotations.DestructiveHint,
                IdempotentHint = schema.Annotations.IdempotentHint,
            };

            tools.Add(tool);
        }
    }

    private void AddResources(McpServerOptions options)
    {
        var resources = new McpServerResourceCollection();
        options.ResourceCollection = resources;
        AddResource(resources, "fusion360://status", "Connection Status", "Check whether Fusion 360 is reachable");
        AddResource(resources, "fusion360://design", "Design Tree", "Full design tree: bodies, sketches, features, components");
        AddResource(resources, "fusion360://parameters", "User Parameters", "All user-defined parameters in the design");
        AddResource(resources, "fusion360://body/{name}", "Body Info", "Detailed info about a named body");
        AddResource(resources, "fusion360://component/{name}", "Component Info", "Info about a named component");
    }

    private void AddResource(McpServerResourceCollection resources, string uriTemplate, string name, string description)
    {
        var handler = new Func<RequestContext<ReadResourceRequestParams>, ValueTask<ReadResourceResult>>(HandleReadResource);
        var resource = McpServerResource.Create(handler, new McpServerResourceCreateOptions
        {
            UriTemplate = uriTemplate,
            Name = name,
            Description = description,
            MimeType = "application/json",
        });
        resources.Add(resource);
    }

    private void AddPrompts(McpServerOptions options)
    {
        var prompts = new McpServerPrimitiveCollection<McpServerPrompt>();
        options.PromptCollection = prompts;
        foreach (var spec in PromptTemplates.All)
        {
            var handler = new Func<RequestContext<GetPromptRequestParams>, ValueTask<GetPromptResult>>(HandleGetPrompt);
            var prompt = McpServerPrompt.Create(handler, new McpServerPromptCreateOptions
            {
                Name = spec.Name,
                Title = spec.Name,
                Description = spec.Description,
            });

            // The delegate signature carries no user arguments, so declare
            // the reference's prompt arguments explicitly.
            prompt.ProtocolPrompt.Arguments = new List<PromptArgument>(spec.Arguments);
            prompts.Add(prompt);
        }
    }

    // ── handlers ─────────────────────────────────────────────────────

    private async ValueTask<CallToolResult> HandleToolAsync(RequestContext<CallToolRequestParams> ctx)
    {
        var name = ctx.Params.Name;
        var arguments = new JsonObject();
        foreach (var (key, element) in ctx.Params.Arguments ?? new Dictionary<string, JsonElement>())
        {
            arguments[key] = JsonNode.Parse(element.GetRawText());
        }

        try
        {
            // The backend blocks (socket connect / 45s command timeout), so
            // keep the MCP request loop off it.
            var result = await Task.Run(() => _backend.Invoke(name, arguments));
            return ResultFormatter.FormatResult(name, result);
        }
        catch (FusionError ex)
        {
            // Structured add-in error (a timed-out mutation, or a classified
            // error frame) — the connection itself is healthy.
            return ResultFormatter.FusionErrorResult(name, ex);
        }
        catch (Exception ex)
        {
            _backend.Reset();
            return ResultFormatter.ConnectionErrorResult(name, ex);
        }
    }

    private ValueTask<ReadResourceResult> HandleReadResource(RequestContext<ReadResourceRequestParams> ctx)
    {
        var uri = ctx.Params.Uri;
        var text = _resourceReader.Read(uri);
        var result = new ReadResourceResult
        {
            Contents = new List<ResourceContents>
            {
                new TextResourceContents
                {
                    Uri = uri,
                    Text = text,
                    MimeType = "application/json",
                },
            },
        };
        return new ValueTask<ReadResourceResult>(result);
    }

    private ValueTask<GetPromptResult> HandleGetPrompt(RequestContext<GetPromptRequestParams> ctx)
    {
        var result = PromptTemplates.Build(ctx.Params.Name, ctx.Params.Arguments ?? new Dictionary<string, JsonElement>());
        return new ValueTask<GetPromptResult>(result);
    }

    private static JsonElement ToJsonElement(JsonObject node)
        => JsonSerializer.Deserialize<JsonElement>(node.ToJsonString())!;
}
