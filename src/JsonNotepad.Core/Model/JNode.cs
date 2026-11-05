using System.Diagnostics;

namespace JsonNotepad.Core.Model;

/// <summary>
/// A node in the editable JSON tree.
/// </summary>
/// <remarks>
/// <para>
/// The model is intentionally compact (one object per JSON value, ~56 bytes plus
/// strings) because documents with millions of values must fit in memory.
/// Children lists are allocated lazily so empty containers and primitives cost
/// nothing extra.
/// </para>
/// <para>
/// Numbers are stored as their original literal text so that values such as
/// <c>12345678901234567890</c> or <c>1.10</c> round-trip byte-for-byte; nothing
/// is ever converted through <see cref="double"/> unless a consumer asks.
/// </para>
/// <para>
/// Attached nodes are mutated only through <see cref="JDocument"/> so that every
/// change raises <see cref="JDocument.Changed"/>; the setters here are internal.
/// The model is NOT thread-safe: it is owned by the UI thread, and background
/// work operates on snapshots (<see cref="DeepClone"/>).
/// </para>
/// </remarks>
[DebuggerDisplay("{DebuggerText,nq}")]
public sealed class JNode
{
    internal const string TrueLiteral = "true";
    internal const string FalseLiteral = "false";

    private List<JNode>? _children;

