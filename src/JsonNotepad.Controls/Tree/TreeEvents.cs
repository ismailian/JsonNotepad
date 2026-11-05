using JsonNotepad.Core.Editing;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Controls.Tree;

/// <summary>The two columns of the tree: member names (left) and values (right).</summary>
public enum TreePane
{
    Names,
    Values,
}

/// <summary>Raised when the user commits an in-place edit. Set <see cref="Error"/> to reject it.</summary>
public sealed class NodeEditEventArgs(JNode node, TreePane pane, string newText) : EventArgs
{
    public JNode Node { get; } = node;

    public TreePane Pane { get; } = pane;

    public string NewText { get; } = newText;

    /// <summary>When set by a handler, the edit is rejected and the message is shown to the user.</summary>
    public string? Error { get; set; }
}

/// <summary>Raised when a node is dropped onto another node.</summary>
public sealed class NodeDropEventArgs(JNode node, JNode target, InsertPosition position, bool copy) : EventArgs
{
    public JNode Node { get; } = node;

    public JNode Target { get; } = target;

    public InsertPosition Position { get; } = position;

    public bool Copy { get; } = copy;
}

public sealed class FilesDroppedEventArgs(string[] files) : EventArgs
{
    public IReadOnlyList<string> Files { get; } = files;
}

/// <summary>Drag payload for in-process node moves.</summary>
internal sealed class NodeDragData(JNode node, JsonTreeView source)
{
    public const string Format = "JsonNotepad.TreeNode";

    public JNode Node { get; } = node;

    public JsonTreeView Source { get; } = source;
}
