namespace JsonNotepad.Controls.Tree;

/// <summary>
/// The single TextBox overlaid on a cell while editing (XML Notepad's TextEditorOverlay idea).
/// Enter commits, Escape cancels, Ctrl+Enter inserts a line break, Tab commits and switches column.
/// Editing keys are kept local so the main menu's shortcuts (Ctrl+C, Delete, …) don't steal them.
/// </summary>
internal sealed class InPlaceEditor : TextBox
{
    public InPlaceEditor()
    {
        BorderStyle = BorderStyle.FixedSingle;
        Multiline = true;
        AcceptsReturn = false;
        AcceptsTab = false;
        WordWrap = false;
        MaxLength = int.MaxValue;
        Visible = false;
    }

    public event EventHandler? CommitRequested;

    public event EventHandler? CancelRequested;

    public event EventHandler? TabRequested;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Enter:
                CommitRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case Keys.Escape:
                CancelRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case Keys.Tab:
            case Keys.Shift | Keys.Tab:
                TabRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case Keys.Control | Keys.Enter:
            case Keys.Shift | Keys.Enter:
                SelectedText = Environment.NewLine;
                GrowToFit();
                return true;
            case Keys.Control | Keys.A:
                SelectAll();
                return true;
        }
        if (IsEditingKey(keyData))
        {
            return false; // let the TextBox handle it natively instead of the form's menu
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    internal int MaxHeight = 200;

    internal void GrowToFit()
    {
        int lines = Math.Max(1, Lines.Length);
        int lineHeight = Font.Height;
        int desired = Math.Min(MaxHeight, (lines * lineHeight) + 6);
        if (desired > Height)
        {
            Height = desired;
        }
        ScrollBars = lines * lineHeight + 6 > MaxHeight ? ScrollBars.Vertical : ScrollBars.None;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (Visible)
        {
            GrowToFit();
        }
    }

    private static bool IsEditingKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var mods = keyData & Keys.Modifiers;
        if (mods == Keys.Control && key is Keys.C or Keys.X or Keys.V or Keys.Z or Keys.Y or Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.Back or Keys.Delete)
        {
            return true;
        }
        if (mods is Keys.None or Keys.Shift && key is Keys.Delete or Keys.Insert or Keys.Home or Keys.End or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Back)
        {
            return true;
        }
        return mods == (Keys.Control | Keys.Shift) && key is Keys.Left or Keys.Right or Keys.Home or Keys.End;
    }
}
