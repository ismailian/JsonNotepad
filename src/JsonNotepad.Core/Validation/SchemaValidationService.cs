using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.Validation;

/// <summary>
/// Locates, loads and caches schemas, and runs validation off the UI thread.
/// </summary>
/// <remarks>
/// Threading contract: <see cref="ValidateAsync"/> must be called on the thread that owns
/// the document (it snapshots synchronously), and then validates the snapshot on the
/// thread pool. Callers compare <see cref="ValidationResult.DocumentVersion"/> with the
/// live document to discard stale results.
/// </remarks>
public sealed class SchemaValidationService
{
    private readonly Dictionary<string, (DateTime Stamp, JsonSchemaValidator Validator)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public sealed record ValidationResult(long DocumentVersion, string? SchemaPath, IReadOnlyList<Diagnostic> Diagnostics);

    /// <summary>
    /// Determines which schema applies: an explicit association wins, otherwise the root's
    /// "$schema" member when it points to a local file (relative to the document).
    /// </summary>
    public static string? ResolveSchemaPath(JDocument document, string? documentPath, string? explicitSchemaPath, out string? note)
    {
        note = null;
        if (!string.IsNullOrEmpty(explicitSchemaPath))
        {
            return explicitSchemaPath;
        }
        if (document.Root.FindProperty("$schema") is not { Kind: JNodeKind.String } schemaRef || string.IsNullOrWhiteSpace(schemaRef.Value))
        {
            return null;
        }
        var value = schemaRef.Value.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile)
            {
                return uri.LocalPath;
            }
            note = $"Remote schema \"{value}\" is not downloaded. Use Tools ▸ Assign Schema to validate against a local copy.";
            return null;
        }
        var baseDir = documentPath is null ? Environment.CurrentDirectory : Path.GetDirectoryName(Path.GetFullPath(documentPath))!;
        return Path.GetFullPath(Path.Combine(baseDir, value));
    }

    public Task<ValidationResult> ValidateAsync(JDocument document, string schemaPath, CancellationToken cancellationToken)
    {
        long version = document.Version;
        var snapshot = document.CreateSnapshot(); // on the owning thread
        return Task.Run(() => new ValidationResult(version, schemaPath, ValidateSnapshot(snapshot, schemaPath, cancellationToken)), cancellationToken);
    }

    /// <summary>Validates an already-detached snapshot synchronously (call from a worker thread).</summary>
    public IReadOnlyList<Diagnostic> ValidateSnapshot(JNode snapshot, string schemaPath, CancellationToken cancellationToken)
    {
        try
        {
            return GetValidator(schemaPath).Validate(snapshot, cancellationToken);
        }
        catch (JsonParseException ex)
        {
            return Single(schemaPath, $"The schema is not valid JSON: {ex.Message}", ex.Line, ex.Column);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Single(schemaPath, $"Cannot load schema: {ex.Message}", null, null);
        }
    }

    public void Invalidate(string? schemaPath = null)
    {
        lock (_gate)
        {
            if (schemaPath is null)
            {
                _cache.Clear();
            }
            else
            {
                _cache.Remove(schemaPath);
            }
        }
    }

    private JsonSchemaValidator GetValidator(string schemaPath)
    {
        var stamp = File.GetLastWriteTimeUtc(schemaPath);
        lock (_gate)
        {
            if (_cache.TryGetValue(schemaPath, out var entry) && entry.Stamp == stamp)
            {
                return entry.Validator;
            }
        }
        var result = JsonLoader.LoadFile(schemaPath, ParseOptions.Default, null, CancellationToken.None);
        var validator = new JsonSchemaValidator(result.Document.Root) { SchemaFileName = schemaPath };
        lock (_gate)
        {
            _cache[schemaPath] = (stamp, validator);
        }
        return validator;
    }

    private static Diagnostic[] Single(string schemaPath, string message, long? line, long? column) =>
        new[] { new Diagnostic(DiagnosticSeverity.Error, message, null, line, column, "Schema", schemaPath) };
}
