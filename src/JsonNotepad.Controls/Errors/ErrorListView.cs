using System.ComponentModel;
using System.Globalization;
using JsonNotepad.Controls.Icons;
using JsonNotepad.Controls.Theming;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Controls.Errors;

/// <summary>
/// The Error List pane (XML Notepad's TaskList): a virtual-mode ListView, so thousands of
/// schema violations cost nothing until they scroll into view. Double-click or Enter raises
/// <see cref="DiagnosticActivated"/> so the host can navigate to the offending node.
/// </summary>
[DesignerCategory("Code")]
public sealed class ErrorListView : ListView
{
    private IReadOnlyList<Diagnostic> _items = Array.Empty<Diagnostic>();

    public ErrorListView()
    {
        View = View.Details;
        VirtualMode = true;
        FullRowSelect = true;
        MultiSelect = false;
        HideSelection = false;
        GridLines = false;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        DoubleBuffered = true;
        AccessibleName = "Error list";

        Columns.Add(string.Empty, 26);
        Columns.Add("Description", 520);
        Columns.Add("Location", 260);
        Columns.Add("Source", 90);
        Columns.Add("File", 220);

        SmallImageList = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };
        BuildImages(Theme.Light);
    }

    private Theme _theme = Theme.Light;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int size = LogicalToDeviceUnits(16);
        if (SmallImageList!.ImageSize.Width != size)
        {
            SmallImageList.ImageSize = new Size(size, size);
            BuildImages(_theme);
            Columns[0].Width = LogicalToDeviceUnits(26);
        }
    }

    public event EventHandler<Diagnostic>? DiagnosticActivated;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<Diagnostic> Diagnostics
    {
        get => _items;
        set
        {
            _items = value ?? Array.Empty<Diagnostic>();
            SelectedIndices.Clear();
            VirtualListSize = _items.Count;
            Invalidate();
        }
    }

    public int ErrorCount => _items.Count(d => d.Severity == DiagnosticSeverity.Error);

    public int WarningCount => _items.Count(d => d.Severity == DiagnosticSeverity.Warning);

    [Browsable(false)]
    public Diagnostic? SelectedDiagnostic =>
        SelectedIndices.Count > 0 && SelectedIndices[0] < _items.Count ? _items[SelectedIndices[0]] : null;

    public void ApplyTheme(Theme theme)
    {
        _theme = theme;
        BackColor = theme.Window;
        ForeColor = theme.WindowText;
        BuildImages(theme);
    }

    /// <summary>Selects and returns the next diagnostic (wrapping), for "Next Error" (F4).</summary>
    public Diagnostic? SelectNext()
    {
        if (_items.Count == 0)
        {
            return null;
        }
        int next = SelectedIndices.Count > 0 ? (SelectedIndices[0] + 1) % _items.Count : 0;
        SelectedIndices.Clear();
        SelectedIndices.Add(next);
        EnsureVisible(next);
        return _items[next];
    }

    protected override void OnRetrieveVirtualItem(RetrieveVirtualItemEventArgs e)
    {
        var d = _items[e.ItemIndex];
        var item = new ListViewItem(string.Empty, (int)d.Severity);
        item.SubItems.Add(d.Message);
        item.SubItems.Add(FormatLocation(d));
        item.SubItems.Add(d.Source);
        item.SubItems.Add(d.FileName is null ? string.Empty : Path.GetFileName(d.FileName));
        item.ToolTipText = d.Message;
        e.Item = item;
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        if (SelectedDiagnostic is { } d)
        {
            DiagnosticActivated?.Invoke(this, d);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Enter && SelectedDiagnostic is { } d)
        {
            DiagnosticActivated?.Invoke(this, d);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.C && e.Control && SelectedDiagnostic is { } c)
        {
            Clipboard.SetText($"{c.Severity}\t{c.Message}\t{FormatLocation(c)}\t{c.Source}");
            e.Handled = true;
        }
    }

    public static string FormatLocation(Diagnostic d)
    {
        if (d.Line is long line && line > 0)
        {
            return d.Column is long col && col > 0
                ? string.Create(CultureInfo.CurrentCulture, $"Line {line}, Col {col}")
                : string.Create(CultureInfo.CurrentCulture, $"Line {line}");
        }
        return d.Path ?? string.Empty;
    }

    private void BuildImages(Theme theme)
    {
        var list = SmallImageList!;
        list.Images.Clear();
        int size = list.ImageSize.Width;
        list.Images.Add(GlyphIcons.Render(GlyphIcons.Error, size, Color.FromArgb(0xE5, 0x1A, 0x1A)) ?? new Bitmap(size, size));
        list.Images.Add(GlyphIcons.Render(GlyphIcons.Warning, size, Color.FromArgb(0xD8, 0x8C, 0x00)) ?? new Bitmap(size, size));
        list.Images.Add(GlyphIcons.Render(GlyphIcons.Info, size, theme.IsDark ? Color.FromArgb(0x3F, 0x9C, 0xFF) : Color.FromArgb(0x00, 0x5F, 0xB8)) ?? new Bitmap(size, size));
    }
}
