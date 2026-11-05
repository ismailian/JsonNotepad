using System.ComponentModel;
using System.Drawing.Drawing2D;
using JsonNotepad.Controls.Icons;
using JsonNotepad.Controls.Theming;
using JsonNotepad.Core.Editing;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Controls.Tree;

/// <summary>
/// Owner-drawn, virtualized two-column tree for a <see cref="JDocument"/>: member names
/// with expand glyphs, tree lines and type icons on the left, values on the right,
/// separated by a draggable divider — the JSON counterpart of XML Notepad's
/// XmlTreeView + NodeTextView pair, merged into one control so there is a single
/// scroll position, focus and selection to keep in sync.
/// </summary>
/// <remarks>
/// Rendering cost is proportional to the rows on screen, never to the document:
/// see <see cref="TreeRows"/>. Structural model changes only mark the row list dirty;
/// it is rebuilt lazily before the next paint or navigation.
/// </remarks>
[DesignerCategory("Code")]
public sealed partial class JsonTreeView : Control
{
    private const int MaxDisplayChars = 512;

    private readonly TreeRows _rows = new();
    private readonly VScrollBar _vscroll;
    private readonly InPlaceEditor _editor;
    private readonly ToolTip _toolTip;
    private readonly System.Windows.Forms.Timer _searchTimer;

    private JDocument? _document;
    private JNode? _selected;
    private int _selectedRowHint = -1;
    private int _topRow;
    private bool _rowsDirty = true;
    private TreePane _activePane = TreePane.Names;
    private int _nameWidth = 280;
    private Theme _theme = Theme.Light;
    private bool _showTreeLines = true;

    // Metrics (recomputed on font/DPI change)
    private int _rowHeight = 20;
    private int _indent = 18;
    private int _iconSize = 16;
    private int _padding = 4;

    // Editing
    private JNode? _editNode;
    private TreePane _editPane;
    private bool _endingEdit;

    // Mouse state
    private bool _draggingSplitter;
    private Point _dragStart;
    private JNode? _dragCandidate;
    private string? _toolTipText;
    private int _dropRow = -1;
    private InsertPosition _dropPosition;

    // Incremental search
    private string _searchText = string.Empty;

    public JsonTreeView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
        TabStop = true;
        AllowDrop = true;
        AccessibleRole = AccessibleRole.Outline;
        AccessibleName = "JSON tree";

        _vscroll = new VScrollBar { Dock = DockStyle.Right, Visible = false, TabStop = false };
        _vscroll.ValueChanged += (_, _) =>
        {
            if (_topRow != _vscroll.Value)
            {
                _topRow = _vscroll.Value;
                CancelEdit();
                Invalidate();
            }
        };
        Controls.Add(_vscroll);

        _editor = new InPlaceEditor();
        _editor.CommitRequested += (_, _) => EndEdit(cancel: false);
        _editor.CancelRequested += (_, _) => EndEdit(cancel: true);
        _editor.TabRequested += (_, _) =>
        {
            if (EndEdit(cancel: false))
            {
                ActivePane = ActivePane == TreePane.Names ? TreePane.Values : TreePane.Names;
            }
        };
        _editor.LostFocus += (_, _) =>
        {
            if (IsEditing && !_endingEdit && !_editor.Focused)
            {
                if (!EndEdit(cancel: false))
                {
                    EndEdit(cancel: true);
                }
            }
        };
        Controls.Add(_editor);

        _toolTip = new ToolTip { InitialDelay = 600, ReshowDelay = 200, AutoPopDelay = 15000, ShowAlways = false };

        _searchTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        _searchTimer.Tick += (_, _) => ResetIncrementalSearch();

