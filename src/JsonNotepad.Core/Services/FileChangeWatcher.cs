namespace JsonNotepad.Core.Services;

/// <summary>
/// Watches one file for external modification (like XML Notepad's reload prompt).
/// </summary>
/// <remarks>
/// FileSystemWatcher fires several events per save and often while the writer still has
/// the file locked, so events are debounced and then confirmed by comparing the file's
/// timestamp and size against the last state we know about (our own saves update it via
/// <see cref="AcceptCurrentState"/> so they are never reported).
/// </remarks>
public sealed class FileChangeWatcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);

    private readonly DelayedActions _delayed;
    private FileSystemWatcher? _watcher;
    private string? _path;
    private (DateTime WriteTime, long Length) _known;
    private bool _suspended;

    public FileChangeWatcher(DelayedActions delayedActions)
    {
        _delayed = delayedActions ?? throw new ArgumentNullException(nameof(delayedActions));
    }

    /// <summary>Raised on the UI context when the file was changed by someone else.</summary>
    public event EventHandler? FileChanged;

    /// <summary>Raised on the UI context when the file was deleted or renamed away.</summary>
    public event EventHandler? FileDeleted;

    public void Watch(string? path)
    {
        Stop();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (dir is null || !Directory.Exists(dir))
        {
            return;
        }
        _path = full;
        AcceptCurrentState();
        try
        {
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(full))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                IncludeSubdirectories = false,
            };
            _watcher.Changed += OnRawEvent;
            _watcher.Created += OnRawEvent;
            _watcher.Deleted += OnRawEvent;
            _watcher.Renamed += OnRawEvent;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            // Some network locations don't support change notifications; degrade gracefully.
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    /// <summary>Suppresses notifications during our own save.</summary>
    public void Suspend() => _suspended = true;

    public void Resume()
    {
        AcceptCurrentState();
        _suspended = false;
    }

    /// <summary>Records the file's current timestamp/size as "known" (no change pending).</summary>
    public void AcceptCurrentState()
    {
        _known = Probe(_path);
    }

    public void Stop()
    {
        _delayed.Cancel(nameof(FileChangeWatcher));
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        _path = null;
    }

    public void Dispose() => Stop();

    private void OnRawEvent(object sender, FileSystemEventArgs e)
    {
        // Thread-pool thread: just (re)schedule the check on the UI context.
        _delayed.Start(nameof(FileChangeWatcher), Check, Debounce);
    }

    private void Check()
    {
        if (_suspended || _path is null)
        {
            return;
        }
        if (!File.Exists(_path))
        {
            _known = default;
            FileDeleted?.Invoke(this, EventArgs.Empty);
            return;
        }
        var now = Probe(_path);
        if (now != _known)
        {
            _known = now;
            FileChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static (DateTime, long) Probe(string? path)
    {
        try
        {
            if (path is not null)
            {
                var info = new FileInfo(path);
                if (info.Exists)
                {
                    return (info.LastWriteTimeUtc, info.Length);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return default;
    }
}
