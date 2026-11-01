using System.Collections.Generic;

namespace Fusion360.Mcp.AddIn;

/// <summary>
/// Lightweight document revision tracking (ADR-0011, P3): a per-document
/// revision counter keyed by Fusion's document creation ID plus the registry
/// of entity names the add-in has
/// <i>reported to the agent</i> (get_scene_info / get_object_info).
///
/// The counter starts at 0 for every document and increments once per
/// successful mutation executed through <see cref="CommandHandler" />
/// (including <c>undo</c> / regen); it is process-local, so an add-in
/// restart resets it — the KOMPAS-lite variant from ADR-0011 (we address
/// entities by name, not COM objects, so no reference re-registration).
///
/// The reported-name registry is what lets a failed lookup distinguish
/// "never existed" (<c>BODY_NOT_FOUND</c> / <c>SKETCH_NOT_FOUND</c>) from
/// "existed at the caller's last read, now gone" (<c>STALE_REFERENCE</c>):
/// DesignQueries throws the stale-reference message when the missing name
/// was previously reported by a scene read.
/// </summary>
public static class ModelRevision
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, long> Revisions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<string>> ReportedNames = new(StringComparer.Ordinal);

    /// <summary>The current revision for the document identity (0 when unseen).</summary>
    public static long Current(string documentIdentity)
    {
        lock (Gate)
        {
            return Revisions.TryGetValue(documentIdentity, out var revision) ? revision : 0;
        }
    }

    /// <summary>Records one applied mutation and returns the new revision.</summary>
    public static long Increment(string documentIdentity)
    {
        lock (Gate)
        {
            var next = Current(documentIdentity) + 1;
            Revisions[documentIdentity] = next;
            return next;
        }
    }

    /// <summary>Registers entity names a scene read reported to the agent.</summary>
    public static void RecordReportedNames(string documentIdentity, IEnumerable<string> names)
    {
        lock (Gate)
        {
            if (!ReportedNames.TryGetValue(documentIdentity, out var documentNames))
            {
                documentNames = new HashSet<string>(StringComparer.Ordinal);
                ReportedNames[documentIdentity] = documentNames;
            }

            foreach (var name in names)
            {
                if (name.Length > 0)
                {
                    documentNames.Add(name);
                }
            }
        }
    }

    /// <summary>True when the name was reported by a previous scene read.</summary>
    public static bool WasReported(string documentIdentity, string name)
    {
        lock (Gate)
        {
            return ReportedNames.TryGetValue(documentIdentity, out var documentNames)
                && documentNames.Contains(name);
        }
    }

    /// <summary>Clears all revision + name-registry state (tests / add-in restart).</summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            Revisions.Clear();
            ReportedNames.Clear();
        }
    }
}
