using JsonNotepad.Core.Search;

namespace JsonNotepad.App.Forms;

/// <summary>What the Find dialog searches in (implemented by the main window).</summary>
internal interface IFindTarget
{
    /// <summary>Selects and returns the next match, or null when there is none.</summary>
    FindMatch? FindNext(FindOptions options);

    /// <summary>Replaces the current match (if the selection is one) and moves to the next.</summary>
    string? ReplaceCurrent(FindOptions options, string replacement);

    (int Replaced, int Skipped) ReplaceAll(FindOptions options, string replacement);
}

/// <summary>Modeless Find / Replace dialog (Ctrl+F / Ctrl+H).</summary>
internal sealed class FindReplaceForm : DialogBase
{
    private readonly IFindTarget _target;
    private readonly List<string> _findHistory;
    private readonly List<string> _replaceHistory;
    private readonly ComboBox _find;
    private readonly ComboBox _replace;
    private readonly Label _replaceLabel;
    private readonly CheckBox _matchCase;
    private readonly CheckBox _wholeWord;
    private readonly CheckBox _regex;
    private readonly RadioButton _scopeAll;
    private readonly RadioButton _scopeNames;
    private readonly RadioButton _scopeValues;
    private readonly CheckBox _up;
    private readonly Button _findNext;
    private readonly Button _replaceOne;
    private readonly Button _replaceAll;
    private readonly Label _status;

    public FindReplaceForm(IFindTarget target, List<string> findHistory, List<string> replaceHistory)
    {
        _target = target;
        _findHistory = findHistory;
        _replaceHistory = replaceHistory;
        Text = "Find";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ClientSize = new Size(520, 250);
        KeyPreview = true;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6, AutoSize = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _find = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        _replace = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        _find.Items.AddRange(_findHistory.Cast<object>().ToArray());
        _replace.Items.AddRange(_replaceHistory.Cast<object>().ToArray());
        _replaceLabel = new Label { Text = "Re&place with:", AutoSize = true, Anchor = AnchorStyles.Left };

        _matchCase = new CheckBox { Text = "Match &case", AutoSize = true };
        _wholeWord = new CheckBox { Text = "Match &whole word", AutoSize = true };
        _regex = new CheckBox { Text = "Use regular e&xpressions", AutoSize = true };
        var options = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill };
        options.Controls.AddRange(new Control[] { _matchCase, _wholeWord, _regex });

