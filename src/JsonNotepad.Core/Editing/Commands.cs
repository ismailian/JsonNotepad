using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.Editing;

/// <summary>Inserts a detached node into a container.</summary>
public sealed class InsertNodeCommand(JDocument document, JNode parent, int index, JNode node) : EditCommand
{
    public override string Name => "Insert";

    public JNode Node => node;

    public override JNode? SelectionAfterDo => node;

    public override JNode? SelectionAfterUndo => index > 0 ? parent[index - 1] : parent;

    public override void Do() => document.InsertChild(parent, index, node);

    public override void Undo() => document.RemoveChild(node);
}

/// <summary>Removes a node; undo puts it back at the same index.</summary>
public sealed class DeleteNodeCommand(JDocument document, JNode node) : EditCommand
{
    private JNode? _parent;
    private int _index;

    public override string Name => "Delete";

    public override JNode? SelectionAfterDo
    {
        get
        {
            if (_parent is null)
            {
                return null;
            }
            if (_index < _parent.Count)
            {
                return _parent[_index];
            }
            return _index > 0 ? _parent[_index - 1] : _parent;
        }
    }

    public override JNode? SelectionAfterUndo => node;

    public override void Do()
    {
        _parent = node.Parent ?? throw new InvalidOperationException("The root cannot be deleted.");
        _index = document.RemoveChild(node);
    }

    public override void Undo() => document.InsertChild(_parent!, _index, node);
}

/// <summary>Renames an object member.</summary>
public sealed class RenameNodeCommand(JDocument document, JNode node, string newName) : EditCommand
{
    private readonly string _oldName = node.Name ?? string.Empty;

    public override string Name => "Rename";

    public override bool IsNoop => string.Equals(_oldName, newName, StringComparison.Ordinal);

    public override JNode? SelectionAfterDo => node;

    public override JNode? SelectionAfterUndo => node;

    public override void Do() => document.SetName(node, newName);

    public override void Undo() => document.SetName(node, _oldName);
}

/// <summary>Changes a primitive value (string text, number literal or boolean).</summary>
public sealed class EditValueCommand(JDocument document, JNode node, string newValue) : EditCommand
{
    private readonly string? _oldValue = node.Value;

    public override string Name => "Edit Value";

    public override bool IsNoop => string.Equals(_oldValue, newValue, StringComparison.Ordinal);

    public override JNode? SelectionAfterDo => node;

    public override JNode? SelectionAfterUndo => node;

    public override void Do() => document.SetValue(node, newValue);

    public override void Undo() => document.SetValue(node, _oldValue);
}

/// <summary>Swaps one node for another (type changes, pasting over, raw JSON edits).</summary>
public sealed class ReplaceNodeCommand(JDocument document, JNode oldNode, JNode newNode, string name = "Change Type") : EditCommand
{
    public override string Name => name;

    public override JNode? SelectionAfterDo => newNode;

    public override JNode? SelectionAfterUndo => oldNode;

    public override void Do() => document.Replace(oldNode, newNode);

    public override void Undo() => document.Replace(newNode, oldNode);
}

/// <summary>
/// Moves a node to another place in the tree (nudge, drag/drop). Handles the name
/// bookkeeping when crossing between objects (named members) and arrays (unnamed items).
/// </summary>
public sealed class MoveNodeCommand : EditCommand
{
    private readonly JDocument _document;
    private readonly JNode _node;
    private readonly JNode _newParent;
    private readonly int _newIndex;
    private readonly string? _newName;
    private readonly string _name;
    private JNode? _oldParent;
    private int _oldIndex;
    private string? _oldName;

    /// <param name="newIndex">Index in <paramref name="newParent"/> after the node has been removed from its old place.</param>
    public MoveNodeCommand(JDocument document, JNode node, JNode newParent, int newIndex, string? newName, string name = "Move")
    {
        _document = document;
        _node = node;
        _newParent = newParent;
        _newIndex = newIndex;
        _newName = newName;
        _name = name;
        if (ReferenceEquals(node, newParent) || node.IsAncestorOf(newParent))
        {
            throw new InvalidOperationException("A node cannot be moved into itself.");
        }
    }

    public override string Name => _name;

    public override JNode? SelectionAfterDo => _node;

    public override JNode? SelectionAfterUndo => _node;

    public override void Do()
    {
        _oldParent = _node.Parent ?? throw new InvalidOperationException("The root cannot be moved.");
        _oldName = _node.Name;
        _oldIndex = _document.RemoveChild(_node);
        _node.Name = _newParent.Kind == JNodeKind.Object ? _newName ?? _oldName ?? "item" : null;
        try
        {
            _document.InsertChild(_newParent, _newIndex, _node);
        }
        catch
        {
            _node.Name = _oldName;
            _document.InsertChild(_oldParent, _oldIndex, _node);
            throw;
        }
    }

    public override void Undo()
    {
        _document.RemoveChild(_node);
        _node.Name = _oldName;
        _document.InsertChild(_oldParent!, _oldIndex, _node);
    }
}

/// <summary>Reorders the members of a container (e.g. Sort Properties).</summary>
public sealed class ReorderChildrenCommand(JDocument document, JNode parent, IReadOnlyList<JNode> newOrder, string name = "Sort") : EditCommand
{
    private readonly JNode[] _oldOrder = parent.Children.ToArray();

    public override string Name => name;

    public override bool IsNoop
    {
        get
        {
            for (int i = 0; i < _oldOrder.Length; i++)
            {
                if (!ReferenceEquals(_oldOrder[i], newOrder[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }

    public override JNode? SelectionAfterDo => parent;

    public override JNode? SelectionAfterUndo => parent;

    public override void Do() => document.Reorder(parent, newOrder);

    public override void Undo() => document.Reorder(parent, _oldOrder);
}
