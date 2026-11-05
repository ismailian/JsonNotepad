using System.Drawing;
using System.Drawing.Imaging;

namespace JsonNotepad.Tools.IconGen;

/// <summary>
/// Writes a multi-resolution Windows .ico container by hand.
/// Frames below 256px are stored as classic 32-bit BMP/DIB entries (maximum compatibility with
/// GDI, the resource compiler and System.Drawing.Icon); the 256px frame is PNG-compressed (Vista+ convention).
/// </summary>
internal static class IcoWriter
{
    private const int PngThreshold = 256;

    public static void Write(string path, IReadOnlyList<Bitmap> frames)
    {
        var payloads = frames.Select(f => f.Width >= PngThreshold ? EncodePng(f) : EncodeDib(f)).ToList();

        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);

        // ICONDIR
        w.Write((ushort)0);            // reserved
        w.Write((ushort)1);            // type: icon
        w.Write((ushort)frames.Count); // image count

        var offset = 6 + (16 * frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            // ICONDIRENTRY
            w.Write((byte)(f.Width >= 256 ? 0 : f.Width));
            w.Write((byte)(f.Height >= 256 ? 0 : f.Height));
            w.Write((byte)0);     // palette colour count
            w.Write((byte)0);     // reserved
            w.Write((ushort)1);   // colour planes
            w.Write((ushort)32);  // bits per pixel
            w.Write(payloads[i].Length);
            w.Write(offset);
            offset += payloads[i].Length;
        }

        foreach (var p in payloads)
        {
            w.Write(p);
        }
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>BITMAPINFOHEADER + bottom-up BGRA XOR bitmap + 1bpp AND mask.</summary>
    private static byte[] EncodeDib(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var maskStride = ((width + 31) / 32) * 4;

        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[data.Stride * height];
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(40);              // biSize
        w.Write(width);           // biWidth
        w.Write(height * 2);      // biHeight (XOR + AND)
        w.Write((ushort)1);       // biPlanes
        w.Write((ushort)32);      // biBitCount
        w.Write(0);               // biCompression = BI_RGB
        w.Write((width * 4 * height) + (maskStride * height)); // biSizeImage
        w.Write(0);               // biXPelsPerMeter
        w.Write(0);               // biYPelsPerMeter
        w.Write(0);               // biClrUsed
        w.Write(0);               // biClrImportant

        // XOR bitmap, bottom-up, BGRA (Format32bppArgb is already BGRA in memory).
        for (var y = height - 1; y >= 0; y--)
        {
            w.Write(pixels, y * data.Stride, width * 4);
        }

        // AND mask, bottom-up: 1 = transparent.
        var row = new byte[maskStride];
        for (var y = height - 1; y >= 0; y--)
        {
            Array.Clear(row);
            for (var x = 0; x < width; x++)
            {
                var alpha = pixels[(y * data.Stride) + (x * 4) + 3];
                if (alpha == 0)
                {
                    row[x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }

            w.Write(row);
        }

        w.Flush();
        return ms.ToArray();
    }
}
