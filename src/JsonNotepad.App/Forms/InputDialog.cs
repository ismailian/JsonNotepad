namespace JsonNotepad.App.Forms;

/// <summary>
/// Single- or multi-line text prompt with an optional validator that keeps the dialog
/// open and shows the error inline (used by Go to Path, Go to Line and Edit as JSON).
/// </summary>
internal sealed class InputDialog : DialogBase
{
    private readonly TextBox _text;
    private readonly Label _error;
    private readonly Func<string, string?>? _validate;

    private InputDialog(string title, string prompt, string initial, bool multiline, Font? font, Func<string, string?>? validate)
    {
        _validate = validate;
        Text = title;
        ClientSize = multiline ? new Size(720, 480) : new Size(520, 150);
        MinimumSize = multiline ? new Size(400, 300) : new Size(360, 170);

        var label = new Label { Text = prompt, Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 6) };
        _text = new TextBox
        {
            Dock = multiline ? DockStyle.Fill : DockStyle.Top,
            Multiline = multiline,
            AcceptsReturn = multiline,
            AcceptsTab = multiline,
            ScrollBars = multiline ? ScrollBars.Both : ScrollBars.None,
            WordWrap = false,
            MaxLength = int.MaxValue,
            Text = multiline ? initial.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal) : initial,
        };
        if (font is not null)
        {
            _text.Font = font;
        }
        _error = new Label { Dock = DockStyle.Bottom, AutoSize = true, ForeColor = Color.Firebrick, Padding = new Padding(0, 6, 0, 0) };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Accept();

        Controls.Add(_text);
        Controls.Add(label);
        Controls.Add(_error);
        Controls.Add(CreateButtonBar(cancel, ok));
        AcceptButton = multiline ? null : ok;
        CancelButton = cancel;
        if (multiline)
        {
            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && e.Control)
                {
                    e.Handled = true;
                    Accept();
                }
            };
        }
    }

    public string Value => _text.Text;

    public static string? Show(IWin32Window owner, string title, string prompt, string initial, Func<string, string?>? validate = null)
    {
        using var dlg = new InputDialog(title, prompt, initial, multiline: false, null, validate);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Value : null;
    }

    public static string? ShowMultiline(IWin32Window owner, string title, string prompt, string initial, Font font, Func<string, string?>? validate = null)
    {
        using var dlg = new InputDialog(title, prompt, initial, multiline: true, font, validate);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Value : null;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _text.Focus();
        if (!_text.Multiline)
        {
            _text.SelectAll();
        }
    }

    private void Accept()
    {
        var error = _validate?.Invoke(_text.Text);
        if (error is not null)
        {
            _error.Text = error;
            System.Media.SystemSounds.Beep.Play();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
