using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

/// <summary>
/// Entry point of the self-contained test runner.
/// </summary>
/// <remarks>
/// Usage: <c>dotnet run --project tests/JsonNotepad.Core.Tests -c Release -- [options] [filter...]</c>
/// <list type="bullet">
/// <item>Any plain argument is a case-insensitive substring of <c>Class.Method</c>; tests matching any filter run.</item>
/// <item><c>--category NAME</c> runs only tests in that category; <c>--exclude-category NAME</c> skips them (repeatable).</item>
/// <item><c>--list</c> prints the selected tests without running them.</item>
/// </list>
/// Exit code: 0 when every selected test passed, 1 on any failure or when nothing matched.
/// </remarks>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var runner = new TestRunner(Console.Out);
        return await runner.RunAsync(typeof(Program).Assembly, args).ConfigureAwait(false);
    }
}

/// <summary>Discovers [Test] methods by reflection, runs them one by one and reports.</summary>
public sealed class TestRunner(TextWriter output)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(2);

    public sealed record TestCase(Type Type, MethodInfo Method, IReadOnlyList<string> Categories)
    {
        public string FullName => Type.Name + "." + Method.Name;
    }

    public async Task<int> RunAsync(Assembly assembly, string[] args)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(args);

        var filters = new List<string>();
        var include = new List<string>();
        var exclude = new List<string>();
        bool listOnly = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--category" when i + 1 < args.Length:
                    include.Add(args[++i]);
                    break;
                case "--exclude-category" when i + 1 < args.Length:
                    exclude.Add(args[++i]);
                    break;
                case "--list":
                    listOnly = true;
                    break;
                default:
                    filters.Add(args[i]);
                    break;
            }
        }

        var tests = Discover(assembly)
            .Where(t => filters.Count == 0 || filters.Exists(f => t.FullName.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Where(t => include.Count == 0 || t.Categories.Any(c => include.Contains(c, StringComparer.OrdinalIgnoreCase)))
            .Where(t => !t.Categories.Any(c => exclude.Contains(c, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        if (tests.Count == 0)
        {
            WriteColored(ConsoleColor.Yellow, "No tests matched the given filter.");
            output.WriteLine();
            return 1;
        }

        if (listOnly)
        {
            foreach (var t in tests)
            {
                output.WriteLine(t.Categories.Count > 0 ? $"{t.FullName}  [{string.Join(", ", t.Categories)}]" : t.FullName);
            }
            output.WriteLine($"{tests.Count} test(s).");
            return 0;
        }

        output.WriteLine($"Running {tests.Count} test(s) from {assembly.GetName().Name}");
        output.WriteLine();

        var total = Stopwatch.StartNew();
        var failures = new List<(TestCase Test, Exception Error)>();
        int passed = 0;
        foreach (var test in tests)
        {
            var log = TestContext.BeginCapture();
            var sw = Stopwatch.StartNew();
            var error = await RunOneAsync(test).ConfigureAwait(false);
            sw.Stop();
            TestContext.EndCapture();

            string elapsed = FormatElapsed(sw.Elapsed);
            if (error is null)
            {
                passed++;
                WriteColored(ConsoleColor.Green, "  PASS ");
                output.WriteLine($" {test.FullName} ({elapsed})");
            }
            else
            {
                failures.Add((test, error));
                WriteColored(ConsoleColor.Red, "  FAIL ");
                output.WriteLine($" {test.FullName} ({elapsed})");
                WriteColored(ConsoleColor.DarkRed, Indent(Describe(error), "         "));
                output.WriteLine();
            }
            foreach (var line in log)
            {
                WriteColored(ConsoleColor.DarkGray, Indent(line, "         "));
                output.WriteLine();
            }
        }
        total.Stop();

        output.WriteLine();
        if (failures.Count > 0)
        {
            WriteColored(ConsoleColor.Red, "Failed tests:");
            output.WriteLine();
            foreach (var (test, _) in failures)
            {
                output.WriteLine("  " + test.FullName);
            }
            output.WriteLine();
        }

        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"Total: {tests.Count}, Passed: {passed}, Failed: {failures.Count}, Time: {total.Elapsed.TotalSeconds:F2} s");
        WriteColored(failures.Count == 0 ? ConsoleColor.Green : ConsoleColor.Red, summary);
        output.WriteLine();
        return failures.Count == 0 ? 0 : 1;
    }

    public static List<TestCase> Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var result = new List<TestCase>();
        foreach (var type in assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && !t.ContainsGenericParameters)
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var classCategories = type.GetCustomAttributes<CategoryAttribute>().Select(c => c.Name).ToList();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
                .OrderBy(m => m.MetadataToken);
            foreach (var m in methods)
            {
                if (m.GetParameters().Length != 0 || m.IsGenericMethodDefinition ||
                    !(m.ReturnType == typeof(void) || m.ReturnType == typeof(Task)))
                {
                    throw new InvalidOperationException($"{type.Name}.{m.Name}: [Test] methods must be parameterless and return void or Task.");
                }
                var categories = classCategories
                    .Concat(m.GetCustomAttributes<CategoryAttribute>().Select(c => c.Name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                result.Add(new TestCase(type, m, categories));
            }
        }
        return result;
    }

    /// <summary>Runs one test on a fresh instance; returns the failure or null.</summary>
    private static async Task<Exception?> RunOneAsync(TestCase test)
    {
        object? instance = null;
        Exception? error = null;
        try
        {
            instance = Activator.CreateInstance(test.Type);
            var returned = test.Method.Invoke(instance, null);
            if (returned is Task task)
            {
                await task.WaitAsync(TestTimeout).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            error = Unwrap(ex);
        }
        finally
        {
            if (instance is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    error ??= new InvalidOperationException("Dispose failed: " + ex.Message, ex);
                }
            }
        }
        return error;
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: { } inner })
        {
            ex = inner;
        }
        return ex;
    }

    private static string Describe(Exception ex)
    {
        var header = ex is AssertionException ? ex.Message : $"{ex.GetType().FullName}: {ex.Message}";
        var frames = (ex.StackTrace ?? string.Empty)
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Contains("JsonNotepad.", StringComparison.Ordinal) && !l.Contains(".Framework.", StringComparison.Ordinal))
            .Take(8);
        var text = header + "\n" + string.Join("\n", frames);
        if (ex.InnerException is { } inner && ex is not AssertionException)
        {
            text += "\n  ---> " + inner.GetType().Name + ": " + inner.Message;
        }
        return text.TrimEnd();
    }

    private static string Indent(string text, string prefix) =>
        string.Join("\n", text.Split('\n').Select(l => prefix + l.TrimEnd('\r')));

    private static string FormatElapsed(TimeSpan t) => t.TotalMilliseconds < 1000
        ? t.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms"
        : t.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s";

    private void WriteColored(ConsoleColor color, string text)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        try
        {
            output.Write(text);
            output.Flush();
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }
}
