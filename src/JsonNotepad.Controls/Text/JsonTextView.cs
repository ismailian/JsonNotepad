using System.ComponentModel;
using System.Text;
using JsonNotepad.Controls.Theming;
using JsonNotepad.Core.IO;

namespace JsonNotepad.Controls.Text;

/// <summary>
/// Read-only, virtualized, syntax-highlighted view of the serialized document (the
/// "Text View" tab — JsonNotepad's counterpart of XML Notepad's XSLT Output tab).
/// </summary>
/// <remarks>
/// A TextBox/RichTextBox becomes unusable with tens of megabytes of text. This control
/// keeps the text as one string plus a line-start index (<see cref="FormattedText"/>)
/// and paints only the visible lines, so a 100 MB document scrolls as smoothly as a
/// 1 KB one. Text is monospaced, which makes column math (selection, hit testing,
/// horizontal scrolling) exact and cheap.
/// </remarks>
[DesignerCategory("Code")]
public sealed class JsonTextView : Control
{
    private const int MaxTokenizedChars = 100_000;

    private readonly VScrollBar _vscroll;
    private readonly HScrollBar _hscroll;
    private readonly List<Token> _tokens = new();
    private FormattedText _content = FormattedText.Empty;
    private Theme _theme = Theme.Light;
    private string? _placeholder;
    private int _topLine;
    private int _leftColumn;
    private int _lineHeight = 16;
    private int _charWidth = 8;
    private int _gutterWidth = 40;
    private (int Line, int Col) _caret;
    private (int Line, int Col) _anchor;
    private bool _mouseSelecting;

