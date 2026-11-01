namespace Fusion360.Mcp.AddIn;

/// <summary>
/// Scene lookups shared by the sketch handlers — ports of the reference
/// _body_by_name / _sketch_by_name / _sketch() / _construction_plane.
/// All failures are <see cref="FusionCommandException"/> with the
/// reference's message texts (ErrorClassifier maps the "not found"
/// shapes to the same codes).
/// </summary>
public static class DesignQueries
{
    /// <summary>Port of reference <c>_body_by_name</c>: root bodies first, then
    /// bodies inside components via occurrence proxies (returns the proxy body in
    /// root coordinate space, so boolean ops stay correct in assembly designs).</summary>
    public static IFusionBody BodyByName(IFusionContext ctx, string name)
    {
        var root = ctx.Design.RootComponent;
        for (var i = 0; i < root.BRepBodies.Count; i++)
        {
            var body = root.BRepBodies.Item(i);
            if (body.Name == name)
            {
                return body;
            }
        }

        for (var o = 0; o < root.AllOccurrences.Count; o++)
        {
            var occ = root.AllOccurrences.Item(o);
            for (var i = 0; i < occ.BRepBodies.Count; i++)
            {
                var proxy = occ.BRepBodies.Item(i);
                if (proxy.Name == name)
                {
                    return proxy;
                }
            }
        }

        throw new FusionCommandException(StaleOrNotFound(ctx.Design.DocumentIdentity, "body", name));
    }

    /// <summary>
    /// P3 (ADR-0011): the reference's not-found message, unless the name
    /// was previously reported to the agent by a scene read (get_scene_info
    /// / get_object_info) — then it is a <i>stale</i> reference (the object
    /// may have been deleted or renamed since the read; the ErrorClassifier
    /// maps the "stale reference" text to STALE_REFERENCE, distinct from
    /// BODY_NOT_FOUND / SKETCH_NOT_FOUND "never existed").
    /// </summary>
    private static string StaleOrNotFound(string documentIdentity, string kind, string name)
        => ModelRevision.WasReported(documentIdentity, name)
            ? $"Stale reference: {kind} '{name}' was reported by a previous scene read but no longer exists"
            : $"{char.ToUpper(kind[0])}{kind.Substring(1)} '{name}' not found";

    /// <summary>
    /// Port of reference <c>_component_by_name</c>: the root component when the
    /// name matches it, otherwise the component owned by a matching occurrence.
    /// </summary>
    public static IFusionComponent ComponentByName(IFusionContext ctx, string name)
    {
        var root = ctx.Design.RootComponent;
        if (root.Name == name)
        {
            return root;
        }

        for (var o = 0; o < root.AllOccurrences.Count; o++)
        {
            var occ = root.AllOccurrences.Item(o);
            if (occ.Component.Name == name)
            {
                return occ.Component;
            }
        }

        throw new FusionCommandException($"Component '{name}' not found");
    }

    /// <summary>Port of reference <c>_sketch_by_name</c>.</summary>
    public static IFusionSketch SketchByName(IFusionContext ctx, string name)
    {
        var sketches = ctx.Design.RootComponent.Sketches;
        for (var i = 0; i < sketches.Count; i++)
        {
            var s = sketches.Item(i);
            if (s.Name == name)
            {
                return s;
            }
        }

        throw new FusionCommandException(StaleOrNotFound(ctx.Design.DocumentIdentity, "sketch", name));
    }

    /// <summary>
    /// Port of reference <c>_sketch()</c>: the named sketch when
    /// <paramref name="name"/> is given, otherwise the most recent.
    /// </summary>
    public static IFusionSketch SketchByNameOrLast(IFusionContext ctx, string? name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            return SketchByName(ctx, name!);
        }

        return LastSketch(ctx);
    }

    /// <summary>Port of reference <c>_sketch()</c> without a name: the last sketch.</summary>
    public static IFusionSketch LastSketch(IFusionContext ctx)
    {
        var sketches = ctx.Design.RootComponent.Sketches;
        if (sketches.Count == 0)
        {
            throw new FusionCommandException("No sketch available — create one first");
        }

        return sketches.Item(sketches.Count - 1);
    }

    /// <summary>
    /// Port of reference <c>_construction_plane</c>: only xy/yz/xz are
    /// accepted, with the reference error text.
    /// </summary>
    public static IFusionConstructionPlane ConstructionPlane(IFusionRootComponent root, string plane)
    {
        return plane switch
        {
            "xy" => root.XYConstructionPlane,
            "yz" => root.YZConstructionPlane,
            "xz" => root.XZConstructionPlane,
            _ => throw new FusionCommandException($"Unknown plane '{plane}' — use xy, yz, or xz"),
        };
    }
}