        _scopeAll = new RadioButton { Text = "Names and values", Checked = true, AutoSize = true };
        _scopeNames = new RadioButton { Text = "Names only", AutoSize = true };
        _scopeValues = new RadioButton { Text = "Values only", AutoSize = true };
        var scope = new GroupBox { Text = "Look in", AutoSize = true, Dock = DockStyle.Fill };
        var scopeFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true };
        scopeFlow.Controls.AddRange(new Control[] { _scopeAll, _scopeNames, _scopeValues });
        scope.Controls.Add(scopeFlow);

        _up = new CheckBox { Text = "Search &up", AutoSize = true };
        options.Controls.Add(_up);

        _findNext = new Button { Text = "&Find Next", AutoSize = true, Dock = DockStyle.Top };
        _replaceOne = new Button { Text = "&Replace", AutoSize = true, Dock = DockStyle.Top };
        _replaceAll = new Button { Text = "Replace &All", AutoSize = true, Dock = DockStyle.Top };
        var close = new Button { Text = "Close", AutoSize = true, Dock = DockStyle.Top, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        foreach (var b in new[] { _findNext, _replaceOne, _replaceAll, close })
        {
            b.MinimumSize = new Size(110, 28);
            buttons.Controls.Add(b);
        }

        _status = new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText };

        grid.Controls.Add(new Label { Text = "Fi&nd what:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        grid.Controls.Add(_find, 1, 0);
        grid.Controls.Add(buttons, 2, 0);
        grid.SetRowSpan(buttons, 4);
        grid.Controls.Add(_replaceLabel, 0, 1);
        grid.Controls.Add(_replace, 1, 1);
        var lower = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        lower.Controls.Add(options, 0, 0);
        lower.Controls.Add(scope, 1, 0);
        grid.Controls.Add(lower, 0, 2);
        grid.SetColumnSpan(lower, 2);
        grid.Controls.Add(_status, 0, 3);
        grid.SetColumnSpan(_status, 2);
        Controls.Add(grid);

        _findNext.Click += (_, _) => DoFind();
        _replaceOne.Click += (_, _) => DoReplace();
        _replaceAll.Click += (_, _) => DoReplaceAll();
        close.Click += (_, _) => Hide();
        AcceptButton = _findNext;
        CancelButton = close;
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ReplaceMode
    {
        get => _replace.Visible;
        set
        {
            _replace.Visible = _replaceLabel.Visible = _replaceOne.Visible = _replaceAll.Visible = value;
            Text = value ? "Replace" : "Find";
        }
    }

    public FindOptions? CurrentOptions => string.IsNullOrEmpty(_find.Text) ? null : BuildOptions(backwardsOverride: null);

    public void ShowFor(IWin32Window owner, bool replace, string? initialText)
    {
        ReplaceMode = replace;
        if (!string.IsNullOrEmpty(initialText) && initialText.Length < 200 && !initialText.Contains('\n', StringComparison.Ordinal))
        {
            _find.Text = initialText;
        }
        _status.Text = string.Empty;
        if (!Visible)
        {
            Show(owner);
        }
        Activate();
        _find.Focus();
        _find.SelectAll();
    }

    /// <summary>F3 / Shift+F3 from the main window.</summary>
    public bool FindAgain(bool backwards)
    {
        if (string.IsNullOrEmpty(_find.Text))
        {
            return false;
        }
        DoFind(backwards);
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.F3)
        {
            DoFind(e.Shift);
            e.Handled = true;
        }
    }

    private FindOptions BuildOptions(bool? backwardsOverride) => new()
    {
        Text = _find.Text,
        MatchCase = _matchCase.Checked,
        WholeWord = _wholeWord.Checked,
        UseRegex = _regex.Checked,
        Scope = _scopeNames.Checked ? FindScope.Names : _scopeValues.Checked ? FindScope.Values : FindScope.All,
        Backwards = backwardsOverride ?? _up.Checked,
        Wrap = true,
    };

    private FindOptions? ValidOptions(bool? backwards = null)
    {
        if (string.IsNullOrEmpty(_find.Text))
        {
            _status.Text = "Type something to find.";
            return null;
        }
        var options = BuildOptions(backwards);
        if (FindEngine.Validate(options) is { } error)
        {
            _status.Text = error;
            return null;
        }
        Remember(_find, _findHistory);
        return options;
    }

    private void DoFind(bool? backwards = null)
    {
        if (ValidOptions(backwards) is not { } options)
        {
            return;
        }
        var match = _target.FindNext(options);
        _status.Text = match is null ? $"\"{options.Text}\" was not found." : $"Found in {(match.Value.Field == FindField.Name ? "name" : "value")}.";
        if (match is null)
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
    }

    private void DoReplace()
    {
        if (ValidOptions() is not { } options)
        {
            return;
        }
        Remember(_replace, _replaceHistory);
        _status.Text = _target.ReplaceCurrent(options, _replace.Text) ?? string.Empty;
    }

    private void DoReplaceAll()
    {
        if (ValidOptions() is not { } options)
        {
            return;
        }
        Remember(_replace, _replaceHistory);
        var (replaced, skipped) = _target.ReplaceAll(options, _replace.Text);
        _status.Text = skipped > 0
            ? $"Replaced {replaced:N0} occurrence(s); skipped {skipped:N0} that would be invalid."
            : $"Replaced {replaced:N0} occurrence(s).";
    }

    private static void Remember(ComboBox combo, List<string> history)
    {
        var text = combo.Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        history.Remove(text);
        history.Insert(0, text);
        if (history.Count > 20)
        {
            history.RemoveAt(history.Count - 1);
        }
        combo.Items.Clear();
        combo.Items.AddRange(history.Cast<object>().ToArray());
        combo.Text = text;
    }
}
