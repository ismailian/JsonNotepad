using JsonNotepad.App.Settings;

namespace JsonNotepad.App.Forms;

/// <summary>Options dialog: a categorized PropertyGrid over a copy of the settings (like XML Notepad).</summary>
internal sealed class OptionsForm : DialogBase
{
    private readonly PropertyGrid _grid;

    public OptionsForm(AppSettings settings)
    {
        Text = "Options";
        ClientSize = new Size(560, 600);
        MinimumSize = new Size(420, 400);
        Result = settings.Clone();

        _grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = Result,
            PropertySort = PropertySort.Categorized,
            ToolbarVisible = false,
            HelpVisible = true,
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var reset = new Button { Text = "Reset to defaults" };
        reset.Click += (_, _) =>
        {
            var fresh = new AppSettings
            {
                Window = Result.Window,
                RecentFiles = Result.RecentFiles,
                LastDirectory = Result.LastDirectory,
                NameColumnWidth = Result.NameColumnWidth,
                ErrorListHeight = Result.ErrorListHeight,
            };
            Result = fresh;
            _grid.SelectedObject = Result;
        };

        var bar = CreateButtonBar(cancel, ok);
        bar.Controls.Add(reset);
        Controls.Add(_grid);
        Controls.Add(bar);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public AppSettings Result { get; private set; }
}
