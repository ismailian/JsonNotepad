namespace JsonNotepad.App.Forms;

/// <summary>Read-only two-column report (statistics, keyboard shortcuts) with copy support.</summary>
internal sealed class ReportForm : DialogBase
{
    public ReportForm(string title, string firstColumn, string secondColumn, IEnumerable<(string Name, string Value)> rows, Size? size = null)
    {
        Text = title;
        ClientSize = size ?? new Size(520, 560);
        MinimizeBox = false;

        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            GridLines = true,
        };
        list.Columns.Add(firstColumn, 300);
        list.Columns.Add(secondColumn, 180);
        foreach (var (name, value) in rows)
        {
            if (name.Length == 0)
            {
                list.Items.Add(new ListViewItem(string.Empty));
                continue;
            }
            list.Items.Add(new ListViewItem(new[] { name, value }));
        }
        list.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                CopyAll(list);
                e.Handled = true;
            }
        };

        var copy = new Button { Text = "Copy" };
        copy.Click += (_, _) => CopyAll(list);
        var close = new Button { Text = "Close", DialogResult = DialogResult.OK };
        Controls.Add(list);
        Controls.Add(CreateButtonBar(close, copy));
        AcceptButton = close;
        CancelButton = close;
        Shown += (_, _) => list.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
    }

    private static void CopyAll(ListView list)
    {
        var lines = list.Items.Cast<ListViewItem>().Select(i => i.SubItems.Count > 1 ? $"{i.Text}\t{i.SubItems[1].Text}" : i.Text);
        Clipboard.SetText(string.Join(Environment.NewLine, lines));
    }
}
