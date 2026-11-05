using JsonNotepad.App.Infrastructure;

namespace JsonNotepad.App.Forms;

/// <summary>Common look for dialogs: DPI-aware, no taskbar entry, standard padding and icon.</summary>
internal class DialogBase : Form
{
    public DialogBase()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = SystemFonts.MessageBoxFont ?? Font;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.Sizable;
        Padding = new Padding(10);
        Icon = AppInfo.LoadIcon();
    }

    protected static FlowLayoutPanel CreateButtonBar(params Button[] buttons)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = false,
        };
        foreach (var b in buttons)
        {
            b.AutoSize = true;
            b.MinimumSize = new Size(88, 28);
            bar.Controls.Add(b);
        }
        return bar;
    }
}
