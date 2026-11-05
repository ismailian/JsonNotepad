using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace JsonNotepad.Tools.IconGen;

/// <summary>
/// Generates every branding asset of JsonNotepad from the procedural logo.
/// Usage: IconGen [outputRoot]   (default: the repository root, located by walking up to JsonNotepad.sln)
/// </summary>
internal static class Program
{
    private static readonly int[] IcoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    private static readonly int[] TargetSizes = [16, 24, 32, 48, 256];

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] is "-h" or "--help" or "/?")
            {
                Console.WriteLine("Usage: IconGen [outputRoot]");
                Console.WriteLine("  outputRoot  Folder that receives src/, installer/ and docs/ outputs.");
                Console.WriteLine("              Defaults to the repository root (the folder containing JsonNotepad.sln).");
                return 0;
            }

            var root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
            Console.WriteLine($"Output root: {root}");
            Generate(root);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ExternalException)
        {
            Console.Error.WriteLine($"IconGen failed: {ex.Message}");
            return 1;
        }
    }

    private static void Generate(string root)
    {
        // Application icon.
        var icoPath = Path.Combine(root, "src", "JsonNotepad.App", "Resources", "AppIcon.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(icoPath)!);
        var frames = IcoSizes.Select(s => LogoRenderer.RenderSquare(s, IconPadding(s))).ToList();
        try
        {
            IcoWriter.Write(icoPath, frames);
        }
        finally
        {
            frames.ForEach(f => f.Dispose());
        }

        Report(icoPath);

        // MSIX visual assets.
        var assets = Path.Combine(root, "installer", "msix", "Assets");
        Save(LogoRenderer.RenderSquare(44, IconPadding(44)), assets, "Square44x44Logo.png");
        foreach (var size in TargetSizes)
        {
            Save(LogoRenderer.RenderSquare(size, IconPadding(size)), assets, $"Square44x44Logo.targetsize-{size}_altform-unplated.png");
        }

        Save(LogoRenderer.RenderSquare(50, IconPadding(50)), assets, "StoreLogo.png");
        Save(LogoRenderer.RenderCentered(71, 71, 48), assets, "SmallTile", "Square71x71Logo.png");
        Save(LogoRenderer.RenderCentered(150, 150, 96), assets, "Square150x150Logo.png");
        Save(LogoRenderer.RenderCentered(310, 150, 96), assets, "Wide310x150Logo.png");
        Save(LogoRenderer.RenderCentered(310, 310, 192), assets, "LargeTile", "Square310x310Logo.png");
        Save(LogoRenderer.RenderCentered(620, 300, 192), assets, "SplashScreen.png");

        // README logo.
        Save(LogoRenderer.RenderSquare(256), Path.Combine(root, "docs"), "images", "logo.png");
    }

    /// <summary>Small icons are full bleed; larger ones get the customary ~1/32 margin.</summary>
    private static float IconPadding(int size) => size < 32 ? 0f : 1f / 32f;

    private static void Save(Bitmap bitmap, string folder, params string[] relative)
    {
        using (bitmap)
        {
            var path = Path.Combine([folder, .. relative]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bitmap.Save(path, ImageFormat.Png);
            Report(path);
        }
    }

    private static void Report(string path) =>
        Console.WriteLine($"  wrote {path} ({new FileInfo(path).Length:N0} bytes)");

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "JsonNotepad.sln")))
                {
                    return dir.FullName;
                }
            }
        }

        throw new InvalidOperationException("Could not locate JsonNotepad.sln above the current or executable directory; pass the output root explicitly.");
    }
}
