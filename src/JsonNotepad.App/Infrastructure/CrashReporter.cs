namespace JsonNotepad.App.Infrastructure;

/// <summary>Logs unexpected exceptions and tells the user, without losing their work silently.</summary>
internal static class CrashReporter
{
    private static bool _showing;

    public static void Report(Exception ex, bool fatal)
    {
        Log.Error(fatal ? "Fatal unhandled exception" : "Unhandled exception", ex);
        if (_showing)
        {
            return;
        }
        _showing = true;
        try
        {
            var text = $"{(fatal ? "JsonNotepad has to close because of an unexpected error." : "An unexpected error occurred.")}\n\n" +
                       $"{ex.GetType().Name}: {ex.Message}\n\nDetails were written to:\n{Log.CurrentFile}";
            MessageBox.Show(text, AppInfo.ProductName, MessageBoxButtons.OK, fatal ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
        }
        finally
        {
            _showing = false;
        }
    }
}
