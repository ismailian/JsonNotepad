using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace JsonNotepad.Tools.IconGen;

/// <summary>
/// Draws the JsonNotepad logo procedurally with GDI+: a rounded square with a blue to teal
/// diagonal gradient, a soft inner highlight, white curly braces and a small tree glyph
/// (three nodes joined by tree-view style connectors) between them.
/// Detail is reduced at small pixel sizes so the mark stays legible at 16px.
/// </summary>
internal static class LogoRenderer
{
    private static readonly Color GradientStart = Color.FromArgb(0x24, 0x4C, 0xDB); // deep blue
    private static readonly Color GradientMid = Color.FromArgb(0x1C, 0x84, 0xD8);   // azure
    private static readonly Color GradientEnd = Color.FromArgb(0x0F, 0xB5, 0xA0);   // teal

    /// <summary>Renders the logo filling a <paramref name="size"/> x <paramref name="size"/> bitmap.</summary>
    /// <param name="size">Bitmap edge in pixels.</param>
    /// <param name="paddingFraction">Transparent margin around the rounded square, as a fraction of the size.</param>
    public static Bitmap RenderSquare(int size, float paddingFraction = 0f)
    {
        var bitmap = NewBitmap(size, size);
        using var g = CreateGraphics(bitmap);
        var pad = (float)Math.Round(size * paddingFraction);
        DrawLogo(g, new RectangleF(pad, pad, size - (2 * pad), size - (2 * pad)));
        return bitmap;
    }

    /// <summary>Renders the logo centered on a transparent canvas (tiles, splash screen).</summary>
    public static Bitmap RenderCentered(int width, int height, int logoSize)
    {
        var bitmap = NewBitmap(width, height);
        using var g = CreateGraphics(bitmap);
        var x = (float)Math.Round((width - logoSize) / 2f);
        var y = (float)Math.Round((height - logoSize) / 2f);
        DrawLogo(g, new RectangleF(x, y, logoSize, logoSize));
        return bitmap;
    }

    private static Bitmap NewBitmap(int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        bitmap.SetResolution(96, 96);
        return bitmap;
    }

    private static Graphics CreateGraphics(Bitmap bitmap)
    {
        var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        return g;
    }

