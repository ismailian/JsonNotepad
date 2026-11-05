using JsonNotepad.Core.Editing;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Controls.Tree;

/// <summary>Keyboard, mouse, drag &amp; drop and accessibility for <see cref="JsonTreeView"/>.</summary>
public sealed partial class JsonTreeView
{
    // ----- Keyboard ---------------------------------------------------------------------------

    protected override bool IsInputKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End or Keys.Enter or Keys.Escape)
        {
            return true;
        }
        if (keyData is Keys.Tab or (Keys.Shift | Keys.Tab))
        {
            return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || IsEditing)
        {
            return;
        }
        EnsureRows();
        int row = CurrentRow();
        bool plain = !e.Control && !e.Alt;

        switch (e.KeyCode)
        {
            case Keys.Up when plain:
                SelectRow(row - 1);
                break;
            case Keys.Down when plain:
                SelectRow(row + 1);
                break;
            case Keys.PageUp when plain:
                SelectRow(row - FullyVisibleRows + 1);
                break;
            case Keys.PageDown when plain:
                SelectRow(row + FullyVisibleRows - 1);
                break;
            case Keys.Home when plain:
                SelectRow(0);
                break;
            case Keys.End when plain:
                SelectRow(_rows.Count - 1);
                break;
            case Keys.Left when plain:
                if (_selected is not null && _rows.IsExpanded(_selected) && _selected.HasChildren)
                {
                    Collapse(_selected);
                }
                else if (_selected?.Parent is not null)
                {
                    SelectNode(_selected.Parent, raise: true);
                }
                break;
            case Keys.Right when plain:
                if (_selected is { HasChildren: true })
                {
                    if (!_rows.IsExpanded(_selected))
                    {
                        Expand(_selected);
                    }
                    else
                    {
                        SelectNode(_selected[0], raise: true);
                    }
                }
                break;
            case Keys.Add when plain:
                if (_selected is not null)
                {
                    Expand(_selected);
                }
                break;
            case Keys.Subtract when plain:
                if (_selected is not null)
                {
                    Collapse(_selected);
                }
                break;
            case Keys.Multiply when plain:
                if (_selected is not null)
                {
                    ExpandAll(_selected);
                }
                break;
            case Keys.Enter when plain:
            case Keys.F2 when plain:
                if (!BeginEdit(e.KeyCode == Keys.F2 && NodeEditor.CanRename(_selected) ? TreePane.Names : _activePane))
                {
                    if (_selected is { HasChildren: true })
                    {
                        Toggle(_selected);
                    }
                }
                break;
            case Keys.Tab when !e.Control:
                ActivePane = ActivePane == TreePane.Names ? TreePane.Values : TreePane.Names;
                break;
            case Keys.Escape:
                ResetIncrementalSearch();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (e.Handled || IsEditing || char.IsControl(e.KeyChar) || ModifierKeys.HasFlag(Keys.Control) || ModifierKeys.HasFlag(Keys.Alt))
        {
            return;
        }
        if (e.KeyChar == ' ' && _searchText.Length == 0)
        {
            return;
        }
        IncrementalSearch(e.KeyChar);
        e.Handled = true;
    }

    // ----- Mouse ------------------------------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Focused && !IsEditing)
        {
            Focus();
        }
        if (IsEditing)
        {
            if (!EndEdit(cancel: false))
            {
                return;
            }
        }
        ResetIncrementalSearch();

        var (row, area) = HitTest(e.Location);
        if (area == HitArea.Splitter && e.Button == MouseButtons.Left)
        {
            _draggingSplitter = true;
            Capture = true;
            return;
        }
        if (row < 0)
        {
            return;
        }

        SelectRow(row);
        if (area == HitArea.Glyph && e.Button == MouseButtons.Left)
        {
            Toggle(_rows[row].Node);
            return;
        }
        ActivePane = area == HitArea.Value ? TreePane.Values : TreePane.Names;
        if (e.Button == MouseButtons.Left && _rows[row].Node.Parent is not null)
        {
            _dragStart = e.Location;
            _dragCandidate = _rows[row].Node;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_draggingSplitter)
        {
            NameColumnWidth = Math.Clamp(e.X, Scale(60), Math.Max(Scale(60), ContentWidthHeight().Width - Scale(60)));
            return;
        }

        if (_dragCandidate is not null && e.Button == MouseButtons.Left)
        {
            var drag = SystemInformation.DragSize;
            if (Math.Abs(e.X - _dragStart.X) > drag.Width || Math.Abs(e.Y - _dragStart.Y) > drag.Height)
            {
                var node = _dragCandidate;
                _dragCandidate = null;
                var data = new DataObject();
                data.SetData(NodeDragData.Format, new NodeDragData(node, this));
                DoDragDrop(data, DragDropEffects.Move | DragDropEffects.Copy);
            }
            return;
        }

        var (row, area) = HitTest(e.Location);
        Cursor = area == HitArea.Splitter ? Cursors.VSplit : Cursors.Default;
        UpdateToolTip(row, area);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragCandidate = null;
        if (_draggingSplitter)
        {
            _draggingSplitter = false;
            Capture = false;
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var (row, area) = HitTest(e.Location);
        if (row < 0 || area is HitArea.Glyph or HitArea.Splitter)
        {
            return;
        }
        var node = _rows[row].Node;
        if (area == HitArea.Value && NodeEditor.CanEditValue(node))
        {
            BeginEdit(TreePane.Values);
        }
        else if (node.HasChildren)
        {
            Toggle(node);
        }
        else
        {
            BeginEdit(area == HitArea.Value ? TreePane.Values : TreePane.Names);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _toolTip.Hide(this);
        _toolTipText = null;
    }

    private void UpdateToolTip(int row, HitArea area)
    {
        string? text = null;
        if (row >= 0 && area is HitArea.Name or HitArea.Value)
        {
            var r = _rows[row];
            var full = area == HitArea.Name ? GetLabel(r) : r.Node.Kind == JNodeKind.String ? r.Node.Value ?? string.Empty : GetDisplayValue(r.Node);
            int available = area == HitArea.Name
                ? EffectiveNameWidth - TextLeft(r.Depth) - Scale(4)
                : ContentWidthHeight().Width - EffectiveNameWidth - Scale(10);
            var shown = area == HitArea.Name ? full : GetDisplayValue(r.Node);
            if (shown.Length > 0 && (shown != full || TextRenderer.MeasureText(shown, Font).Width > available))
            {
                text = full.Length > 2000 ? full[..2000] + "…" : full;
            }
        }
        if (text != _toolTipText)
        {
            _toolTipText = text;
            if (text is null)
            {
                _toolTip.Hide(this);
            }
            else
            {
                _toolTip.SetToolTip(this, text);
            }
        }
    }

    // ----- Drag & drop ---------------------------------------------------------------------------

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            return;
        }
        if (e.Data?.GetData(NodeDragData.Format) is not NodeDragData drag || !ReferenceEquals(drag.Source, this))
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        var p = PointToClient(new Point(e.X, e.Y));
        AutoScrollDuringDrag(p);
        var (row, _) = HitTest(p);
        if (row < 0)
        {
            SetDropTarget(-1, default);
            e.Effect = DragDropEffects.None;
            return;
        }
        var target = _rows[row].Node;
        int offset = p.Y - ((row - _topRow) * _rowHeight);
        InsertPosition position = target.IsContainer && offset > _rowHeight / 4 && offset < _rowHeight * 3 / 4
            ? InsertPosition.Child
            : offset < _rowHeight / 2 ? InsertPosition.Before : InsertPosition.After;
        if (target.Parent is null)
        {
            position = InsertPosition.Child;
        }

        bool copy = (e.KeyState & 8) != 0; // Ctrl
        bool valid = copy ? NodeEditor.CanInsert(target, position)
                          : !ReferenceEquals(drag.Node, target) && !drag.Node.IsAncestorOf(target) && NodeEditor.CanInsert(target, position);
        SetDropTarget(valid ? row : -1, position);
        e.Effect = valid ? (copy ? DragDropEffects.Copy : DragDropEffects.Move) : DragDropEffects.None;
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        SetDropTarget(-1, default);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        int row = _dropRow;
        var position = _dropPosition;
        SetDropTarget(-1, default);

        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            FilesDropped?.Invoke(this, new FilesDroppedEventArgs(files));
            return;
        }
        if (e.Data?.GetData(NodeDragData.Format) is NodeDragData drag && row >= 0 && row < _rows.Count)
        {
            NodeDropped?.Invoke(this, new NodeDropEventArgs(drag.Node, _rows[row].Node, position, e.Effect == DragDropEffects.Copy));
        }
    }

    private void SetDropTarget(int row, InsertPosition position)
    {
        if (row != _dropRow || position != _dropPosition)
        {
            _dropRow = row;
            _dropPosition = position;
            Invalidate();
        }
    }

    private void AutoScrollDuringDrag(Point p)
    {
        if (p.Y < _rowHeight)
        {
            SetTopRow(_topRow - 1);
        }
        else if (p.Y > ClientSize.Height - _rowHeight)
        {
            SetTopRow(_topRow + 1);
        }
    }

    // ----- Accessibility ---------------------------------------------------------------------------

    protected override AccessibleObject CreateAccessibilityInstance() => new TreeAccessibleObject(this);

    private sealed class TreeAccessibleObject(JsonTreeView owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.Outline;

        public override int GetChildCount() => owner.VisibleRowCount;

        public override AccessibleObject? GetChild(int index) =>
            index >= 0 && index < owner._rows.Count ? new RowAccessibleObject(owner, index, this) : null;

        public override AccessibleObject? GetFocused()
        {
            int row = owner.CurrentRow();
            return row >= 0 ? GetChild(row) : null;
        }

        public override AccessibleObject? GetSelected() => GetFocused();
    }

    private sealed class RowAccessibleObject(JsonTreeView owner, int row, AccessibleObject parent) : AccessibleObject
    {
        private TreeRow Row => owner._rows[row];

        public override AccessibleObject Parent => parent;

        public override AccessibleRole Role => AccessibleRole.OutlineItem;

        public override string Name => $"{GetLabel(Row)}: {GetDisplayValue(Row.Node)}";

        public override string Value => Row.Node.Kind.DisplayName();

        public override AccessibleStates State
        {
            get
            {
                var s = AccessibleStates.Selectable | AccessibleStates.Focusable;
                if (ReferenceEquals(Row.Node, owner._selected))
                {
                    s |= AccessibleStates.Selected | AccessibleStates.Focused;
                }
                if (Row.Node.HasChildren)
                {
                    s |= owner._rows.IsExpanded(Row.Node) ? AccessibleStates.Expanded : AccessibleStates.Collapsed;
                }
                return s;
            }
        }

        public override Rectangle Bounds
        {
            get
            {
                int y = (row - owner._topRow) * owner._rowHeight;
                return owner.RectangleToScreen(new Rectangle(0, y, owner.ContentWidthHeight().Width, owner._rowHeight));
            }
        }

        public override void Select(AccessibleSelection flags)
        {
            if (flags.HasFlag(AccessibleSelection.TakeSelection) || flags.HasFlag(AccessibleSelection.TakeFocus))
            {
                owner.SelectRow(row);
            }
        }

        public override void DoDefaultAction()
        {
            if (Row.Node.HasChildren)
            {
                owner.Toggle(Row.Node);
            }
        }
    }
}
