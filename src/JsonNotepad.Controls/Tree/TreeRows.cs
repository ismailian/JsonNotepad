using JsonNotepad.Core.Model;

namespace JsonNotepad.Controls.Tree;

/// <summary>One visible line of the tree.</summary>
/// <param name="Node">The model node displayed on this row.</param>
/// <param name="Depth">Nesting level (root = 0).</param>
/// <param name="Index">Index within the parent (displayed as [n] for array items).</param>
internal readonly record struct TreeRow(JNode Node, int Depth, int Index);

/// <summary>
/// The flattened list of currently visible rows (expanded nodes only).
/// </summary>
/// <remarks>
/// This is what makes the tree virtual: we never create per-node UI objects (XML Notepad
/// creates lazy TreeNode wrappers; we go one step further and keep only expansion state
/// plus a 16-byte struct per *visible* row). Painting, hit testing and scrolling are
/// O(1) row lookups; expand/collapse splice the list in place; structural edits
/// trigger a lazy rebuild that costs O(visible rows), not O(document).
/// </remarks>
internal sealed class TreeRows
{
    private readonly List<TreeRow> _rows = new();

    public HashSet<JNode> Expanded { get; } = new(ReferenceEqualityComparer.Instance);

    public int Count => _rows.Count;

    public TreeRow this[int index] => _rows[index];

    public bool IsExpanded(JNode node) => Expanded.Contains(node);

    public void Clear()
    {
        _rows.Clear();
        Expanded.Clear();
    }

    public void Rebuild(JNode? root)
    {
        _rows.Clear();
        if (root is null)
        {
            return;
        }
        _rows.Add(new TreeRow(root, 0, 0));
        AppendExpandedChildren(_rows, root, 0);
    }

    /// <summary>Finds the row of <paramref name="node"/>, checking <paramref name="hint"/> first.</summary>
    public int IndexOf(JNode node, int hint = -1)
    {
        if ((uint)hint < (uint)_rows.Count && ReferenceEquals(_rows[hint].Node, node))
        {
            return hint;
        }
        // Search outward from the hint: edits usually move a row by a few positions.
        if ((uint)hint < (uint)_rows.Count)
        {
            for (int delta = 1; delta < 64; delta++)
            {
                int a = hint - delta, b = hint + delta;
                if (a >= 0 && ReferenceEquals(_rows[a].Node, node))
                {
                    return a;
                }
                if (b < _rows.Count && ReferenceEquals(_rows[b].Node, node))
                {
                    return b;
                }
            }
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            if (ReferenceEquals(_rows[i].Node, node))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Expands the node at <paramref name="row"/>; returns the number of rows inserted.</summary>
    public int Expand(int row)
    {
        var r = _rows[row];
        if (!r.Node.HasChildren || !Expanded.Add(r.Node))
        {
            return 0;
        }
        var inserted = new List<TreeRow>();
        AppendExpandedChildren(inserted, r.Node, r.Depth);
        _rows.InsertRange(row + 1, inserted);
        return inserted.Count;
    }

    /// <summary>Collapses the node at <paramref name="row"/>; returns the number of rows removed.</summary>
    public int Collapse(int row)
    {
        var r = _rows[row];
        if (!Expanded.Remove(r.Node))
        {
            return 0;
        }
        int end = row + 1;
        while (end < _rows.Count && _rows[end].Depth > r.Depth)
        {
            end++;
        }
        int count = end - row - 1;
        if (count > 0)
        {
            _rows.RemoveRange(row + 1, count);
        }
        return count;
    }

    /// <summary>Last row belonging to the subtree that starts at <paramref name="row"/>.</summary>
    public int SubtreeEnd(int row)
    {
        int depth = _rows[row].Depth;
        int end = row + 1;
        while (end < _rows.Count && _rows[end].Depth > depth)
        {
            end++;
        }
        return end - 1;
    }

    /// <summary>Removes expansion entries for nodes that no longer belong to <paramref name="root"/>.</summary>
    public void Prune(JNode root)
    {
        Expanded.RemoveWhere(n => !ReferenceEquals(n.Root, root));
    }

    private void AppendExpandedChildren(List<TreeRow> target, JNode parent, int parentDepth)
    {
        if (!parent.HasChildren || !Expanded.Contains(parent))
        {
            return;
        }
        var stack = new Stack<(JNode Parent, int Next, int Depth)>();
        stack.Push((parent, 0, parentDepth + 1));
        while (stack.Count > 0)
        {
            var (p, next, depth) = stack.Pop();
            if (next >= p.Count)
            {
                continue;
            }
            stack.Push((p, next + 1, depth));
            var child = p[next];
            target.Add(new TreeRow(child, depth, next));
            if (child.HasChildren && Expanded.Contains(child))
            {
                stack.Push((child, 0, depth + 1));
            }
        }
    }
}