        UpdateMetrics();
    }

    // ----- Events --------------------------------------------------------------

    public event EventHandler? SelectedNodeChanged;

    public event EventHandler? ActivePaneChanged;

    /// <summary>Raised when an in-place edit is committed; the handler applies (or rejects) it.</summary>
    public event EventHandler<NodeEditEventArgs>? NodeEditCommitting;

    public event EventHandler<NodeDropEventArgs>? NodeDropped;

    public event EventHandler<FilesDroppedEventArgs>? FilesDropped;

    /// <summary>Raised when the incremental (type-to-find) search text changes; empty when it ends.</summary>
    public event EventHandler<string>? IncrementalSearchChanged;

    // ----- Properties ------------------------------------------------------------

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public JDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(_document, value))
            {
                return;
            }
            CancelEdit();
            if (_document is not null)
            {
                _document.Changed -= OnDocumentChanged;
            }
            _document = value;
            _rows.Clear();
            _topRow = 0;
            _selected = null;
            _selectedRowHint = -1;
            if (_document is not null)
            {
                _document.Changed += OnDocumentChanged;
                _rows.Expanded.Add(_document.Root);
            }
            _rowsDirty = true;
            SelectNode(_document?.Root, raise: true);
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public JNode? SelectedNode
    {
        get => _selected;
        set => SelectNode(value, raise: true);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TreePane ActivePane
    {
        get => _activePane;
        set
        {
            if (_activePane != value)
            {
                _activePane = value;
                ResetIncrementalSearch();
                Invalidate();
                ActivePaneChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    [DefaultValue(280)]
    public int NameColumnWidth
    {
        get => _nameWidth;
        set
        {
            _nameWidth = Math.Max(60, value);
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool ShowTreeLines
    {
        get => _showTreeLines;
        set
        {
            _showTreeLines = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Theme Theme
    {
        get => _theme;
        set
        {
            _theme = value ?? Theme.Light;
            BackColor = _theme.Window;
            ForeColor = _theme.WindowText;
            _editor.BackColor = _theme.Window;
            _editor.ForeColor = _theme.WindowText;
            Invalidate();
        }
    }

    [Browsable(false)]
    public bool IsEditing => _editor.Visible;

    /// <summary>The in-place editor while editing (so the host can route clipboard commands to it).</summary>
    [Browsable(false)]
    public TextBox EditorTextBox => _editor;

    [Browsable(false)]
    public int VisibleRowCount
    {
        get
        {
            EnsureRows();
            return _rows.Count;
        }
    }

    // ----- Expansion ------------------------------------------------------------------

    public bool IsExpanded(JNode node) => _rows.IsExpanded(node);

    public void Expand(JNode node)
    {
        EnsureRows();
        int row = _rows.IndexOf(node);
        if (row >= 0)
        {
            _rows.Expand(row);
            AfterRowsChanged();
        }
        else if (node.HasChildren)
        {
            _rows.Expanded.Add(node);
            _rowsDirty = true;
            Invalidate();
        }
    }

    public void Collapse(JNode node)
    {
        EnsureRows();
        int row = _rows.IndexOf(node);
        if (row >= 0)
        {
            _rows.Collapse(row);
            if (_selected is not null && node.IsAncestorOf(_selected))
            {
                SelectNode(node, raise: true);
            }
            AfterRowsChanged();
        }
        else
        {
            _rows.Expanded.Remove(node);
        }
    }

    public void Toggle(JNode node)
    {
        if (IsExpanded(node))
        {
            Collapse(node);
        }
        else
        {
            Expand(node);
        }
    }

    /// <summary>Expands every container in the subtree (or the whole document).</summary>
    public void ExpandAll(JNode? node = null)
    {
        node ??= _document?.Root;
        if (node is null)
        {
            return;
        }
        CancelEdit();
        foreach (var n in node.DescendantsAndSelf())
        {
            if (n.HasChildren)
            {
                _rows.Expanded.Add(n);
            }
        }
        _rowsDirty = true;
        AfterRowsChanged();
    }

    public void CollapseAll(JNode? node = null)
    {
        node ??= _document?.Root;
        if (node is null)
        {
            return;
        }
        CancelEdit();
        if (ReferenceEquals(node, _document?.Root))
        {
            _rows.Expanded.Clear();
            _rows.Expanded.Add(node);
        }
        else
        {
            foreach (var n in node.DescendantsAndSelf())
            {
                _rows.Expanded.Remove(n);
            }
        }
        _rowsDirty = true;
        if (_selected is not null && !IsVisibleInTree(_selected))
        {
            SelectNode(node, raise: true);
        }
        AfterRowsChanged();
    }

    /// <summary>Expands containers down to <paramref name="depth"/> levels (root = 0), within <paramref name="maxRows"/>.</summary>
    public void ExpandToDepth(int depth, int maxRows = 5000)
    {
        if (_document is null)
        {
            return;
        }
        _rows.Expanded.Clear();
        _rows.Expanded.Add(_document.Root);
        // Breadth-first so the budget is spent on the top levels first.
        var queue = new Queue<(JNode, int)>();
        queue.Enqueue((_document.Root, 0));
        long rows = 1 + _document.Root.Count;
        while (queue.Count > 0)
        {
            var (n, d) = queue.Dequeue();
            if (d >= depth)
            {
                continue;
            }
            foreach (var c in n.Children)
            {
                if (c.HasChildren && rows + c.Count <= maxRows)
                {
                    _rows.Expanded.Add(c);
                    rows += c.Count;
                    queue.Enqueue((c, d + 1));
                }
            }
        }
        _rowsDirty = true;
        AfterRowsChanged();
    }

    /// <summary>Expands ancestors and scrolls so <paramref name="node"/> is on screen.</summary>
    public void EnsureVisible(JNode node)
    {
        bool changed = false;
        for (var p = node.Parent; p is not null; p = p.Parent)
        {
            changed |= _rows.Expanded.Add(p);
        }
        if (changed)
        {
            _rowsDirty = true;
        }
        EnsureRows();
        int row = _rows.IndexOf(node, _selectedRowHint);
        if (row < 0)
        {
            return;
        }
        int visible = FullyVisibleRows;
        if (row < _topRow)
        {
            SetTopRow(row);
        }
        else if (row >= _topRow + visible)
        {
            SetTopRow(row - visible + 1);
        }
        Invalidate();
    }

    /// <summary>Forces a rebuild of the visible rows (e.g. after many external changes).</summary>
    public void RefreshRows()
    {
        _rowsDirty = true;
        Invalidate();
    }

    // ----- Editing ---------------------------------------------------------------------

    /// <summary>Starts editing the selected node in <paramref name="pane"/>.</summary>
    public bool BeginEdit(TreePane pane, string? initialText = null)
    {
        var node = _selected;
        if (node is null || _document is null)
        {
            return false;
        }
        if (pane == TreePane.Names && !NodeEditor.CanRename(node))
        {
            pane = TreePane.Values;
        }
        if (pane == TreePane.Values && !NodeEditor.CanEditValue(node))
        {
            if (!NodeEditor.CanRename(node))
            {
                return false;
            }
            pane = TreePane.Names;
        }
        CancelEdit();
        EnsureVisible(node);
        int row = _rows.IndexOf(node, _selectedRowHint);
        if (row < 0)
        {
            return false;
        }
        ActivePane = pane;
        _editNode = node;
        _editPane = pane;

        var text = initialText ?? (pane == TreePane.Names ? node.Name ?? string.Empty : node.Value ?? string.Empty);
        var cell = GetCellBounds(row, pane);
        _editor.Font = Font;
        _editor.MaxHeight = Math.Max(_rowHeight * 3, ContentWidthHeight().Height - cell.Top - 2);
        _editor.Bounds = new Rectangle(cell.X, cell.Y, Math.Max(40, cell.Width), _rowHeight);
        _editor.Text = text;
        _editor.Visible = true;
        _editor.GrowToFit();
        _editor.Focus();
        if (initialText is null)
        {
            _editor.SelectAll();
        }
        else
        {
            _editor.SelectionStart = _editor.TextLength;
        }
        Invalidate();
        return true;
    }

    /// <summary>Ends editing. Returns false when the host rejected the new text (editor stays open).</summary>
    public bool EndEdit(bool cancel)
    {
        if (!IsEditing || _endingEdit)
        {
            return true;
        }
        var node = _editNode!;
        if (!cancel)
        {
            var args = new NodeEditEventArgs(node, _editPane, _editor.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
            NodeEditCommitting?.Invoke(this, args);
            if (args.Error is not null)
            {
                _toolTip.Show(args.Error, _editor, 0, _editor.Height + 2, 4000);
                System.Media.SystemSounds.Beep.Play();
                return false;
            }
        }
        _endingEdit = true;
        try
        {
            _toolTip.Hide(_editor);
            // Move focus back to the tree BEFORE hiding the editor; hiding a focused control
            // would otherwise let Windows move focus to the next control in tab order.
            if (_editor.Focused)
            {
                Focus();
            }
            _editor.Visible = false;
            _editNode = null;
        }
        finally
        {
            _endingEdit = false;
        }
        Invalidate();
        return true;
    }

    public void CancelEdit() => EndEdit(cancel: true);

    // ----- Incremental search -------------------------------------------------------------------

    /// <summary>Starts type-to-find (also active implicitly when typing in the tree).</summary>
    public void StartIncrementalSearch()
    {
        Focus();
        _searchText = string.Empty;
        _searchTimer.Stop();
        IncrementalSearchChanged?.Invoke(this, "​"); // non-empty marker: search armed
    }

    private void ResetIncrementalSearch()
    {
        _searchTimer.Stop();
        if (_searchText.Length > 0)
        {
            _searchText = string.Empty;
            IncrementalSearchChanged?.Invoke(this, string.Empty);
        }
    }

    private void IncrementalSearch(char c)
    {
        EnsureRows();
        if (_rows.Count == 0)
        {
            return;
        }
        _searchText += c;
        _searchTimer.Stop();
        _searchTimer.Start();
        IncrementalSearchChanged?.Invoke(this, _searchText);

        int start = Math.Max(0, CurrentRow());
        bool newSearch = _searchText.Length == 1;
        for (int i = 0; i < _rows.Count; i++)
        {
            int r = (start + i + (newSearch ? 1 : 0)) % _rows.Count;
            var text = GetPaneText(_rows[r], _activePane);
            if (text.StartsWith(_searchText, StringComparison.OrdinalIgnoreCase))
            {
                SelectRow(r);
                return;
            }
        }
        System.Media.SystemSounds.Beep.Play();
    }

    // ----- Model change handling ----------------------------------------------------------------

    private void OnDocumentChanged(object? sender, JDocumentChangedEventArgs e)
    {
        if (e.Kind is JDocumentChangeKind.Renamed or JDocumentChangeKind.ValueChanged)
        {
            Invalidate(); // no structural change: rows still valid
            return;
        }
        if (IsEditing && !_endingEdit)
        {
            CancelEdit(); // the row under the editor may have moved
        }
        if (e.Kind == JDocumentChangeKind.Replaced && e.OldNode is not null && _rows.Expanded.Remove(e.OldNode) && e.Node.HasChildren)
        {
            _rows.Expanded.Add(e.Node);
        }
        if (e.Kind == JDocumentChangeKind.Inserted && e.Parent is not null && e.Parent.Count == 1)
        {
            _rows.Expanded.Add(e.Parent); // first child: show it
        }
        _rowsDirty = true;
        Invalidate();
    }

    private void EnsureRows()
    {
        if (!_rowsDirty)
        {
            return;
        }
        _rowsDirty = false;
        _rows.Rebuild(_document?.Root);
        UpdateScrollBar();

        if (_selected is not null && (_document is null || !_document.Contains(_selected)))
        {
            // The selected node was removed by someone other than our host; pick a neighbor.
            int row = Math.Clamp(_selectedRowHint, 0, Math.Max(0, _rows.Count - 1));
            SelectNode(_rows.Count > 0 ? _rows[row].Node : null, raise: true);
        }
        else if (_selected is not null)
        {
            _selectedRowHint = _rows.IndexOf(_selected, _selectedRowHint);
            if (_selectedRowHint < 0)
            {
                EnsureVisible(_selected);
            }
        }
    }

    private void AfterRowsChanged()
    {
        EnsureRows();
        UpdateScrollBar();
        if (_selected is not null)
        {
            _selectedRowHint = _rows.IndexOf(_selected, _selectedRowHint);
        }
        Invalidate();
    }

    private bool IsVisibleInTree(JNode node)
    {
        for (var p = node.Parent; p is not null; p = p.Parent)
        {
            if (!_rows.IsExpanded(p))
            {
                return false;
            }
        }
        return true;
    }

    // ----- Selection ----------------------------------------------------------------------------

    private void SelectNode(JNode? node, bool raise)
    {
        if (node is not null && _document is not null && !_document.Contains(node))
        {
            node = null;
        }
        bool changed = !ReferenceEquals(node, _selected);
        if (changed)
        {
            CancelEdit();
        }
        _selected = node;
        if (node is not null)
        {
            EnsureVisible(node);
            _selectedRowHint = _rows.IndexOf(node, _selectedRowHint);
        }
        Invalidate();
        if (changed && raise)
        {
            SelectedNodeChanged?.Invoke(this, EventArgs.Empty);
            if (Focused && _selectedRowHint >= 0)
            {
                AccessibilityNotifyClients(AccessibleEvents.Focus, _selectedRowHint);
                AccessibilityNotifyClients(AccessibleEvents.Selection, _selectedRowHint);
            }
        }
    }

    private void SelectRow(int row)
    {
        EnsureRows();
        if (_rows.Count == 0)
        {
            return;
        }
        row = Math.Clamp(row, 0, _rows.Count - 1);
        _selectedRowHint = row;
        SelectNode(_rows[row].Node, raise: true);
    }

    private int CurrentRow()
    {
        if (_selected is null)
        {
            return -1;
        }
        _selectedRowHint = _rows.IndexOf(_selected, _selectedRowHint);
        return _selectedRowHint;
    }

    // ----- Scrolling ---------------------------------------------------------------------------

    private int FullyVisibleRows => Math.Max(1, ClientSize.Height / Math.Max(1, _rowHeight));

    private void UpdateScrollBar()
    {
        int visible = FullyVisibleRows;
        bool needed = _rows.Count > visible;
        if (_vscroll.Visible != needed)
        {
            _vscroll.Visible = needed;
        }
        _vscroll.Minimum = 0;
        _vscroll.Maximum = Math.Max(0, _rows.Count - 1);
        _vscroll.LargeChange = visible;
        _vscroll.SmallChange = 1;
        SetTopRow(_topRow);
    }

    private void SetTopRow(int top)
    {
        int max = Math.Max(0, _rows.Count - FullyVisibleRows);
        top = Math.Clamp(top, 0, max);
        _topRow = top;
        if (_vscroll.Visible && _vscroll.Value != top)
        {
            _vscroll.Value = Math.Min(top, _vscroll.Maximum);
        }
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (IsEditing)
        {
            CancelEdit();
        }
        int lines = SystemInformation.MouseWheelScrollLines;
        int delta = lines < 0 ? -Math.Sign(e.Delta) * FullyVisibleRows : -e.Delta * Math.Max(1, lines) / 120;
        SetTopRow(_topRow + delta);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        CancelEdit();
        UpdateScrollBar();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateMetrics();
        UpdateScrollBar();
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateMetrics();
        UpdateScrollBar();
    }

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    private void UpdateMetrics()
    {
        _rowHeight = Math.Max(Scale(18), Font.Height + Scale(5));
        _indent = Scale(18);
        _iconSize = Math.Max(Scale(12), Math.Min(_rowHeight - Scale(4), Scale(16)));
        _padding = Scale(4);
    }

    private Size ContentWidthHeight() =>
        new(ClientSize.Width - (_vscroll.Visible ? _vscroll.Width : 0), ClientSize.Height);

    private int EffectiveNameWidth
    {
        get
        {
            int width = ContentWidthHeight().Width;
            return Math.Clamp(_nameWidth, Math.Min(60, width / 2), Math.Max(60, width - 60));
        }
    }

    // ----- Hit testing -------------------------------------------------------------------------

    private enum HitArea
    {
        None,
        Glyph,
        Name,
        Value,
        Splitter,
    }

    private (int Row, HitArea Area) HitTest(Point p)
    {
        int nameWidth = EffectiveNameWidth;
        if (Math.Abs(p.X - nameWidth) <= Scale(3))
        {
            return (-1, HitArea.Splitter);
        }
        EnsureRows();
        int row = _topRow + (p.Y / _rowHeight);
        if (p.Y < 0 || row >= _rows.Count)
        {
            return (-1, HitArea.None);
        }
        if (p.X > nameWidth)
        {
            return (row, HitArea.Value);
        }
        var r = _rows[row];
        int glyphLeft = _padding + (r.Depth * _indent);
        if (r.Node.HasChildren && p.X >= glyphLeft && p.X < glyphLeft + _indent)
        {
            return (row, HitArea.Glyph);
        }
        return (row, HitArea.Name);
    }

    private Rectangle GetCellBounds(int row, TreePane pane)
    {
        int y = (row - _topRow) * _rowHeight;
        int nameWidth = EffectiveNameWidth;
        if (pane == TreePane.Values)
        {
            return new Rectangle(nameWidth + 2, y, ContentWidthHeight().Width - nameWidth - 2, _rowHeight);
        }
        int x = TextLeft(_rows[row].Depth) - 2;
        return new Rectangle(x, y, Math.Max(60, nameWidth - x - 1), _rowHeight);
    }

    private int TextLeft(int depth) => _padding + ((depth + 1) * _indent) + _iconSize + Scale(4);

    // ----- Painting -----------------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        EnsureRows();
        var g = e.Graphics;
        var size = ContentWidthHeight();
        using (var back = new SolidBrush(_theme.Window))
        {
            g.FillRectangle(back, e.ClipRectangle);
        }

        int nameWidth = EffectiveNameWidth;
        int first = _topRow + Math.Max(0, e.ClipRectangle.Top / _rowHeight);
        int last = Math.Min(_rows.Count - 1, _topRow + (e.ClipRectangle.Bottom / _rowHeight));

        using var linePen = new Pen(_theme.TreeLines) { DashStyle = DashStyle.Dot };
        for (int r = first; r <= last; r++)
        {
            DrawRow(g, r, (r - _topRow) * _rowHeight, nameWidth, size.Width, linePen);
        }

        using (var splitter = new Pen(_theme.Splitter))
        {
            g.DrawLine(splitter, nameWidth, 0, nameWidth, size.Height);
        }

        if (_dropRow >= 0 && _dropRow < _rows.Count)
        {
            DrawDropFeedback(g, nameWidth);
        }

        if (_rows.Count == 0)
        {
            TextRenderer.DrawText(g, "No document", Font, new Rectangle(0, 0, size.Width, _rowHeight * 3), _theme.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private void DrawRow(Graphics g, int rowIndex, int y, int nameWidth, int totalWidth, Pen linePen)
    {
        var row = _rows[rowIndex];
        var node = row.Node;
        bool selected = ReferenceEquals(node, _selected);
        bool focused = Focused || ContainsFocus;
        int depth = row.Depth;
        int mid = y + (_rowHeight / 2);

        if (selected)
        {
            var nameRect = new Rectangle(TextLeft(depth) - Scale(2), y, nameWidth - TextLeft(depth) + Scale(2), _rowHeight);
            var valueRect = new Rectangle(nameWidth + 1, y, totalWidth - nameWidth - 1, _rowHeight);
            using var active = new SolidBrush(focused ? _theme.Selection : _theme.InactiveSelection);
            using var inactive = new SolidBrush(_theme.InactiveSelection);
            g.FillRectangle(_activePane == TreePane.Names ? active : inactive, nameRect);
            g.FillRectangle(_activePane == TreePane.Values ? active : inactive, valueRect);
        }

        // Tree lines: one vertical line per ancestor that has following siblings.
        if (_showTreeLines && depth > 0)
        {
            int cx = GlyphCenter(depth - 1);
            g.DrawLine(linePen, cx, y, cx, node.IsLastChild ? mid : y + _rowHeight);
            g.DrawLine(linePen, cx, mid, GlyphCenter(depth) - (node.HasChildren ? Scale(5) : -Scale(4)), mid);
            var ancestor = node.Parent;
            for (int level = depth - 1; level >= 1 && ancestor is not null; level--, ancestor = ancestor.Parent)
            {
                if (!ancestor.IsLastChild)
                {
                    int ax = GlyphCenter(level - 1);
                    g.DrawLine(linePen, ax, y, ax, y + _rowHeight);
                }
            }
        }

        if (node.HasChildren)
        {
            DrawGlyph(g, GlyphCenter(depth), mid, _rows.IsExpanded(node));
        }

        int iconX = _padding + ((depth + 1) * _indent);
        if (iconX < nameWidth)
        {
            g.DrawImage(NodeKindIcons.Get(node.Kind, _iconSize, _theme), iconX, y + ((_rowHeight - _iconSize) / 2));
        }

        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                                      TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        bool editingThis = IsEditing && ReferenceEquals(_editNode, node);

        int textX = TextLeft(depth);
        if (textX < nameWidth - Scale(8) && !(editingThis && _editPane == TreePane.Names))
        {
            Color color = selected && _activePane == TreePane.Names && focused ? _theme.SelectionText
                : node.Parent?.Kind == JNodeKind.Object ? _theme.Key : _theme.GrayText;
            TextRenderer.DrawText(g, GetLabel(row), Font, new Rectangle(textX, y, nameWidth - textX - Scale(4), _rowHeight), color, flags);
        }

        if (!(editingThis && _editPane == TreePane.Values))
        {
            Color color = selected && _activePane == TreePane.Values && focused ? _theme.SelectionText : _theme.ColorFor(node.Kind);
            TextRenderer.DrawText(g, GetDisplayValue(node), Font, new Rectangle(nameWidth + Scale(6), y, totalWidth - nameWidth - Scale(10), _rowHeight), color, flags);
        }
    }

    private int GlyphCenter(int depth) => _padding + (depth * _indent) + (_indent / 2);

    private void DrawGlyph(Graphics g, int cx, int cy, bool expanded)
    {
        int half = Scale(4);
        var box = new Rectangle(cx - half, cy - half, half * 2, half * 2);
        using (var back = new SolidBrush(_theme.Window))
        {
            g.FillRectangle(back, box);
        }
        using var pen = new Pen(_theme.Glyph);
        g.DrawRectangle(pen, box);
        int arm = half - Scale(2);
        g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
        if (!expanded)
        {
            g.DrawLine(pen, cx, cy - arm, cx, cy + arm);
        }
    }

    private void DrawDropFeedback(Graphics g, int nameWidth)
    {
        int y = (_dropRow - _topRow) * _rowHeight;
        int x = TextLeft(_rows[_dropRow].Depth) - Scale(2);
        using var pen = new Pen(_theme.DropFeedback, Scale(2));
        switch (_dropPosition)
        {
            case InsertPosition.Before:
                g.DrawLine(pen, x, y + 1, nameWidth, y + 1);
                break;
            case InsertPosition.After:
                g.DrawLine(pen, x, y + _rowHeight - 1, nameWidth, y + _rowHeight - 1);
                break;
            default:
                g.DrawRectangle(pen, x, y + 1, nameWidth - x - 1, _rowHeight - 3);
                break;
        }
    }

    private static string GetLabel(TreeRow row)
    {
        var node = row.Node;
        if (node.Parent is null)
        {
            return "(root)";
        }
        return node.Parent.Kind == JNodeKind.Array ? "[" + row.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]" : node.Name ?? string.Empty;
    }

    /// <summary>Single-line rendering of a value for the right column.</summary>
    public static string GetDisplayValue(JNode node)
    {
        switch (node.Kind)
        {
            case JNodeKind.Object:
                return node.Count == 0 ? "{ }" : "{" + node.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + "}";
            case JNodeKind.Array:
                return node.Count == 0 ? "[ ]" : "[" + node.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + "]";
            case JNodeKind.Null:
                return "null";
            case JNodeKind.String:
                var s = node.Value ?? string.Empty;
                bool truncated = s.Length > MaxDisplayChars;
                if (truncated)
                {
                    s = s[..MaxDisplayChars];
                }
                if (s.AsSpan().IndexOfAny("\r\n\t") >= 0)
                {
                    s = s.Replace("\r\n", "↵", StringComparison.Ordinal).Replace('\n', '↵').Replace('\r', '↵').Replace('\t', '→');
                }
                return truncated ? s + "…" : s;
            default:
                return node.Value ?? string.Empty;
        }
    }

    private static string GetPaneText(TreeRow row, TreePane pane) =>
        pane == TreePane.Names ? GetLabel(row) : GetDisplayValue(row.Node);

    // ----- Focus ----------------------------------------------------------------------------------

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        ResetIncrementalSearch();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_document is not null)
            {
                _document.Changed -= OnDocumentChanged;
            }
            _toolTip.Dispose();
            _searchTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
