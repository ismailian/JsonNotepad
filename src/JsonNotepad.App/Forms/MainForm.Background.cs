using JsonNotepad.App.Infrastructure;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Services;
using JsonNotepad.Core.Validation;

namespace JsonNotepad.App.Forms;

/// <summary>
/// Background work: validation, text-view formatting and statistics.
/// </summary>
/// <remarks>
/// Pattern used throughout: (1) debounce with <see cref="DelayedActions"/>, (2) cancel the
/// previous run, (3) snapshot the model on the UI thread, (4) compute on the thread pool,
/// (5) back on the UI thread, apply the result only if the document version is unchanged.
/// </remarks>
internal sealed partial class MainForm
{
    private const long MaxTextViewBytes = 400L * 1024 * 1024;

    // ----- Validation / checks ----------------------------------------------------------------

    private void ScheduleChecks(bool immediate, bool force = false)
    {
        if (!force && !_settings.ValidateOnEdit && !immediate)
        {
            return;
        }
        _delayed.Start("checks", () => _ = RunChecksAsync(force), immediate ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(700));
    }

    private async Task RunChecksAsync(bool userRequested)
    {
        _checkCts?.Cancel();
        _checkCts?.Dispose();
        var cts = _checkCts = new CancellationTokenSource();
        var document = _session.Document;
        long version = document.Version;
        var schemaPath = SchemaValidationService.ResolveSchemaPath(document, _session.FilePath, _schemaPath, out var note);
        _lastResolvedSchema = schemaPath;

        // Freshly loaded and nothing to validate against: the parser already produced the checks.
        if (!userRequested && schemaPath is null && version == 0 && note is null)
        {
            UpdateErrorList();
            return;
        }

        var snapshot = document.CreateSnapshot();
        var loadInfo = _session.LoadDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Information && version == 0).ToList();
        if (userRequested)
        {
            SetStatus(schemaPath is null ? "Checking…" : $"Validating against {Path.GetFileName(schemaPath)}…", transient: false);
        }

