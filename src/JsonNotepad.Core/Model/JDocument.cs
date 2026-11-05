namespace JsonNotepad.Core.Model;

public enum JDocumentChangeKind
{
    Inserted,
    Removed,
    Renamed,
    ValueChanged,
    Replaced,
    Reordered,
}

public sealed class JDocumentChangedEventArgs : EventArgs
{
    public JDocumentChangedEventArgs(JDocumentChangeKind kind, JNode node, JNode? parent, int index, JNode? oldNode = null)
    {
        Kind = kind;
        Node = node;
        Parent = parent;
        Index = index;
        OldNode = oldNode;
    }

    public JDocumentChangeKind Kind { get; }

    /// <summary>The node that was inserted, removed, renamed, changed or (for Replaced) the new node.</summary>
    public JNode Node { get; }

    public JNode? Parent { get; }

    public int Index { get; }

    /// <summary>For <see cref="JDocumentChangeKind.Replaced"/>, the node that was replaced.</summary>
    public JNode? OldNode { get; }
}

/// <summary>
/// The aggregate root of the model. Every mutation of an attached node goes through
/// this class, which validates the operation, applies it and raises <see cref="Changed"/>
/// (Observer pattern). Views and services never mutate nodes directly; editing code goes
/// one level higher still, through undoable commands (see <c>JsonNotepad.Core.Editing</c>).
/// </summary>
public sealed class JDocument
{
    public JDocument(JNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Parent is not null)
        {
            throw new ArgumentException("The root node must be detached.", nameof(root));
        }
        root.Name = null;
        Root = root;
    }

    public JNode Root { get; private set; }

    /// <summary>Monotonic counter incremented on every change; lets background work detect staleness.</summary>
    public long Version { get; private set; }

    public event EventHandler<JDocumentChangedEventArgs>? Changed;

    public static JDocument CreateEmpty() => new(JNode.CreateObject());

    public bool Contains(JNode node) => ReferenceEquals(node.Root, Root);

    public void InsertChild(JNode parent, int index, JNode child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        EnsureAttached(parent);
        if (!parent.IsContainer)
        {
            throw new InvalidOperationException($"Cannot add children to a {parent.Kind.DisplayName()} value.");
        }
        if (child.Parent is not null || ReferenceEquals(child, Root))
        {
            throw new InvalidOperationException("The node is already attached.");
        }
        if ((uint)index > (uint)parent.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        if (parent.Kind == JNodeKind.Object)
        {
            child.Name ??= string.Empty;
        }
        else
        {
            child.Name = null;
        }
        parent.InsertChildCore(index, child);
        Raise(new JDocumentChangedEventArgs(JDocumentChangeKind.Inserted, child, parent, index));
    }

    /// <summary>Detaches <paramref name="child"/> and returns the index it occupied.</summary>
    public int RemoveChild(JNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        EnsureAttached(child);
        var parent = child.Parent ?? throw new InvalidOperationException("The root node cannot be removed.");
        int index = child.GetIndex();
        parent.RemoveChildAtCore(index);
        Raise(new JDocumentChangedEventArgs(JDocumentChangeKind.Removed, child, parent, index));
        return index;
    }

    public void SetName(JNode node, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureAttached(node);
        if (node.Parent?.Kind != JNodeKind.Object)
        {
            throw new InvalidOperationException("Only object members have names.");
        }
        if (string.Equals(node.Name, name, StringComparison.Ordinal))
        {
            return;
        }
        node.Name = name;
        Raise(new JDocumentChangedEventArgs(JDocumentChangeKind.Renamed, node, node.Parent, -1));
    }

    public void SetValue(JNode node, string? value)
    {
        EnsureAttached(node);
        switch (node.Kind)
        {
            case JNodeKind.String:
                ArgumentNullException.ThrowIfNull(value);
                break;
            case JNodeKind.Number:
                if (value is null || !JsonNumber.IsValid(value))
                {
                    throw new FormatException($"'{value}' is not a valid JSON number.");
                }
                break;
            case JNodeKind.Boolean:
                if (value is not (JNode.TrueLiteral or JNode.FalseLiteral))
                {
                    throw new FormatException("Boolean values must be 'true' or 'false'.");
                }
                value = value == JNode.TrueLiteral ? JNode.TrueLiteral : JNode.FalseLiteral;
                break;
            default:
                throw new InvalidOperationException($"{node.Kind.DisplayName()} nodes have no editable value.");
        }
        if (string.Equals(node.Value, value, StringComparison.Ordinal))
        {
            return;
        }
        node.Value = value;
        Raise(new JDocumentChangedEventArgs(JDocumentChangeKind.ValueChanged, node, node.Parent, -1));
    }

    /// <summary>Swaps <paramref name="oldNode"/> (attached) for <paramref name="newNode"/> (detached), including the root.</summary>
    public void Replace(JNode oldNode, JNode newNode)
    {
        ArgumentNullException.ThrowIfNull(newNode);
        EnsureAttached(oldNode);
        if (newNode.Parent is not null)
        {
            throw new InvalidOperationException("The replacement node is already attached.");
        }
        var parent = oldNode.Parent;
        if (parent is null)
        {
            newNode.Name = null;
            Root = newNode;
            Raise(new JDocumentChangedEventArgs(JDocumentChangeKind.Replaced, newNode, null, 0, oldNode));
            return;
        }
        newNode.Name = parent.Kind == JNodeKind.Object ? oldNode.Name ?? string.Empty : null;
        int index = oldNode.GetIndex();
        parent.ReplaceChildCore(index, newNode);
        Raise(new JDocumentChangedEventArgs(JDocumentChangeKind.Replaced, newNode, parent, index, oldNode));
    }

    /// <summary>Reorders the children of <paramref name="parent"/>; <paramref name="order"/> must be a permutation.</summary>
    public void Reorder(JNode parent, IReadOnlyList<JNode> order)
    {
        EnsureAttached(parent);
        if (order.Count != parent.Count)
        {
            throw new ArgumentException("The new order must contain every child exactly once.", nameof(order));
        }
        var set = new HashSet<JNode>(parent.Children, ReferenceEqualityComparer.Instance);
        foreach (var n in order)
        {
            if (!set.Remove(n))
            {
                throw new ArgumentException("The new order must contain every child exactly once.", nameof(order));
            }
        }
        parent.SetChildrenOrderCore(order);
        Raise(new JDocumentChangedEventArgs(JDocumentChangeKind.Reordered, parent, parent.Parent, -1));
    }

    /// <summary>Deep copy of the whole tree for consumption by background threads.</summary>
    public JNode CreateSnapshot() => Root.DeepClone();

    public long CountNodes()
    {
        long count = 0;
        foreach (var _ in Root.DescendantsAndSelf())
        {
            count++;
        }
        return count;
    }

    private void EnsureAttached(JNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!Contains(node))
        {
            throw new InvalidOperationException("The node does not belong to this document.");
        }
    }

    private void Raise(JDocumentChangedEventArgs e)
    {
        Version++;
        Changed?.Invoke(this, e);
    }
}
