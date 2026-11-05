using JsonNotepad.Core.Editing;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Services;

namespace JsonNotepad.Core.Session;

/// <summary>
/// Everything that belongs to "the open file": the document, its undo history, its
/// path and physical format, dirty tracking and external-change detection.
/// </summary>
/// <remarks>
/// This is the UI-agnostic heart of the application (the role XmlCache plays in
/// XML Notepad). The main window binds to it; it can equally be driven from tests
/// or another front end. Members must be called on the owning (UI) thread; the
/// expensive parts (parse, serialize) are awaited on the thread pool internally.
/// </remarks>
public sealed class DocumentSession : IDisposable
{
    private readonly FileChangeWatcher _watcher;
    private bool _wasDirty;

    public DocumentSession(DelayedActions delayedActions, int maxUndoHistory = 1000)
    {
        UndoManager = new UndoManager(maxUndoHistory);
        UndoManager.StateChanged += (_, _) => RaiseDirtyIfChanged();
        _watcher = new FileChangeWatcher(delayedActions);
        _watcher.FileChanged += (_, _) => ExternalChange?.Invoke(this, new ExternalChangeEventArgs(deleted: false));
        _watcher.FileDeleted += (_, _) => ExternalChange?.Invoke(this, new ExternalChangeEventArgs(deleted: true));
        Document = JDocument.CreateEmpty();
        Editor = new NodeEditor(Document, UndoManager);
    }

    public JDocument Document { get; private set; }

    public NodeEditor Editor { get; private set; }

    public UndoManager UndoManager { get; }

    public string? FilePath { get; private set; }

    public FileFormatInfo Format { get; private set; } = FileFormatInfo.Default;

    public IReadOnlyList<Diagnostic> LoadDiagnostics { get; private set; } = Array.Empty<Diagnostic>();

    public long FileSize { get; private set; }

    public bool IsUntitled => FilePath is null;

    public bool IsDirty => !UndoManager.IsAtSavePoint;

    public bool IsBusy { get; private set; }

    public string DisplayName => FilePath is null ? "Untitled" : Path.GetFileName(FilePath);

    /// <summary>Raised after <see cref="Document"/> was replaced by New/Open/Reload.</summary>
    public event EventHandler? DocumentReplaced;

    public event EventHandler? FilePathChanged;

    public event EventHandler? DirtyChanged;

    public event EventHandler<ExternalChangeEventArgs>? ExternalChange;

    public void New(JNode? root = null)
    {
        SetDocument(new JDocument(root ?? JNode.CreateObject()), null, FileFormatInfo.Default, Array.Empty<Diagnostic>(), 0);
    }

    /// <summary>Loads a file. On failure the current document is left untouched and the exception propagates.</summary>
    public async Task<LoadResult> OpenAsync(string path, ParseOptions options, IProgress<LoadProgress>? progress, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(path);
        IsBusy = true;
        try
        {
            var result = await JsonLoader.LoadFileAsync(full, options, progress, cancellationToken);
            SetDocument(result.Document, full, result.Format, result.Diagnostics, result.FileSize);
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Saves to <paramref name="path"/> (or the current path). A snapshot is taken synchronously,
    /// so the user may continue editing while the file is written; the save point is set to the
    /// state that was actually written.
    /// </summary>
    public async Task SaveAsync(string? path, FormatOptions options, FileFormatInfo? format, CancellationToken cancellationToken)
    {
        var target = Path.GetFullPath(path ?? FilePath ?? throw new InvalidOperationException("The document has no file name."));
        var effectiveFormat = format ?? Format;
        var snapshot = Document.CreateSnapshot();
        long state = UndoManager.CurrentState;
        bool samePath = string.Equals(target, FilePath, StringComparison.OrdinalIgnoreCase);

        IsBusy = true;
        if (samePath)
        {
            _watcher.Suspend();
        }
        try
        {
            await JsonFileSaver.SaveAsync(snapshot, target, effectiveFormat, options, cancellationToken);
        }
        finally
        {
            IsBusy = false;
            if (samePath)
            {
                _watcher.Resume();
            }
        }

        Format = effectiveFormat;
        UndoManager.MarkSavePoint(state);
        FileSize = new FileInfo(target).Length;
        if (!samePath)
        {
            FilePath = target;
            _watcher.Watch(target);
            FilePathChanged?.Invoke(this, EventArgs.Empty);
        }
        RaiseDirtyIfChanged();
    }

    /// <summary>Stop reporting the current external change (user chose to keep their version).</summary>
    public void IgnoreExternalChange() => _watcher.AcceptCurrentState();

    public void Dispose() => _watcher.Dispose();

    private void SetDocument(JDocument document, string? path, FileFormatInfo format, IReadOnlyList<Diagnostic> diagnostics, long size)
    {
        bool pathChanged = !string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase);
        Document = document;
        Editor = new NodeEditor(document, UndoManager);
        Format = format;
        LoadDiagnostics = diagnostics;
        FileSize = size;
        FilePath = path;
        UndoManager.Clear();
        _watcher.Watch(path);
        DocumentReplaced?.Invoke(this, EventArgs.Empty);
        if (pathChanged)
        {
            FilePathChanged?.Invoke(this, EventArgs.Empty);
        }
        RaiseDirtyIfChanged();
    }

    private void RaiseDirtyIfChanged()
    {
        bool dirty = IsDirty;
        if (dirty != _wasDirty)
        {
            _wasDirty = dirty;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class ExternalChangeEventArgs(bool deleted) : EventArgs
{
    public bool Deleted { get; } = deleted;
}
