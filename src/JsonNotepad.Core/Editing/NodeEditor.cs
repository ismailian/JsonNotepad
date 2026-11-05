using System.Globalization;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.Editing;

public enum InsertPosition
{
    Before,
    After,
    Child,
}

public enum NudgeDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>Outcome of an edit attempted from user input.</summary>
public readonly record struct EditResult(bool Success, string? Error, JNode? Node)
{
    public static EditResult Ok(JNode? node) => new(true, null, node);

    public static EditResult Fail(string error) => new(false, error, null);
}

/// <summary>
/// High-level editing operations (Facade over <see cref="JDocument"/> + <see cref="UndoManager"/>).
/// Every public mutating method validates user intent, builds the right command(s) and
/// executes them through the undo manager, so the UI never deals with commands directly
/// and every edit is undoable.
/// </summary>
public sealed class NodeEditor
{
    public const string DefaultPropertyName = "newProperty";

    public NodeEditor(JDocument document, UndoManager undoManager)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        UndoManager = undoManager ?? throw new ArgumentNullException(nameof(undoManager));
    }

    public JDocument Document { get; }

    public UndoManager UndoManager { get; }

    // ----- Insert ----------------------------------------------------------

    public static bool CanInsert(JNode? target, InsertPosition position) => target is not null && position switch
    {
        InsertPosition.Child => target.IsContainer,
        _ => target.Parent is not null,
    };

    public JNode Insert(JNode target, InsertPosition position, JNodeKind kind) =>
        InsertNode(target, position, JNode.Create(kind), "Insert " + kind.DisplayName());

    /// <summary>Inserts a detached node; generates a unique name when the destination is an object.</summary>
    public JNode InsertNode(JNode target, InsertPosition position, JNode node, string commandName = "Insert")
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(node);
        if (!CanInsert(target, position))
        {
            throw new InvalidOperationException(position == InsertPosition.Child
                ? "Only objects and arrays can have children."
                : "Siblings cannot be added to the root value.");
        }

        var (parent, index) = position switch
        {
            InsertPosition.Child => (target, target.Count),
            InsertPosition.Before => (target.Parent!, target.GetIndex()),
            _ => (target.Parent!, target.GetIndex() + 1),
        };

        if (parent.Kind == JNodeKind.Object)
        {
            node.Name = NameGenerator.MakeUnique(parent, string.IsNullOrEmpty(node.Name) ? DefaultPropertyName : node.Name);
        }

        var command = new InsertNodeCommand(Document, parent, index, node) { };
        UndoManager.Execute(new NamedCommand(command, commandName));
        return node;
    }

    // ----- Delete / duplicate ---------------------------------------------

    public static bool CanDelete(JNode? node) => node?.Parent is not null;

    public JNode? Delete(JNode node)
    {
        if (!CanDelete(node))
        {
            throw new InvalidOperationException("The root value cannot be deleted.");
        }
        var command = new DeleteNodeCommand(Document, node);
        UndoManager.Execute(command);
        return command.SelectionAfterDo;
    }

    public JNode Duplicate(JNode node)
    {
        if (!CanDelete(node))
        {
            throw new InvalidOperationException("The root value cannot be duplicated.");
        }
        var copy = node.DeepClone();
        copy.Name = node.Name;
        return InsertNode(node, InsertPosition.After, copy, "Duplicate");
    }

    // ----- Rename / set value ---------------------------------------------

    public static bool CanRename(JNode? node) => node?.Parent?.Kind == JNodeKind.Object;

    public static bool CanEditValue(JNode? node) => node is not null && node.Kind is JNodeKind.String or JNodeKind.Number or JNodeKind.Boolean;

    public EditResult Rename(JNode node, string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        if (!CanRename(node))
        {
            return EditResult.Fail("Only object members have names.");
        }
        if (node.Parent!.ContainsProperty(newName, except: node))
        {
            return EditResult.Fail($"The object already has a member named \"{newName}\".");
        }
        UndoManager.Execute(new RenameNodeCommand(Document, node, newName));
        return EditResult.Ok(node);
    }

    /// <summary>Validates <paramref name="text"/> against the node's type and applies it.</summary>
    public EditResult SetValue(JNode node, string text)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(text);
        switch (node.Kind)
        {
            case JNodeKind.String:
                UndoManager.Execute(new EditValueCommand(Document, node, text));
                return EditResult.Ok(node);
            case JNodeKind.Number:
                if (!JsonNumber.TryNormalize(text, out var literal))
                {
                    return EditResult.Fail($"\"{text}\" is not a valid number. Use Change To ▸ String to store text.");
                }
                UndoManager.Execute(new EditValueCommand(Document, node, literal));
                return EditResult.Ok(node);
            case JNodeKind.Boolean:
                var t = text.Trim();
                if (!t.Equals("true", StringComparison.OrdinalIgnoreCase) && !t.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    return EditResult.Fail("A boolean must be true or false.");
                }
                UndoManager.Execute(new EditValueCommand(Document, node, t.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false"));
                return EditResult.Ok(node);
            case JNodeKind.Null:
                return EditResult.Fail("Null has no value to edit. Use Change To to pick another type.");
            default:
                return EditResult.Fail("Objects and arrays are edited through their children.");
        }
    }

    /// <summary>Toggles a boolean in place (Space in the tree).</summary>
    public void ToggleBoolean(JNode node)
    {
        if (node.Kind == JNodeKind.Boolean)
        {
            UndoManager.Execute(new EditValueCommand(Document, node, node.BooleanValue ? "false" : "true"));
        }
    }

    // ----- Change type -----------------------------------------------------

    public JNode ChangeKind(JNode node, JNodeKind kind)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Kind == kind)
        {
            return node;
        }
        var replacement = KindConverter.Convert(node, kind);
        UndoManager.Execute(new ReplaceNodeCommand(Document, node, replacement, "Change to " + kind.DisplayName()));
        return replacement;
    }

    /// <summary>Replaces a node with parsed JSON text (keeps the member name).</summary>
    public EditResult ReplaceWithJson(JNode node, string json, ParseOptions options)
    {
        if (!JsonParser.TryParse(json, options, out var parsed, out var error))
        {
            return EditResult.Fail(error!);
        }
        UndoManager.Execute(new ReplaceNodeCommand(Document, node, parsed!, "Edit JSON"));
        return EditResult.Ok(parsed);
    }

    // ----- Nudge -----------------------------------------------------------

    public static bool CanNudge(JNode? node, NudgeDirection direction)
    {
        if (node?.Parent is null)
        {
            return false;
        }
        var parent = node.Parent;
        switch (direction)
        {
            case NudgeDirection.Up:
                return !ReferenceEquals(parent.FirstChild, node);
            case NudgeDirection.Down:
                return !ReferenceEquals(parent.LastChild, node);
            case NudgeDirection.Left:
                return parent.Parent is not null;
            default:
                int index = node.GetIndex();
                return index > 0 && parent[index - 1].IsContainer;
        }
    }

    /// <summary>
    /// Moves a node like XML Notepad's nudge commands: up/down among siblings,
    /// left = out of its container (after it), right = into the previous sibling container.
    /// </summary>
    public void Nudge(JNode node, NudgeDirection direction)
    {
        if (!CanNudge(node, direction))
        {
            return;
        }
        var parent = node.Parent!;
        int index = node.GetIndex();
        MoveNodeCommand command;
        switch (direction)
        {
            case NudgeDirection.Up:
                command = new MoveNodeCommand(Document, node, parent, index - 1, node.Name, "Nudge Up");
                break;
            case NudgeDirection.Down:
                command = new MoveNodeCommand(Document, node, parent, index + 1, node.Name, "Nudge Down");
                break;
            case NudgeDirection.Left:
                var grand = parent.Parent!;
                int parentIndex = parent.GetIndex();
                command = new MoveNodeCommand(Document, node, grand, parentIndex + 1, NameFor(node, grand), "Nudge Left");
                break;
            default:
                var target = parent[index - 1];
                command = new MoveNodeCommand(Document, node, target, target.Count, NameFor(node, target), "Nudge Right");
                break;
        }
        UndoManager.Execute(command);
    }

    /// <summary>Moves <paramref name="node"/> relative to <paramref name="target"/> (drag and drop).</summary>
    public bool Move(JNode node, JNode target, InsertPosition position)
    {
        if (node.Parent is null || ReferenceEquals(node, target) || node.IsAncestorOf(target) || !CanInsert(target, position))
        {
            return false;
        }
        var newParent = position == InsertPosition.Child ? target : target.Parent!;
        int index;
        if (position == InsertPosition.Child)
        {
            index = ReferenceEquals(node.Parent, newParent) ? newParent.Count - 1 : newParent.Count;
        }
        else
        {
            index = target.GetIndex() + (position == InsertPosition.After ? 1 : 0);
            if (ReferenceEquals(node.Parent, newParent) && node.GetIndex() < index)
            {
                index--; // account for the removal of the node itself
            }
        }
        var name = ReferenceEquals(node.Parent, newParent) ? node.Name : NameFor(node, newParent);
        UndoManager.Execute(new MoveNodeCommand(Document, node, newParent, index, name));
        return true;
    }

    // ----- Sort --------------------------------------------------------------

    public void SortProperties(JNode node, bool recursive)
    {
        using (UndoManager.BeginCompound(recursive ? "Sort Properties (Recursive)" : "Sort Properties"))
        {
            var targets = recursive ? node.DescendantsAndSelf().Where(n => n.Kind == JNodeKind.Object && n.Count > 1).ToList()
                                    : (node.Kind == JNodeKind.Object && node.Count > 1 ? new List<JNode> { node } : new List<JNode>());
            foreach (var obj in targets)
            {
                var order = obj.Children
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(c => c.Name, StringComparer.Ordinal)
                    .ToList();
                UndoManager.Execute(new ReorderChildrenCommand(Document, obj, order, "Sort Properties"));
            }
        }
    }

    // ----- Clipboard --------------------------------------------------------

    /// <summary>Serializes a node for the clipboard (value only; the name travels separately).</summary>
    public static string ToClipboardText(JNode node, FormatOptions options) => JsonDocumentWriter.ToJson(node, options with { TrailingNewLine = false });

    /// <summary>
    /// Parses clipboard text. Accepts any JSON value, or a member fragment such as
    /// <c>"key": value</c> / several members (which become an object's members).
    /// </summary>
    public static IReadOnlyList<JNode>? ParseClipboardText(string text, ParseOptions options, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The clipboard is empty.";
            return null;
        }
        if (JsonParser.TryParse(text, options, out var value, out error))
        {
            return new[] { value! };
        }
        // Try as object members: "a": 1, "b": 2
        var trimmed = text.Trim().TrimEnd(',');
        if (trimmed.StartsWith('"') && JsonParser.TryParse("{" + trimmed + "}", options, out var obj, out _))
        {
            error = null; // the first attempt's error does not apply to the fragment parse that succeeded
            var members = obj!.Children.ToList();
            foreach (var m in members)
            {
                obj.RemoveChildAtCore(0);
            }
            return members;
        }
        return null;
    }

    /// <summary>Pastes nodes as one undo step; returns the last inserted node.</summary>
    public JNode? Paste(JNode target, InsertPosition position, IReadOnlyList<JNode> nodes, string? clipboardName)
    {
        if (nodes.Count == 0 || !CanInsert(target, position))
        {
            return null;
        }
        JNode? last = null;
        using (UndoManager.BeginCompound("Paste"))
        {
            var anchor = target;
            var pos = position;
            foreach (var node in nodes)
            {
                if (nodes.Count == 1 && clipboardName is not null && string.IsNullOrEmpty(node.Name))
                {
                    node.Name = clipboardName;
                }
                last = InsertNode(anchor, pos, node, "Paste");
                if (pos != InsertPosition.Child)
                {
                    anchor = last;
                    pos = InsertPosition.After;
                }
            }
        }
        return last;
    }

    private static string? NameFor(JNode node, JNode destination)
    {
        if (destination.Kind != JNodeKind.Object)
        {
            return null;
        }
        var baseName = string.IsNullOrEmpty(node.Name)
            ? (node.Parent?.Kind == JNodeKind.Array ? "item" + node.GetIndex().ToString(CultureInfo.InvariantCulture) : "item")
            : node.Name;
        return NameGenerator.MakeUnique(destination, baseName, except: node);
    }

    /// <summary>Wraps a command to give it a context-specific display name.</summary>
    private sealed class NamedCommand(EditCommand inner, string name) : EditCommand
    {
        public override string Name => name;

        public override bool IsNoop => inner.IsNoop;

        public override JNode? SelectionAfterDo => inner.SelectionAfterDo;

        public override JNode? SelectionAfterUndo => inner.SelectionAfterUndo;

        public override void Do() => inner.Do();

        public override void Undo() => inner.Undo();

        public override void Redo() => inner.Redo();
    }
}

/// <summary>Generates member names that are unique within an object.</summary>
public static class NameGenerator
{
    public static string MakeUnique(JNode obj, string baseName, JNode? except = null)
    {
        if (!obj.ContainsProperty(baseName, except))
        {
            return baseName;
        }
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in obj.Children)
        {
            if (!ReferenceEquals(c, except) && c.Name is not null)
            {
                existing.Add(c.Name);
            }
        }
        for (int i = 1; ; i++)
        {
            var candidate = baseName + i.ToString(CultureInfo.InvariantCulture);
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
