namespace JsonNotepad.Core.Services;

/// <summary>Most-recently-used file list (most recent first, case-insensitive paths).</summary>
public sealed class RecentFiles
{
    private readonly List<string> _items = new();

    public RecentFiles(int capacity = 15, IEnumerable<string>? initial = null)
    {
        Capacity = Math.Max(1, capacity);
        if (initial is not null)
        {
            foreach (var p in initial.Reverse())
            {
                AddCore(p);
            }
        }
    }

    public int Capacity { get; private set; }

    public IReadOnlyList<string> Items => _items;

    public event EventHandler? Changed;

    public void Add(string path)
    {
        AddCore(path);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string path)
    {
        if (_items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCapacity(int capacity)
    {
        Capacity = Math.Max(1, capacity);
        if (_items.Count > Capacity)
        {
            _items.RemoveRange(Capacity, _items.Count - Capacity);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AddCore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        _items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, path);
        if (_items.Count > Capacity)
        {
            _items.RemoveRange(Capacity, _items.Count - Capacity);
        }
    }
}
