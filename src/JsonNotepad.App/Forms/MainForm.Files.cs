using JsonNotepad.App.Infrastructure;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Paths;
using JsonNotepad.Core.Session;

namespace JsonNotepad.App.Forms;

/// <summary>New / Open / Save / Reload, unsaved-change prompts and external file changes.</summary>
internal sealed partial class MainForm
{
    private async Task NewDocumentAsync()
    {
        if (!await ConfirmDiscardChangesAsync())
        {
            return;
        }
        _fileErrors = Array.Empty<Diagnostic>();
        _schemaPath = null;
        _session.New();
        SetStatus("New document");
    }

    private async Task OpenWithDialogAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = FilterJson,
            InitialDirectory = _session.FilePath is null ? _settings.LastDirectory : Path.GetDirectoryName(_session.FilePath),
            Title = "Open JSON File",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            await OpenFileAsync(dlg.FileName);
        }
    }

    private void OpenDroppedFiles(IReadOnlyList<string> files)
    {
        if (files.Count == 0)
        {
            return;
        }
        _ = OpenFileAsync(files[0]);
        // Additional files open in their own windows, like XML Notepad.
        if (Environment.ProcessPath is { } exe)
        {
            foreach (var f in files.Skip(1).Take(8))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { ArgumentList = { f }, UseShellExecute = false });
            }
        }
    }

    private Task ReloadAsync() => _session.FilePath is { } path ? OpenFileAsync(path, reload: true) : Task.CompletedTask;

    /// <summary>Opens (or reloads) a file asynchronously with progress and cancellation.</summary>
    private async Task OpenFileAsync(string path, bool reload = false)
    {
        if (_busy)
        {
            return;
        }
        if (!reload && !await ConfirmDiscardChangesAsync())
        {
            return;
        }
        if (reload && _session.IsDirty &&
            MessageBox.Show(this, $"Discard your changes to {_session.DisplayName} and reload it from disk?", AppInfo.ProductName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            MessageBox.Show(this, $"\"{path}\" is not a valid file path.", AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var keepSelection = reload && Selected is { } sel ? JsonPath.Build(sel) : null;
        var name = Path.GetFileName(fullPath);
        long size = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        SetBusy(true, $"Opening {name}…", cancellable: size > 1024 * 1024);

        var progress = new Progress<LoadProgress>(p =>
        {
            _progress.Value = (int)(p.Fraction * 1000);
            if (p.TotalBytes > 1024 * 1024)
            {
                _statusMessage.Text = $"Opening {name}… {p.BytesRead / 1048576.0:N1} of {p.TotalBytes / 1048576.0:N1} MB";
            }
        });

        try
        {
            var result = await _session.OpenAsync(fullPath, _settings.ToParseOptions(), progress, _loadCts.Token);
            if (!reload)
            {
                _schemaPath = null;
            }
            _fileErrors = Array.Empty<Diagnostic>();
            _recent.Add(fullPath);
            _settings.LastDirectory = Path.GetDirectoryName(fullPath);
            UpdateErrorList();
            if (keepSelection is not null && JsonPath.Resolve(_session.Document.Root, keepSelection) is { } again)
            {
                _tree.SelectedNode = again;
            }
            var mbps = result.Elapsed.TotalSeconds > 0 ? result.FileSize / 1048576.0 / result.Elapsed.TotalSeconds : 0;
            SetStatus($"{(reload ? "Reloaded" : "Opened")} {name}: {result.NodeCount:N0} nodes, {FormatSize(result.FileSize)} in {result.Elapsed.TotalMilliseconds:N0} ms" +
                      (result.FileSize > 4 * 1024 * 1024 ? $" ({mbps:N0} MB/s)" : string.Empty));
            Log.Info($"Opened {fullPath}: {result.NodeCount} nodes, {result.FileSize} bytes, {result.Elapsed.TotalMilliseconds:N0} ms");
        }
        catch (OperationCanceledException)
        {
            SetStatus($"Opening {name} was cancelled.");
        }
        catch (JsonParseException ex)
        {
            _fileErrors = new[] { new Diagnostic(DiagnosticSeverity.Error, ex.Message, null, ex.Line, ex.Column, "Parser", fullPath) };
            UpdateErrorList();
            if (_split.Panel2Collapsed)
            {
                ToggleErrorList();
            }
            MessageBox.Show(this, $"{name} is not valid JSON and could not be opened.\n\n{ex.Message}\n\nLine {ex.Line}, column {ex.Column}.",
                AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus($"Parse error in {name} at line {ex.Line}, column {ex.Column}.", transient: false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            _recent.Remove(fullPath);
            MessageBox.Show(this, $"The file \"{fullPath}\" was not found.", AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Could not open {name}:\n\n{ex.Message}", AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (OutOfMemoryException)
        {
            GC.Collect();
            MessageBox.Show(this, $"There is not enough memory to open {name} ({FormatSize(size)}).", AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            _tree.Focus();
        }
    }

    /// <summary>Saves the document; returns false if cancelled or failed.</summary>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_saving)
        {
            return false;
        }
        _tree.EndEdit(cancel: false);
        var path = _session.FilePath;
        if (saveAs || path is null)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = FilterJson,
                FileName = _session.IsUntitled ? "untitled.json" : Path.GetFileName(path),
                InitialDirectory = path is null ? _settings.LastDirectory : Path.GetDirectoryName(path),
                DefaultExt = "json",
                AddExtension = true,
                OverwritePrompt = true,
                Title = "Save JSON File",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                return false;
            }
            path = dlg.FileName;
        }

        _saving = true;
        RequestActionUpdate();
        var name = Path.GetFileName(path);
        SetStatus($"Saving {name}…", transient: false);
        try
        {
            await _session.SaveAsync(path, CurrentFormatOptions(), format: null, CancellationToken.None);
            _recent.Add(_session.FilePath!);
            _settings.LastDirectory = Path.GetDirectoryName(_session.FilePath);
            SetStatus($"Saved {name} ({FormatSize(_session.FileSize)})");
            UpdateFormatStatus();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Save failed: {path}", ex);
            MessageBox.Show(this, $"Could not save {name}:\n\n{ex.Message}", AppInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Save failed.");
            return false;
        }
        finally
        {
            _saving = false;
            RequestActionUpdate();
        }
    }

    /// <summary>Asks to save unsaved changes. Returns false when the user cancels.</summary>
    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!_session.IsDirty)
        {
            return true;
        }
        var answer = MessageBox.Show(this, $"Do you want to save changes to {_session.DisplayName}?", AppInfo.ProductName,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        return answer switch
        {
            DialogResult.Yes => await SaveAsync(saveAs: false),
            DialogResult.No => true,
            _ => false,
        };
    }

    private async void OnExternalChange(object? sender, ExternalChangeEventArgs e)
    {
        if (_externalPromptOpen || _busy || _saving)
        {
            return;
        }
        if (e.Deleted)
        {
            _session.UndoManager.InvalidateSavePoint();
            SetStatus($"{_session.DisplayName} was deleted or renamed outside JsonNotepad. Save to recreate it.", transient: false);
            return;
        }
        if (!_session.IsDirty && _settings.AutoReloadUnmodified)
        {
            await ReloadAsync();
            SetStatus($"{_session.DisplayName} changed on disk and was reloaded.");
            return;
        }
        _externalPromptOpen = true;
        try
        {
            var answer = MessageBox.Show(this,
                $"{_session.DisplayName} has been modified outside JsonNotepad.\n\nReload it? Your unsaved changes will be lost.",
                AppInfo.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
            {
                _session.UndoManager.MarkSavePoint(); // avoid a second "discard?" prompt
                await ReloadAsync();
            }
            else
            {
                _session.IgnoreExternalChange();
            }
        }
        finally
        {
            _externalPromptOpen = false;
        }
    }

    /// <summary>The format options for saving/copying/text view, honoring the Output Format menu.</summary>
    private FormatOptions CurrentFormatOptions()
    {
        var baseOptions = _settings.ToFormatOptions();
        return _outputStyle switch
        {
            OutputStyle.Compact => baseOptions with { Indented = false },
            OutputStyle.Indented => baseOptions,
            _ when _settings.PreserveFileFormatting && !_session.IsUntitled => _session.Format.ToFormatOptions(baseOptions),
            _ => baseOptions,
        };
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes:N0} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:N1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:N1} MB",
        _ => $"{bytes / 1073741824.0:N2} GB",
    };
}
