using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace Fusion360.Mcp.Server;

/// <summary>One prompt: name, description, and its declared (optional) arguments.</summary>
public sealed record PromptSpec(string Name, string Description, IReadOnlyList<PromptArgument> Arguments);

/// <summary>
/// The reference's three prompts and their step-by-step bodies. Faithful port
/// of <c>_PROMPTS</c> + <c>get_prompt</c> (server.py) — including the default
/// argument values and the (intentionally swapped) width/length in the box
/// rectangle line.
/// </summary>
public static class PromptTemplates
{
    /// <summary>The three prompts, in reference order.</summary>
    public static IReadOnlyList<PromptSpec> All { get; } = new List<PromptSpec>
    {
        new(
            "create-box",
            "Guide for creating a parametric box in Fusion 360",
            new PromptArgument[]
            {
                new() { Name = "length", Description = "Box length in cm", Required = false },
                new() { Name = "width", Description = "Box width in cm", Required = false },
                new() { Name = "height", Description = "Box height in cm", Required = false },
            }),
        new(
            "model-threaded-bolt",
            "Step-by-step guide for modeling a threaded bolt in Fusion 360",
            new PromptArgument[]
            {
                new() { Name = "designation", Description = "Thread designation (e.g. M10x1.5)", Required = false },
            }),
        new(
            "sheet-metal-enclosure",
            "Guide for creating a sheet metal enclosure",
            new PromptArgument[]
            {
                new() { Name = "length", Description = "Enclosure length in cm", Required = false },
                new() { Name = "width", Description = "Enclosure width in cm", Required = false },
                new() { Name = "height", Description = "Enclosure height in cm", Required = false },
            }),
    };

    /// <summary>
    /// Builds the <see cref="GetPromptResult"/> for <paramref name="name"/> from
    /// the (optional) string <paramref name="arguments"/>. Port of the
    /// reference <c>get_prompt</c>.
    /// </summary>
    public static GetPromptResult Build(string name, IDictionary<string, JsonElement> arguments)
    {
        string Arg(string key, string fallback)
            => arguments.TryGetValue(key, out var element) &&
               element.ValueKind == JsonValueKind.String &&
               element.GetString() is { } value
                ? value
                : fallback;

        var text = name switch
        {
            "create-box" =>
                "Create a parametric box in Fusion 360:\n" +
                "1. create_sketch on xy plane\n" +
                $"2. draw_rectangle width={Arg("width", "5")} height={Arg("length", "10")}\n" +
                $"3. extrude height={Arg("height", "3")}\n" +
                "4. get_scene_info to verify",
            "model-threaded-bolt" =>
                $"Model a threaded bolt ({Arg("designation", "M10x1.5")}):\n" +
                "1. create_sketch on xy plane\n" +
                "2. draw_circle for bolt shaft\n" +
                "3. extrude to bolt length\n" +
                $"4. create_thread designation={Arg("designation", "M10x1.5")}\n" +
                "5. Create hex head sketch + extrude\n" +
                "6. chamfer head edges",
            "sheet-metal-enclosure" =>
                $"Create a sheet metal enclosure ({Arg("length", "20")}x{Arg("width", "10")}x{Arg("height", "5")} cm):\n" +
                "1. create_sketch on xy plane\n" +
                $"2. draw_rectangle {Arg("width", "10")}x{Arg("length", "20")}\n" +
                "3. extrude to sheet thickness\n" +
                "4. create_flange on each edge\n" +
                "5. flat_pattern to verify unfold",
            _ => $"No template for prompt: {name}",
        };

        var description = All.FirstOrDefault(p => p.Name == name)?.Description ?? string.Empty;
        return new GetPromptResult
        {
            Description = description,
            Messages = new List<PromptMessage>
            {
                new() { Role = Role.User, Content = new TextContentBlock { Text = text } },
            },
        };
    }
}
