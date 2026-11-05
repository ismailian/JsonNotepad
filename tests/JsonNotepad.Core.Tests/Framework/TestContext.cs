namespace JsonNotepad.Core.Tests.Framework;

/// <summary>Collects informational output of the running test; the runner prints it under the result line.</summary>
public static class TestContext
{
    private static readonly AsyncLocal<List<string>?> Current = new();

    public static void Log(string message)
    {
        var sink = Current.Value;
        if (sink is null)
        {
            Console.WriteLine(message);
            return;
        }
        lock (sink)
        {
            sink.Add(message);
        }
    }

    internal static List<string> BeginCapture()
    {
        var list = new List<string>();
        Current.Value = list;
        return list;
    }

    internal static void EndCapture() => Current.Value = null;
}