    private JNode(JNodeKind kind, string? value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>The JSON type of this node. Changing type replaces the node.</summary>
    public JNodeKind Kind { get; }

    /// <summary>Property name when the parent is an object; otherwise <c>null</c>.</summary>
    public string? Name { get; internal set; }

    /// <summary>
    /// Primitive payload: unescaped text for strings, the literal for numbers,
    /// "true"/"false" for booleans, <c>null</c> for null and containers.
    /// </summary>
    public string? Value { get; internal set; }

    public JNode? Parent { get; private set; }

    public bool IsContainer => Kind.IsContainer();

    public int Count => _children?.Count ?? 0;

    public bool HasChildren => _children is { Count: > 0 };

    public IReadOnlyList<JNode> Children => (IReadOnlyList<JNode>?)_children ?? Array.Empty<JNode>();

    public JNode this[int index] => _children is null
        ? throw new ArgumentOutOfRangeException(nameof(index))
        : _children[index];

    public JNode? FirstChild => _children is { Count: > 0 } c ? c[0] : null;

    public JNode? LastChild => _children is { Count: > 0 } c ? c[^1] : null;

    /// <summary>True when this node is the last child of its parent (O(1)); used for tree lines.</summary>
    public bool IsLastChild => Parent is null || ReferenceEquals(Parent._children![^1], this);

    public bool IsRoot => Parent is null;

    public bool BooleanValue => Kind == JNodeKind.Boolean && Value == TrueLiteral;

    public JNode Root
    {
        get
        {
            var n = this;
            while (n.Parent is not null)
            {
                n = n.Parent;
            }
            return n;
        }
    }

    public int Depth
    {
        get
        {
            int d = 0;
            for (var n = Parent; n is not null; n = n.Parent)
            {
                d++;
            }
            return d;
        }
    }

    /// <summary>Index within the parent. O(number of siblings); avoid in tight loops.</summary>
    public int GetIndex() => Parent?._children!.IndexOf(this) ?? -1;

    public bool IsAncestorOf(JNode node)
    {
        for (var n = node.Parent; n is not null; n = n.Parent)
        {
            if (ReferenceEquals(n, this))
            {
                return true;
            }
        }
        return false;
    }

    public JNode? FindProperty(string name)
    {
        if (Kind != JNodeKind.Object || _children is null)
        {
            return null;
        }
        foreach (var c in _children)
        {
            if (string.Equals(c.Name, name, StringComparison.Ordinal))
            {
                return c;
            }
        }
        return null;
    }

    public bool ContainsProperty(string name, JNode? except = null)
    {
        if (_children is null)
        {
            return false;
        }
        foreach (var c in _children)
        {
            if (!ReferenceEquals(c, except) && string.Equals(c.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Iterative pre-order traversal (safe for arbitrarily deep documents).</summary>
    public IEnumerable<JNode> DescendantsAndSelf()
    {
        var stack = new Stack<(JNode Node, int Next)>();
        yield return this;
        if (HasChildren)
        {
            stack.Push((this, 0));
        }
        while (stack.Count > 0)
        {
            var (node, next) = stack.Pop();
            if (next >= node.Count)
            {
                continue;
            }
            stack.Push((node, next + 1));
            var child = node._children![next];
            yield return child;
            if (child.HasChildren)
            {
                stack.Push((child, 0));
            }
        }
    }

    /// <summary>Deep copy, preserving <see cref="Name"/>. The copy is detached. Iterative.</summary>
    public JNode DeepClone()
    {
        var rootCopy = CloneShallow(this);
        if (!HasChildren)
        {
            return rootCopy;
        }

        var stack = new Stack<(JNode Source, JNode Target)>();
        stack.Push((this, rootCopy));
        while (stack.Count > 0)
        {
            var (src, dst) = stack.Pop();
            var list = new List<JNode>(src._children!.Count);
            foreach (var child in src._children)
            {
                var copy = CloneShallow(child);
                copy.Parent = dst;
                list.Add(copy);
                if (child.HasChildren)
                {
                    stack.Push((child, copy));
                }
            }
            dst._children = list;
        }
        return rootCopy;
    }

    private static JNode CloneShallow(JNode n) => new(n.Kind, n.Value) { Name = n.Name };

    // ----- Factories -------------------------------------------------------

    public static JNode CreateObject() => new(JNodeKind.Object, null);

    public static JNode CreateArray() => new(JNodeKind.Array, null);

    public static JNode CreateString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(JNodeKind.String, value);
    }

    public static JNode CreateNumber(string literal)
    {
        if (!JsonNumber.IsValid(literal))
        {
            throw new FormatException($"'{literal}' is not a valid JSON number.");
        }
        return new(JNodeKind.Number, literal);
    }

    public static JNode CreateBoolean(bool value) => new(JNodeKind.Boolean, value ? TrueLiteral : FalseLiteral);

    public static JNode CreateNull() => new(JNodeKind.Null, null);

    /// <summary>Creates a node with the default value for <paramref name="kind"/>.</summary>
    public static JNode Create(JNodeKind kind) => kind switch
    {
        JNodeKind.Object => CreateObject(),
        JNodeKind.Array => CreateArray(),
        JNodeKind.String => CreateString(string.Empty),
        JNodeKind.Number => new JNode(JNodeKind.Number, "0"),
        JNodeKind.Boolean => CreateBoolean(false),
        _ => CreateNull(),
    };

    /// <summary>Fast path for the parser: number literal already validated by the reader.</summary>
    internal static JNode CreateNumberUnchecked(string literal) => new(JNodeKind.Number, literal);

    /// <summary>Sets the name of a node that is not yet attached to a document.</summary>
    public JNode WithName(string? name)
    {
        if (Parent is not null)
        {
            throw new InvalidOperationException("Use JDocument.SetName to rename an attached node.");
        }
        Name = name;
        return this;
    }

    // ----- Internal mutation (JDocument / parser only) --------------------

    internal void AddChildCore(JNode child)
    {
        child.Parent = this;
        (_children ??= new List<JNode>()).Add(child);
    }

    internal void InsertChildCore(int index, JNode child)
    {
        child.Parent = this;
        (_children ??= new List<JNode>()).Insert(index, child);
    }

    internal JNode RemoveChildAtCore(int index)
    {
        var child = _children![index];
        _children.RemoveAt(index);
        child.Parent = null;
        return child;
    }

    internal void ReplaceChildCore(int index, JNode child)
    {
        var old = _children![index];
        old.Parent = null;
        child.Parent = this;
        _children[index] = child;
    }

    internal void SetChildrenOrderCore(IReadOnlyList<JNode> order)
    {
        _children = new List<JNode>(order);
    }

    internal void TrimExcess()
    {
        if (_children is { } c && c.Capacity - c.Count > 16)
        {
            c.TrimExcess();
        }
    }

    private string DebuggerText => Kind switch
    {
        JNodeKind.Object => $"{Name ?? "(item)"}: {{{Count}}}",
        JNodeKind.Array => $"{Name ?? "(item)"}: [{Count}]",
        JNodeKind.Null => $"{Name ?? "(item)"}: null",
        _ => $"{Name ?? "(item)"}: {Value}",
    };

    public override string ToString() => DebuggerText;
}
