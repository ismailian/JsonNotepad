using JsonNotepad.App.Forms;
using JsonNotepad.App.Infrastructure;
using JsonNotepad.App.Settings;

namespace JsonNotepad.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => CrashReporter.Report(e.Exception, fatal: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                CrashReporter.Report(ex, fatal: true);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();

        var settings = SettingsStore.Load();
#pragma warning disable WFO5001 // Dark mode is experimental in .NET 9 WinForms; opt-in via settings.
        Application.SetColorMode(settings.Theme switch
        {
            AppTheme.Dark => SystemColorMode.Dark,
            AppTheme.Light => SystemColorMode.Classic,
            _ => SystemColorMode.System,
        });
#pragma warning restore WFO5001

        var file = args.FirstOrDefault(a => !a.StartsWith('-') && !a.StartsWith('/'));
        Log.Info($"Starting {AppInfo.ProductName} {AppInfo.FullVersion} on {AppInfo.RuntimeDescription}");
        Application.Run(new MainForm(settings, file));
    }
}
