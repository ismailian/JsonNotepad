using System.Globalization;
using System.Text;

namespace JsonNotepad.App.Infrastructure;

/// <summary>Minimal append-only file log (no logging framework needed for a desktop editor).</summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static string CurrentFile => Path.Combine(AppPaths.LogDirectory, $"jsonnotepad-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warning(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : message + Environment.NewLine + ex);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                var line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
                File.AppendAllText(CurrentFile, line, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
