using System.Drawing.Drawing2D;
using System.Drawing.Text;
using JsonNotepad.Controls.Theming;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Controls.Icons;

/// <summary>
/// Draws the small type badges shown before each tree label ({} [] "" # ✓ ∅),
/// the JSON equivalent of XML Notepad's element/attribute/text icons. Bitmaps are
/// cached per (kind, size, theme) so painting a row is a single DrawImage.
/// </summary>
public static class NodeKindIcons
{
    private static readonly Dictionary<(JNodeKind, int, bool), Bitmap> Cache = new();

    public static Bitmap Get(JNodeKind kind, int size, Theme theme)
    {
        var key = (kind, size, theme.IsDark);
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var bmp))
            {
                bmp = Draw(kind, size, theme);
                Cache[key] = bmp;
            }
            return bmp;
        }
    }

    private static Bitmap Draw(JNodeKind kind, int size, Theme theme)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var color = theme.BadgeFor(kind);
        var rect = new RectangleF(0.5f, 0.5f, size - 1.5f, size - 1.5f);
        float radius = Math.Max(2f, size / 4.5f);
        using (var path = RoundedRect(rect, radius))
        using (var fill = new SolidBrush(Color.FromArgb(theme.IsDark ? 70 : 38, color)))
        using (var pen = new Pen(Color.FromArgb(theme.IsDark ? 200 : 170, color), Math.Max(1f, size / 16f)))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        string text = kind switch
        {
            JNodeKind.Object => "{}",
            JNodeKind.Array => "[]",
            JNodeKind.String => "ab",
            JNodeKind.Number => "#",
            JNodeKind.Boolean => "b",
            _ => "ø",
        };
        float fontSize = text.Length > 1 ? size * 0.50f : size * 0.62f;
        using var font = new Font("Consolas", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, new RectangleF(0, 0.5f, size, size), format);
        return bmp;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
