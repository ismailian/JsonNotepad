using System.ComponentModel;

namespace JsonNotepad.Controls.Theming;

/// <summary>
/// A borderless, themable tab strip (Tree View / Text View). The stock TabControl draws a
/// light 3D border that cannot be themed; XML Notepad replaces it with its own
/// NoBorderTabControl for the same reason.
/// </summary>
[DesignerCategory("Code")]
public sealed class ViewSwitcher : Control
{
    private readonly List<(string Text, Control Page)> _pages = new();
    private Theme _theme = Theme.Light;
    private int _selected;
    private int _hot = -1;

    public ViewSwitcher()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        AccessibleRole = AccessibleRole.PageTabList;
        AccessibleName = "Views";
        UpdateHeight();
    }

    public event EventHandler? SelectedIndexChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Theme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            value = Math.Clamp(value, 0, Math.Max(0, _pages.Count - 1));
            if (value == _selected && _pages.Count > 0 && _pages[value].Page.Visible)
            {
                return;
            }
            _selected = value;
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].Page.Visible = i == _selected;
            }
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void AddPage(string text, Control page)
    {
        _pages.Add((text, page));
        page.Visible = _pages.Count - 1 == _selected;
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateHeight();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateHeight();
    }

    private void UpdateHeight() => Height = Font.Height + LogicalToDeviceUnits(12);

    private Rectangle TabBounds(int index)
    {
        int x = LogicalToDeviceUnits(6);
        for (int i = 0; i < _pages.Count; i++)
        {
            int w = TextRenderer.MeasureText(_pages[i].Text, Font).Width + LogicalToDeviceUnits(20);
            if (i == index)
            {
                return new Rectangle(x, 0, w, Height);
            }
            x += w + LogicalToDeviceUnits(2);
        }
        return Rectangle.Empty;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var strip = _theme.IsDark ? Color.FromArgb(0x2D, 0x2D, 0x30) : SystemColors.Control;
        var hover = _theme.IsDark ? Color.FromArgb(0x3E, 0x3E, 0x42) : Color.FromArgb(0xE5, 0xE5, 0xE5);
        using (var back = new SolidBrush(strip))
        {
            g.FillRectangle(back, ClientRectangle);
        }
        for (int i = 0; i < _pages.Count; i++)
        {
            var r = TabBounds(i);
            bool selected = i == _selected;
            if (selected || i == _hot)
            {
                using var fill = new SolidBrush(selected ? _theme.Window : hover);
                g.FillRectangle(fill, r);
            }
            if (selected)
            {
                using var accent = new SolidBrush(_theme.DropFeedback);
                g.FillRectangle(accent, r.X, r.Bottom - LogicalToDeviceUnits(2), r.Width, LogicalToDeviceUnits(2));
            }
            TextRenderer.DrawText(g, _pages[i].Text, Font, r, selected ? _theme.WindowText : _theme.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hot = HitTest(e.Location);
        if (hot != _hot)
        {
            _hot = hot;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int i = HitTest(e.Location);
        if (i >= 0)
        {
            SelectedIndex = i;
            _pages[i].Page.Focus();
        }
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            if (TabBounds(i).Contains(p))
            {
                return i;
            }
        }
        return -1;
    }
}
