using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fusion360.Mcp.Server;

/// <summary>
/// Resolves the reference's three static resources and two resource templates
/// to JSON text, porting <c>read_resource</c> (server.py):
/// <list type="bullet">
///   <item><c>fusion360://status</c> → ping → <c>{"connected":true,"ping":...}</c></item>
///   <item><c>fusion360://design</c> → get_scene_info</item>
///   <item><c>fusion360://parameters</c> → get_parameters</item>
///   <item><c>fusion360://body/{name}</c> / <c>fusion360://component/{name}</c> → get_object_info</item>
/// </list>
/// On any error the connection is reset and an error object is returned
/// (<c>connected:false</c> for status, <c>error</c> otherwise) — matching the
/// reference.
/// </summary>
public sealed class ResourceReader
{
    private const string StatusUri = "fusion360://status";
    private const string DesignUri = "fusion360://design";
    private const string ParametersUri = "fusion360://parameters";
    private const string BodyPrefix = "fusion360://body/";
    private const string ComponentPrefix = "fusion360://component/";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly IToolBackend _backend;

    public ResourceReader(IToolBackend backend) => _backend = backend;

    /// <summary>Returns the JSON text for <paramref name="uri"/>, or an error object on failure.</summary>
    public string Read(string uri)
    {
        try
        {
            return ReadContent(uri);
        }
        catch (Exception ex)
        {
            _backend.Reset();
            var error = new JsonObject { ["error"] = ex.Message };
            if (uri == StatusUri)
            {
                error["connected"] = false;
            }

            return error.ToJsonString(Indented);
        }
    }

    private string ReadContent(string uri)
    {
        switch (uri)
        {
            case StatusUri:
            {
                var ping = _backend.Invoke("ping", new JsonObject());
                return new JsonObject { ["connected"] = true, ["ping"] = ping }.ToJsonString(Indented);
            }

            case DesignUri:
                return _backend.Invoke("get_scene_info", new JsonObject()).ToJsonString(Indented);

            case ParametersUri:
                return _backend.Invoke("get_parameters", new JsonObject()).ToJsonString(Indented);

            default:
            {
                var name = ExtractName(uri);
                if (name is null)
                {
                    throw new ArgumentException($"Unknown resource: {uri}");
                }

                return _backend.Invoke("get_object_info", new JsonObject { ["name"] = name }).ToJsonString(Indented);
            }
        }
    }

    private static string? ExtractName(string uri)
    {
        if (uri.StartsWith(BodyPrefix, StringComparison.Ordinal))
        {
            var name = uri[BodyPrefix.Length..];
            return name.Length > 0 ? name : null;
        }

        if (uri.StartsWith(ComponentPrefix, StringComparison.Ordinal))
        {
            var name = uri[ComponentPrefix.Length..];
            return name.Length > 0 ? name : null;
        }

        return null;
    }
}
