namespace JsonNotepad.Core.Model;

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Information,
}

/// <summary>
/// An entry for the Error List: parse errors, load warnings and schema violations.
/// Locations are expressed either as a 1-based line/column (parse errors, which
/// have no node) or as a JSONPath (everything else, which survives edits better
/// than line numbers).
/// </summary>
public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Message,
    string? Path = null,
    long? Line = null,
    long? Column = null,
    string Source = "JSON",
    string? FileName = null);
