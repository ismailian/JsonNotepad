using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace JsonNotepad.Controls.Icons;

/// <summary>
/// Renders toolbar/menu icons from the Windows icon fonts (Segoe Fluent Icons on
/// Windows 11, Segoe MDL2 Assets on Windows 10). This gives crisp, DPI-correct,
/// theme-colored icons with zero image assets and zero packages.
/// </summary>
public static class GlyphIcons
{
    // Code points shared by Segoe Fluent Icons and Segoe MDL2 Assets.
    public static readonly string New = G(0xE8A5);
    public static readonly string Open = G(0xE8E5);
    public static readonly string Save = G(0xE74E);
    public static readonly string SaveAs = G(0xE792);
    public static readonly string Undo = G(0xE7A7);
    public static readonly string Redo = G(0xE7A6);
    public static readonly string Cut = G(0xE8C6);
    public static readonly string Copy = G(0xE8C8);
    public static readonly string Paste = G(0xE77F);
    public static readonly string Delete = G(0xE74D);
    public static readonly string Up = G(0xE74A);
    public static readonly string Down = G(0xE74B);
    public static readonly string Left = G(0xE72B);
    public static readonly string Right = G(0xE72A);
    public static readonly string ExpandAll = G(0xE710);
    public static readonly string CollapseAll = G(0xE738);
    public static readonly string Find = G(0xE721);
    public static readonly string Refresh = G(0xE72C);
    public static readonly string Settings = G(0xE713);
    public static readonly string Error = G(0xEA39);
    public static readonly string Warning = G(0xE7BA);
    public static readonly string Info = G(0xE946);
    public static readonly string Help = G(0xE897);
    public static readonly string Rename = G(0xE8AC);
    public static readonly string Validate = G(0xE73E);
    public static readonly string Sort = G(0xE8CB);
    public static readonly string Statistics = G(0xE9D2);
    public static readonly string Duplicate = G(0xE8F4);
    public static readonly string NewWindow = G(0xE78B);

    private static readonly Lazy<string?> FontName = new(DetectFont);
    private static readonly Dictionary<(string, int, int), Bitmap> Cache = new();

    public static bool IsAvailable => FontName.Value is not null;

    /// <summary>Renders <paramref name="glyph"/> into a cached bitmap of <paramref name="size"/> pixels.</summary>
    public static Bitmap? Render(string glyph, int size, Color color)
    {
        if (FontName.Value is null)
        {
            return null;
        }
        var key = (glyph, size, color.ToArgb());
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font(FontName.Value, size * 0.72f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
            }
            Cache[key] = bmp;
            return bmp;
        }
    }

    private static string G(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static string? DetectFont()
    {
        using var fonts = new InstalledFontCollection();
        var names = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Contains("Segoe Fluent Icons"))
        {
            return "Segoe Fluent Icons";
        }
        return names.Contains("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : null;
    }
}
