namespace JsonNotepad.Core.Model;

/// <summary>
/// Walks a tree in document (pre-)order, forwards or backwards, starting anywhere.
/// </summary>
/// <remarks>
/// Keeps the (parent, index) path explicitly, so each step is O(1) amortized. A naive
/// "next sibling via IndexOf" walk is O(n²) across a large array, which is exactly the
/// shape of big JSON files (one huge array of records).
/// </remarks>
public sealed class PreorderCursor
{
    private readonly JNode _root;
    private readonly List<(JNode Parent, int Index)> _path = new();

    public PreorderCursor(JNode root, JNode? start = null)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        Current = root;
        if (start is not null && !ReferenceEquals(start, root))
        {
            MoveTo(start);
        }
    }

    public JNode Current { get; private set; }

    public void MoveTo(JNode node)
    {
        _path.Clear();
        var stack = new Stack<(JNode, int)>();
        for (var n = node; n.Parent is not null; n = n.Parent)
        {
            stack.Push((n.Parent, n.GetIndex()));
        }
        while (stack.Count > 0)
        {
            _path.Add(stack.Pop());
        }
        Current = node;
    }

    public void MoveToFirst()
    {
        _path.Clear();
        Current = _root;
    }

    public void MoveToLast()
    {
        _path.Clear();
        Current = _root;
        DescendToLast();
    }

    public bool MoveNext()
    {
        if (Current.HasChildren)
        {
            _path.Add((Current, 0));
            Current = Current[0];
            return true;
        }
        // Find the nearest ancestor level with a next sibling without mutating the path,
        // so that a failed move (end of document) leaves the cursor where it was.
        for (int depth = _path.Count - 1; depth >= 0; depth--)
        {
            var (parent, index) = _path[depth];
            if (index + 1 < parent.Count)
            {
                _path.RemoveRange(depth, _path.Count - depth);
                _path.Add((parent, index + 1));
                Current = parent[index + 1];
                return true;
            }
        }
        return false;
    }

    public bool MovePrevious()
    {
        if (_path.Count == 0)
        {
            return false;
        }
        var (parent, index) = _path[^1];
        _path.RemoveAt(_path.Count - 1);
        if (index > 0)
        {
            _path.Add((parent, index - 1));
            Current = parent[index - 1];
            DescendToLast();
        }
        else
        {
            Current = parent;
        }
        return true;
    }

    /// <summary>Pre-order index of <paramref name="node"/> within <paramref name="root"/> (O(n)).</summary>
    public static int IndexOf(JNode root, JNode node)
    {
        int i = 0;
        foreach (var n in root.DescendantsAndSelf())
        {
            if (ReferenceEquals(n, node))
            {
                return i;
            }
            i++;
        }
        return -1;
    }

    /// <summary>Node at pre-order <paramref name="index"/> (O(n)).</summary>
    public static JNode? NodeAt(JNode root, int index)
    {
        if (index < 0)
        {
            return null;
        }
        int i = 0;
        foreach (var n in root.DescendantsAndSelf())
        {
            if (i++ == index)
            {
                return n;
            }
        }
        return null;
    }

    private void DescendToLast()
    {
        while (Current.HasChildren)
        {
            int last = Current.Count - 1;
            _path.Add((Current, last));
            Current = Current[last];
        }
    }
}
