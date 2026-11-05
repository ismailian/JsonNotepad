using System.Diagnostics;
using System.Reflection;

namespace JsonNotepad.App.Infrastructure;

/// <summary>Product metadata derived from assembly attributes (generated from Directory.Build.props).</summary>
internal static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    public static string ProductName => Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "JsonNotepad";

    public static string Copyright => Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    public static string Description => Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty;

    /// <summary>Semantic version, e.g. "1.2.0" or "1.2.0-beta.1" (without build metadata).</summary>
    public static string Version
    {
        get
        {
            var info = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                       ?? Assembly.GetName().Version?.ToString(3)
                       ?? "0.0.0";
            int plus = info.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? info[..plus] : info;
        }
    }

    /// <summary>Full informational version including the commit hash when built from git.</summary>
    public static string FullVersion =>
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Version;

    public static string RuntimeDescription => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    public static bool IsPackaged
    {
        get
        {
            try
            {
                return Environment.ProcessPath?.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) == true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public static Icon? LoadIcon()
    {
        using var stream = Assembly.GetManifestResourceStream("JsonNotepad.AppIcon.ico");
        return stream is null ? null : new Icon(stream);
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Warning($"Could not open {url}: {ex.Message}");
        }
    }
}

internal static class AppPaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JsonNotepad");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
}