        try
        {
            var results = await Task.Run(() =>
            {
                var list = new List<Diagnostic>(DocumentChecker.Check(snapshot, 500, cts.Token));
                if (schemaPath is not null)
                {
                    list.AddRange(_validation.ValidateSnapshot(snapshot, schemaPath, cts.Token));
                }
                return list;
            }, cts.Token);

            if (cts.IsCancellationRequested || !ReferenceEquals(document, _session.Document) || document.Version != version)
            {
                return; // stale
            }
            if (note is not null)
            {
                results.Add(new Diagnostic(DiagnosticSeverity.Information, note, "$", Source: "Schema"));
            }
            results.InsertRange(0, loadInfo);
            _checkDiagnostics = results;
            UpdateErrorList();
            RequestActionUpdate();

            if (userRequested)
            {
                int errors = results.Count(d => d.Severity == DiagnosticSeverity.Error);
                SetStatus(schemaPath is null
                    ? (results.Count == 0 ? "The document is well-formed. No schema is assigned (Tools ▸ Assign Schema)." : $"{results.Count:N0} issue(s) found.")
                    : errors == 0 ? $"Valid against {Path.GetFileName(schemaPath)}." : $"{errors:N0} schema error(s). Press F4 to go to the next one.");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void AssignSchema()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "JSON Schema (*.json;*.schema.json)|*.json|All files (*.*)|*.*",
            Title = "Assign JSON Schema",
            InitialDirectory = _session.FilePath is null ? _settings.LastDirectory : Path.GetDirectoryName(_session.FilePath),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        _schemaPath = dlg.FileName;
        _validation.Invalidate(_schemaPath);
        ScheduleChecks(immediate: true, force: true);
        RequestActionUpdate();
    }

    private void ClearSchema()
    {
        _schemaPath = null;
        ScheduleChecks(immediate: true, force: true);
        RequestActionUpdate();
    }

    // ----- Text view ----------------------------------------------------------------------------

    private void InvalidateTextView()
    {
        if (_tabs.SelectedIndex == TextTab)
        {
            _delayed.Start("textView", () => _ = RefreshTextViewAsync(syncToSelection: false), TimeSpan.FromMilliseconds(300));
        }
    }

    private async Task RefreshTextViewAsync(bool syncToSelection)
    {
        var document = _session.Document;
        if (_formatted is not null && _formatted.SourceVersion == document.Version && ReferenceEquals(_formattedFor, document))
        {
            if (syncToSelection)
            {
                SyncTextToSelection();
            }
            return;
        }
        if (_session.FileSize > MaxTextViewBytes)
        {
            _textView.Placeholder = $"This document is too large for the Text View ({FormatSize(_session.FileSize)}). Use the Tree View instead.";
            return;
        }

        _textCts?.Cancel();
        _textCts?.Dispose();
        var cts = _textCts = new CancellationTokenSource();
        long version = document.Version;
        var snapshot = document.CreateSnapshot();
        var options = CurrentFormatOptions() with { UseTabs = false, Indented = true };
        _delayed.Start("textPlaceholder", () => _textView.Placeholder = "Formatting…", TimeSpan.FromMilliseconds(250));

        try
        {
            var text = await Task.Run(() => FormattedText.Build(snapshot, options, version, cts.Token), cts.Token);
            _delayed.Cancel("textPlaceholder");
            if (cts.IsCancellationRequested || !ReferenceEquals(document, _session.Document))
            {
                return;
            }
            _formatted = text;
            _formattedFor = document;
            _textView.Content = text;
            if (syncToSelection)
            {
                SyncTextToSelection();
            }
            if (document.Version != version)
            {
                InvalidateTextView(); // edited meanwhile; refresh again
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (OutOfMemoryException)
        {
            _delayed.Cancel("textPlaceholder");
            _textView.Placeholder = "Not enough memory to format this document as text.";
        }
    }

    private JDocument? _formattedFor;

    private void SyncTextToSelection()
    {
        if (_formatted is null || Selected is not { } node || _formatted.SourceVersion != _session.Document.Version)
        {
            return;
        }
        int index = PreorderCursor.IndexOf(_session.Document.Root, node);
        if (index >= 0 && index < _formatted.NodeLines.Length)
        {
            _textView.GoToLine(_formatted.NodeLines[index]);
        }
    }

    private void SyncTreeToTextLine(int line)
    {
        if (_formatted is null || _formatted.SourceVersion != _session.Document.Version)
        {
            return;
        }
        int index = _formatted.FindNodeIndexAtLine(line);
        if (PreorderCursor.NodeAt(_session.Document.Root, index) is { } node)
        {
            _tabs.SelectedIndex = TreeTab;
            _tree.SelectedNode = node;
            _tree.Focus();
        }
    }

    // ----- Statistics ------------------------------------------------------------------------------

    private async Task ShowStatisticsAsync()
    {
        var snapshot = _session.Document.CreateSnapshot();
        SetStatus("Computing statistics…", transient: false);
        UseWaitCursor = true;
        DocumentStatistics stats;
        try
        {
            stats = await Task.Run(() => DocumentStatistics.Compute(snapshot, CancellationToken.None));
        }
        finally
        {
            UseWaitCursor = false;
        }
        SetStatus(string.Empty);

        var rows = new List<(string, string)>
        {
            ("File", _session.FilePath ?? "(untitled)"),
            ("Size on disk", _session.IsUntitled ? "-" : FormatSize(_session.FileSize)),
            ("Encoding", _session.Format.EncodingDisplayName),
            (string.Empty, string.Empty),
            ("Total nodes", stats.TotalNodes.ToString("N0")),
            ("Objects", stats.Objects.ToString("N0")),
            ("Arrays", stats.Arrays.ToString("N0")),
            ("Strings", stats.Strings.ToString("N0")),
            ("Numbers", stats.Numbers.ToString("N0")),
            ("Booleans", stats.Booleans.ToString("N0")),
            ("Nulls", stats.Nulls.ToString("N0")),
            (string.Empty, string.Empty),
            ("Maximum depth", stats.MaxDepth.ToString("N0")),
            ("Largest array", stats.LargestArray.ToString("N0") + " items"),
            ("Largest object", stats.LargestObject.ToString("N0") + " members"),
            ("Total string length", stats.TotalStringChars.ToString("N0") + " chars"),
            ("Longest string", stats.LongestString.ToString("N0") + " chars"),
            ("Distinct keys", stats.DistinctKeys.ToString("N0")),
            (string.Empty, string.Empty),
        };
        rows.AddRange(stats.TopKeys.Select(k => ($"Key \"{k.Key}\"", k.Value.ToString("N0") + "×")));
        using var form = new ReportForm("Document Statistics", "Measure", "Value", rows);
        form.ShowDialog(this);
        Log.Info($"Statistics: {stats.TotalNodes} nodes");
    }
}
