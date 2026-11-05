namespace JsonNotepad.Core.Services;

/// <summary>
/// Named, restartable, debounced actions — a port of XML Notepad's DelayedActions idea.
/// Starting an action that is already pending restarts its timer, so a burst of edits
/// triggers one validation, one settings save or one reload instead of hundreds.
/// </summary>
/// <remarks>
/// Actions run on the <see cref="SynchronizationContext"/> captured at construction (the UI
/// thread for WinForms), so callers can touch UI and model state safely.
/// </remarks>
public sealed class DelayedActions : IDisposable
{
    private readonly SynchronizationContext _context;
    private readonly Dictionary<string, Entry> _pending = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _disposed;

    public DelayedActions(SynchronizationContext? context = null)
    {
        _context = context ?? SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public void Start(string name, Action action, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            if (_pending.TryGetValue(name, out var existing))
            {
                existing.Timer.Dispose();
            }
            var entry = new Entry(action);
            entry.Timer = new Timer(_ => Fire(name, entry), null, delay, Timeout.InfiniteTimeSpan);
            _pending[name] = entry;
        }
    }

    public bool Cancel(string name)
    {
        lock (_gate)
        {
            if (_pending.Remove(name, out var entry))
            {
                entry.Timer.Dispose();
                return true;
            }
            return false;
        }
    }

    public bool IsPending(string name)
    {
        lock (_gate)
        {
            return _pending.ContainsKey(name);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var e in _pending.Values)
            {
                e.Timer.Dispose();
            }
            _pending.Clear();
        }
    }

    private void Fire(string name, Entry entry)
    {
        _context.Post(_ =>
        {
            lock (_gate)
            {
                // Only run if this exact entry is still the current one (not restarted/cancelled).
                if (_disposed || !_pending.TryGetValue(name, out var current) || !ReferenceEquals(current, entry))
                {
                    return;
                }
                _pending.Remove(name);
                entry.Timer.Dispose();
            }
            entry.Action();
        }, null);
    }

    private sealed class Entry(Action action)
    {
        public Action Action { get; } = action;

        public Timer Timer { get; set; } = null!;
    }
}
