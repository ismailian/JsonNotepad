using JsonNotepad.Core.Model;
using JsonNotepad.Core.Paths;

namespace JsonNotepad.Core.Validation;

/// <summary>
/// Schema-independent well-formedness checks that can go stale as the user edits
/// (currently: duplicate member names). Re-run in the background with validation.
/// </summary>
public static class DocumentChecker
{
    public static IReadOnlyList<Diagnostic> Check(JNode root, int maxDiagnostics, CancellationToken cancellationToken)
    {
        var results = new List<Diagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long visited = 0;
        foreach (var node in root.DescendantsAndSelf())
        {
            if ((++visited & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (node.Kind != JNodeKind.Object || node.Count < 2)
            {
                continue;
            }
            seen.Clear();
            foreach (var member in node.Children)
            {
                var name = member.Name ?? string.Empty;
                if (!seen.Add(name))
                {
                    results.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Duplicate key \"{name}\". Most JSON readers keep only the last occurrence.",
                        JsonPath.Build(member),
                        Source: "JSON"));
                    if (results.Count >= maxDiagnostics)
                    {
                        return results;
                    }
                }
            }
        }
        return results;
    }
}
