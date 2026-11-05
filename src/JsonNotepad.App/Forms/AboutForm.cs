using JsonNotepad.App.Infrastructure;

namespace JsonNotepad.App.Forms;

internal sealed class AboutForm : DialogBase
{
    public AboutForm()
    {
        Text = "About " + AppInfo.ProductName;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(460, 300);

        var icon = new PictureBox
        {
            Image = AppInfo.LoadIcon() is { } ico ? new Icon(ico, 64, 64).ToBitmap() : null,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(64, 64),
            Location = new Point(16, 16),
        };
        var title = new Label
        {
            Text = AppInfo.ProductName,
            Font = new Font(Font.FontFamily, 16f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(96, 16),
        };
        var details = new Label
        {
            Text = $"Version {AppInfo.Version}\n{AppInfo.Description}\n\n" +
                   $"Runtime: {AppInfo.RuntimeDescription}\n" +
                   $"{(AppInfo.IsPackaged ? "Installed from MSIX package" : "Desktop installation")}\n\n" +
                   $"{AppInfo.Copyright}\n" +
                   "Inspired by Microsoft XML Notepad (MIT License).",
            AutoSize = false,
            Location = new Point(96, 52),
            Size = new Size(350, 180),
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        Controls.Add(icon);
        Controls.Add(title);
        Controls.Add(details);
        Controls.Add(CreateButtonBar(ok));
        AcceptButton = ok;
        CancelButton = ok;
    }
}