    private static void DrawLogo(Graphics g, RectangleF r)
    {
        var s = r.Width;
        DrawBackground(g, r);

        if (s <= 24)
        {
            // Tiny sizes: just heavy braces, spaced wide, pixel-friendly stroke.
            var stroke = Math.Max(2f, s * 0.125f);
            DrawBraces(g, r, spineInset: 0.315f, height: 0.62f, armWidth: 0.11f, stroke);
            if (s >= 20)
            {
                // A single centre dot hints at the "content" between the braces.
                FillDot(g, r.X + (s * 0.5f), r.Y + (s * 0.5f), Math.Max(1.4f, s * 0.07f));
            }

            return;
        }

        if (s <= 48)
        {
            // Small sizes: bolder braces and a chunkier, simpler tree.
            var stroke = s * 0.085f;
            DrawBraces(g, r, spineInset: 0.27f, height: 0.58f, armWidth: 0.09f, stroke);
            DrawTree(g, r, nodeRadius: 0.058f, lineWidth: 0.05f, compact: true);
            return;
        }

        DrawBraces(g, r, spineInset: 0.235f, height: 0.56f, armWidth: 0.085f, s * 0.07f);
        DrawTree(g, r, nodeRadius: 0.047f, lineWidth: 0.034f, compact: false);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawBackground(Graphics g, RectangleF r)
    {
        var s = r.Width;
        var radius = s <= 24 ? s * 0.2f : s * 0.22f;
        using var shape = RoundedRect(r, radius);

        // Diagonal gradient: top-left blue to bottom-right teal.
        using (var brush = new LinearGradientBrush(
            new PointF(r.Left, r.Top), new PointF(r.Right, r.Bottom), GradientStart, GradientEnd))
        {
            brush.InterpolationColors = new ColorBlend
            {
                Colors = [GradientStart, GradientMid, GradientEnd],
                Positions = [0f, 0.5f, 1f],
            };
            g.FillPath(brush, shape);
        }

        if (s < 32)
        {
            return; // highlights only add mud at tiny sizes
        }

        var oldClip = g.Clip;
        g.SetClip(shape, CombineMode.Intersect);

        // Soft top highlight (glass sheen fading to nothing at mid height).
        var sheen = new RectangleF(r.X, r.Y, s, s * 0.55f);
        using (var sheenBrush = new LinearGradientBrush(
            sheen, Color.FromArgb(18, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
        {
            g.FillRectangle(sheenBrush, sheen.X, sheen.Y, sheen.Width, sheen.Height - 1);
        }

        // Gentle darkening toward the bottom for depth.
        var shade = new RectangleF(r.X, r.Y + (s * 0.55f), s, s * 0.45f);
        using (var shadeBrush = new LinearGradientBrush(
            shade, Color.FromArgb(0, 0, 20, 60), Color.FromArgb(40, 0, 20, 60), LinearGradientMode.Vertical))
        {
            g.FillRectangle(shadeBrush, shade.X, shade.Y + 1, shade.Width, shade.Height);
        }

        g.Clip = oldClip;
        oldClip.Dispose();

        // Thin inner rim light.
        var inset = Math.Max(0.75f, s * 0.012f);
        var rimRect = RectangleF.Inflate(r, -inset, -inset);
        using var rim = RoundedRect(rimRect, radius - inset);
        using var rimPen = new Pen(Color.FromArgb(s >= 64 ? 30 : 20, 255, 255, 255), Math.Max(1f, s * 0.01f));
        g.DrawPath(rimPen, rim);
    }

    /// <summary>Draws a mirrored pair of curly braces.</summary>
    /// <param name="spineInset">Distance of each brace's vertical spine from the side, as a fraction of size.</param>
    /// <param name="height">Brace height as a fraction of size.</param>
    /// <param name="armWidth">Horizontal reach of the arms and the tip from the spine, as a fraction of size.</param>
    private static void DrawBraces(Graphics g, RectangleF r, float spineInset, float height, float armWidth, float stroke)
    {
        var s = r.Width;
        var top = r.Y + (s * (1 - height) / 2);
        var bottom = top + (s * height);
        var a = s * armWidth;
        var pixelSnap = s <= 48;
        if (pixelSnap)
        {
            stroke = Math.Max(1f, MathF.Round(stroke));
            top = MathF.Round(top);
            bottom = MathF.Round(bottom);
        }

        using var shadowPen = MakePen(Color.FromArgb(s >= 48 ? 45 : 0, 0, 25, 70), stroke);
        using var pen = MakePen(Color.White, stroke);

        foreach (var leftSide in new[] { true, false })
        {
            var spine = leftSide ? r.X + (s * spineInset) : r.Right - (s * spineInset);
            if (pixelSnap)
            {
                spine = Snap(spine, stroke);
            }

            var dir = leftSide ? 1f : -1f; // arms point toward the centre, tip points outward
            using var path = BracePath(spine, top, bottom, a * dir);

            if (shadowPen.Color.A > 0)
            {
                using var shadow = (GraphicsPath)path.Clone();
                using var m = new Matrix();
                m.Translate(0, s * 0.018f);
                shadow.Transform(m);
                g.DrawPath(shadowPen, shadow);
            }

            g.DrawPath(pen, path);
        }
    }

    private static GraphicsPath BracePath(float x, float top, float bottom, float a)
    {
        var mid = (top + bottom) / 2;
        var k = Math.Abs(a) * 1.05f; // vertical length of each curl
        var path = new GraphicsPath();

        // Top arm curling into the spine.
        path.AddBezier(x + a, top, x + (a * 0.15f), top, x, top + (k * 0.2f), x, top + k);
        path.AddLine(x, top + k, x, mid - k);
        // Spine into the outward tip and back.
        path.AddBezier(x, mid - k, x, mid - (k * 0.25f), x - (a * 0.35f), mid, x - a, mid);
        path.AddBezier(x - a, mid, x - (a * 0.35f), mid, x, mid + (k * 0.25f), x, mid + k);
        path.AddLine(x, mid + k, x, bottom - k);
        // Bottom arm.
        path.AddBezier(x, bottom - k, x, bottom - (k * 0.2f), x + (a * 0.15f), bottom, x + a, bottom);
        return path;
    }

    /// <summary>
    /// Tree-view glyph: a root node with a vertical connector and two child nodes joined by elbows,
    /// echoing the outline tree of XML Notepad.
    /// </summary>
    private static void DrawTree(Graphics g, RectangleF r, float nodeRadius, float lineWidth, bool compact)
    {
        var s = r.Width;
        float P(float f) => f * s;

        var rootX = r.X + P(compact ? 0.43f : 0.455f);
        var rootY = r.Y + P(compact ? 0.37f : 0.385f);
        var childX = r.X + P(compact ? 0.585f : 0.578f);
        var child1Y = r.Y + P(0.5f);
        var child2Y = r.Y + P(compact ? 0.63f : 0.615f);
        var penWidth = P(lineWidth);

        if (compact)
        {
            // Align connectors to the pixel grid so they stay crisp at 32-48px.
            penWidth = Math.Max(1f, MathF.Round(penWidth));
            rootX = Snap(rootX, penWidth);
            childX = Snap(childX, penWidth);
            rootY = Snap(rootY, penWidth);
            child1Y = Snap(child1Y, penWidth);
            child2Y = Snap(child2Y, penWidth);
        }

        using var linePen = new Pen(Color.FromArgb(235, 255, 255, 255), penWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        using (var connectors = new GraphicsPath())
        {
            connectors.AddLine(rootX, rootY, rootX, child2Y);
            connectors.AddLine(rootX, child2Y, childX, child2Y);
            connectors.StartFigure();
            connectors.AddLine(rootX, child1Y, childX, child1Y);
            g.DrawPath(linePen, connectors);
        }

        FillDot(g, rootX, rootY, P(nodeRadius * 1.15f));
        FillDot(g, childX, child1Y, P(nodeRadius));
        FillDot(g, childX, child2Y, P(nodeRadius));
    }

    /// <summary>Places a stroke centre so a line of the given width covers whole pixels.</summary>
    private static float Snap(float coordinate, float width) =>
        ((int)MathF.Round(width) % 2 == 1) ? MathF.Floor(coordinate) + 0.5f : MathF.Round(coordinate);

    private static void FillDot(Graphics g, float cx, float cy, float radius)
    {
        using var brush = new SolidBrush(Color.White);
        g.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);
    }

    private static Pen MakePen(Color color, float width) => new(color, width)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round,
    };
}