    public JsonTextView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.IBeam;
        AccessibleRole = AccessibleRole.Text;
        AccessibleName = "JSON text";
        _vscroll = new VScrollBar { Dock = DockStyle.Right, TabStop = false, Cursor = Cursors.Default };
        _hscroll = new HScrollBar { Dock = DockStyle.Bottom, TabStop = false, Cursor = Cursors.Default };
        _vscroll.ValueChanged += (_, _) => { _topLine = _vscroll.Value; Invalidate(); };
        _hscroll.ValueChanged += (_, _) => { _leftColumn = _hscroll.Value; Invalidate(); };
        Controls.Add(_vscroll);
        Controls.Add(_hscroll);
        base.Font = new Font("Consolas", 10f);
        UpdateMetrics();
    }

    /// <summary>Caret moved to a different line.</summary>
    public event EventHandler? CaretLineChanged;

    /// <summary>A line was double-clicked (used to jump to the node in the tree).</summary>
    public event EventHandler<int>? LineActivated;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FormattedText Content
    {
        get => _content;
        set
        {
            _content = value ?? FormattedText.Empty;
            _caret = _anchor = (Math.Min(_caret.Line, _content.LineCount - 1), 0);
            _placeholder = null;
            UpdateMetrics();
            UpdateScrollBars();
            Invalidate();
        }
    }

    /// <summary>Message shown instead of content (e.g. "Generating…").</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Placeholder
    {
        get => _placeholder;
        set
        {
            _placeholder = value;
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
            Invalidate();
        }
    }

    /// <summary>0-based caret line.</summary>
    [Browsable(false)]
    public int CaretLine => _caret.Line;

    [Browsable(false)]
    public int CaretColumn => _caret.Col;

    [Browsable(false)]
    public bool HasSelection => _caret != _anchor;

    public void GoToLine(int line, bool select = false)
    {
        line = Math.Clamp(line, 0, _content.LineCount - 1);
        _anchor = (line, 0);
        _caret = select ? (line, _content.GetLine(line).Length) : (line, 0);
        if (!select)
        {
            _anchor = _caret;
        }
        ScrollToCaret(center: true);
        CaretLineChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SelectAll()
    {
        int last = _content.LineCount - 1;
        _anchor = (0, 0);
        _caret = (last, _content.GetLine(last).Length);
        Invalidate();
    }

    public string GetSelectedText()
    {
        if (!HasSelection)
        {
            return _content.GetLine(_caret.Line).ToString();
        }
        var (start, end) = OrderedSelection();
        int startOffset = OffsetOf(start);
        int endOffset = OffsetOf(end);
        return _content.Text.Substring(startOffset, endOffset - startOffset);
    }

    public void CopySelection()
    {
        var text = GetSelectedText();
        if (text.Length > 0)
        {
            Clipboard.SetText(text.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
        }
    }

    // ----- Metrics & scrolling ----------------------------------------------------------------

    private int VisibleLines => Math.Max(1, (ClientSize.Height - (_hscroll.Visible ? _hscroll.Height : 0)) / _lineHeight);

    private int VisibleColumns => Math.Max(1, (ClientSize.Width - _gutterWidth - (_vscroll.Visible ? _vscroll.Width : 0)) / _charWidth);

    private void UpdateMetrics()
    {
        var size = TextRenderer.MeasureText("MMMMMMMMMM", Font, Size.Empty, TextFormatFlags.NoPadding);
        _charWidth = Math.Max(1, size.Width / 10);
        _lineHeight = Math.Max(1, Font.Height + 1);
        int digits = Math.Max(3, _content.LineCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        _gutterWidth = (digits + 2) * _charWidth;
    }

    private void UpdateScrollBars()
    {
        _vscroll.Maximum = Math.Max(0, _content.LineCount - 1);
        _vscroll.LargeChange = VisibleLines;
        _vscroll.Visible = _content.LineCount > VisibleLines;
        _hscroll.Maximum = Math.Max(0, _content.MaxLineLength + 2);
        _hscroll.LargeChange = VisibleColumns;
        _hscroll.Visible = _content.MaxLineLength + 2 > VisibleColumns;
        SetTop(_topLine);
        SetLeft(_leftColumn);
    }

    private void SetTop(int line)
    {
        _topLine = Math.Clamp(line, 0, Math.Max(0, _content.LineCount - VisibleLines));
        if (_vscroll.Value != _topLine)
        {
            _vscroll.Value = Math.Min(_topLine, _vscroll.Maximum);
        }
        Invalidate();
    }

    private void SetLeft(int col)
    {
        _leftColumn = Math.Clamp(col, 0, Math.Max(0, _content.MaxLineLength + 2 - VisibleColumns));
        if (_hscroll.Value != _leftColumn)
        {
            _hscroll.Value = Math.Min(_leftColumn, _hscroll.Maximum);
        }
        Invalidate();
    }

    private void ScrollToCaret(bool center = false)
    {
        if (center && (_caret.Line < _topLine || _caret.Line >= _topLine + VisibleLines))
        {
            SetTop(_caret.Line - (VisibleLines / 3));
        }
        else if (_caret.Line < _topLine)
        {
            SetTop(_caret.Line);
        }
        else if (_caret.Line >= _topLine + VisibleLines)
        {
            SetTop(_caret.Line - VisibleLines + 1);
        }
        if (_caret.Col < _leftColumn)
        {
            SetLeft(_caret.Col - 4);
        }
        else if (_caret.Col >= _leftColumn + VisibleColumns)
        {
            SetLeft(_caret.Col - VisibleColumns + 4);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollBars();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateMetrics();
        UpdateScrollBars();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        if (ModifierKeys.HasFlag(Keys.Shift))
        {
            SetLeft(_leftColumn - (e.Delta * lines / 120 * 4));
        }
        else
        {
            SetTop(_topLine - (e.Delta * lines / 120));
        }
    }

    // ----- Painting ----------------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int width = ClientSize.Width - (_vscroll.Visible ? _vscroll.Width : 0);
        int height = ClientSize.Height - (_hscroll.Visible ? _hscroll.Height : 0);
        using (var back = new SolidBrush(_theme.Window))
        {
            g.FillRectangle(back, 0, 0, ClientSize.Width, ClientSize.Height);
        }

        if (_placeholder is not null)
        {
            TextRenderer.DrawText(g, _placeholder, base.Font, new Rectangle(0, 0, width, height), _theme.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        using (var gutter = new SolidBrush(_theme.Gutter))
        {
            g.FillRectangle(gutter, 0, 0, _gutterWidth - (_charWidth / 2), height);
        }

        var (selStart, selEnd) = OrderedSelection();
        using var selBrush = new SolidBrush(Focused ? Color.FromArgb(_theme.IsDark ? 140 : 90, _theme.Selection) : _theme.InactiveSelection);
        using var currentLine = new SolidBrush(_theme.CurrentLine);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoClipping;

        int last = Math.Min(_content.LineCount - 1, _topLine + VisibleLines);
        var region = g.Clip;
        for (int line = _topLine; line <= last; line++)
        {
            int y = (line - _topLine) * _lineHeight;
            var number = (line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            TextRenderer.DrawText(g, number, Font, new Rectangle(0, y, _gutterWidth - _charWidth, _lineHeight), _theme.GutterText,
                TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            g.SetClip(new Rectangle(_gutterWidth, 0, width - _gutterWidth, height));
            if (line == _caret.Line && !HasSelection)
            {
                g.FillRectangle(currentLine, _gutterWidth, y, width - _gutterWidth, _lineHeight);
            }

            var text = _content.GetLine(line);
            if (HasSelection && line >= selStart.Line && line <= selEnd.Line)
            {
                int c0 = line == selStart.Line ? selStart.Col : 0;
                int c1 = line == selEnd.Line ? selEnd.Col : text.Length + 1;
                int x0 = _gutterWidth + ((c0 - _leftColumn) * _charWidth);
                g.FillRectangle(selBrush, x0, y, Math.Max(_charWidth / 2, (c1 - c0) * _charWidth), _lineHeight);
            }

            DrawLineText(g, text, y, flags);
            g.Clip = region;
        }

        if (Focused && !HasSelection && _caret.Line >= _topLine && _caret.Line <= last)
        {
            int cx = _gutterWidth + ((_caret.Col - _leftColumn) * _charWidth);
            if (cx >= _gutterWidth)
            {
                using var caretPen = new Pen(_theme.WindowText);
                g.DrawLine(caretPen, cx, (_caret.Line - _topLine) * _lineHeight, cx, ((_caret.Line - _topLine) * _lineHeight) + _lineHeight - 1);
            }
        }
    }

    private void DrawLineText(Graphics g, ReadOnlySpan<char> text, int y, TextFormatFlags flags)
    {
        int visibleEnd = _leftColumn + VisibleColumns + 1;
        if (text.Length > MaxTokenizedChars)
        {
            // Pathologically long line: draw the visible slice uncolored.
            int s = Math.Min(_leftColumn, text.Length);
            var slice = text.Slice(s, Math.Min(text.Length - s, VisibleColumns + 1));
            TextRenderer.DrawText(g, slice.ToString(), Font, new Point(_gutterWidth, y), _theme.WindowText, flags);
            return;
        }

        JsonLexer.Tokenize(text, Math.Min(text.Length, visibleEnd + 1), _tokens);
        foreach (var t in _tokens)
        {
            int tEnd = t.Start + t.Length;
            if (tEnd <= _leftColumn)
            {
                continue;
            }
            if (t.Start >= visibleEnd)
            {
                break;
            }
            int s = Math.Max(t.Start, _leftColumn);
            int e = Math.Min(tEnd, visibleEnd);
            var color = t.Kind switch
            {
                TokenKind.Key => _theme.Key,
                TokenKind.String => _theme.StringValue,
                TokenKind.Number => _theme.NumberValue,
                TokenKind.Keyword => text[t.Start] == 'n' ? _theme.NullValue : _theme.BooleanValue,
                TokenKind.Punctuation => _theme.Punctuation,
                _ => _theme.WindowText,
            };
            int x = _gutterWidth + ((s - _leftColumn) * _charWidth);
            TextRenderer.DrawText(g, text[s..e].ToString(), Font, new Point(x, y), color, flags);
        }
    }

    // ----- Input ---------------------------------------------------------------------------

    protected override bool IsInputKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        return key is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End
               || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int line = _caret.Line, col = _caret.Col;
        int lineLen = _content.GetLine(line).Length;
        switch (e.KeyCode)
        {
            case Keys.Up: line--; break;
            case Keys.Down: line++; break;
            case Keys.Left: col = col > 0 ? col - 1 : col; break;
            case Keys.Right: col = Math.Min(lineLen, col + 1); break;
            case Keys.PageUp: line -= VisibleLines - 1; break;
            case Keys.PageDown: line += VisibleLines - 1; break;
            case Keys.Home: if (e.Control) { line = 0; } col = 0; break;
            case Keys.End:
                if (e.Control)
                {
                    line = _content.LineCount - 1;
                }
                col = _content.GetLine(Math.Clamp(line, 0, _content.LineCount - 1)).Length;
                break;
            case Keys.A when e.Control: SelectAll(); e.Handled = true; return;
            case Keys.C when e.Control:
            case Keys.Insert when e.Control:
                CopySelection();
                e.Handled = true;
                return;
            default: return;
        }
        MoveCaret(line, col, e.Shift);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        var pos = PositionFromPoint(e.Location);
        MoveCaret(pos.Line, pos.Col, ModifierKeys.HasFlag(Keys.Shift));
        _mouseSelecting = true;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_mouseSelecting)
        {
            var pos = PositionFromPoint(e.Location);
            MoveCaret(pos.Line, pos.Col, extend: true);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _mouseSelecting = false;
        Capture = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && _placeholder is null)
        {
            _anchor = _caret;
            LineActivated?.Invoke(this, _caret.Line);
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    private void MoveCaret(int line, int col, bool extend)
    {
        int oldLine = _caret.Line;
        line = Math.Clamp(line, 0, _content.LineCount - 1);
        col = Math.Clamp(col, 0, _content.GetLine(line).Length);
        _caret = (line, col);
        if (!extend)
        {
            _anchor = _caret;
        }
        ScrollToCaret();
        Invalidate();
        if (oldLine != line)
        {
            CaretLineChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private (int Line, int Col) PositionFromPoint(Point p)
    {
        int line = _topLine + (p.Y / _lineHeight);
        int col = _leftColumn + (int)Math.Round((p.X - _gutterWidth) / (double)_charWidth);
        line = Math.Clamp(line, 0, _content.LineCount - 1);
        return (line, Math.Clamp(col, 0, _content.GetLine(line).Length));
    }

    private ((int Line, int Col) Start, (int Line, int Col) End) OrderedSelection() =>
        _anchor.Line < _caret.Line || (_anchor.Line == _caret.Line && _anchor.Col <= _caret.Col) ? (_anchor, _caret) : (_caret, _anchor);

    private int OffsetOf((int Line, int Col) pos) =>
        Math.Min(_content.Text.Length, _content.LineStarts[pos.Line] + pos.Col);

    protected override AccessibleObject CreateAccessibilityInstance() => new TextAccessibleObject(this);

    private sealed class TextAccessibleObject(JsonTextView owner) : ControlAccessibleObject(owner)
    {
        public override string? Value
        {
            get
            {
                int caretLine = owner.CaretLine;
                var line = owner._content.GetLine(caretLine);
                var sb = new StringBuilder();
                sb.Append("Line ").Append(caretLine + 1).Append(": ").Append(line);
                return sb.ToString();
            }
        }
    }
}
