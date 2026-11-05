using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace JsonNotepad.Core.Tests.Framework;

/// <summary>Minimal assertion library. Every failure throws <see cref="AssertionException"/>.</summary>
public static class Assert
{
    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
        {
            return;
        }
        var sb = new StringBuilder();
        sb.Append("Assert.Equal failed.");
        AppendUserMessage(sb, message);
        sb.Append("\n  Expected: ").Append(Format(expected));
        sb.Append("\n  Actual:   ").Append(Format(actual));
        if (expected is string e && actual is string a)
        {
            int i = FirstDifference(e, a);
            sb.Append(CultureInfo.InvariantCulture, $"\n  First difference at index {i}: expected {DescribeCharAt(e, i)}, actual {DescribeCharAt(a, i)}");
        }
        throw new AssertionException(sb.ToString());
    }

    public static void NotEqual<T>(T notExpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
        {
            throw new AssertionException($"Assert.NotEqual failed.{UserMessage(message)}\n  Both values: {Format(actual)}");
        }
    }

    public static void True([DoesNotReturnIf(false)] bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new AssertionException($"Assert.True failed.{UserMessage(message)}");
        }
    }

    public static void False([DoesNotReturnIf(true)] bool condition, string? message = null)
    {
        if (condition)
        {
            throw new AssertionException($"Assert.False failed.{UserMessage(message)}");
        }
    }

    public static void Null(object? value, string? message = null)
    {
        if (value is not null)
        {
            throw new AssertionException($"Assert.Null failed.{UserMessage(message)}\n  Actual: {Format(value)}");
        }
    }

    public static T NotNull<T>([NotNull] T? value, string? message = null)
        where T : class
    {
        if (value is null)
        {
            throw new AssertionException($"Assert.NotNull failed.{UserMessage(message)}");
        }
        return value;
    }

    public static T NotNull<T>([NotNull] T? value, string? message = null)
        where T : struct
    {
        if (value is null)
        {
            throw new AssertionException($"Assert.NotNull failed.{UserMessage(message)}");
        }
        return value.Value;
    }

    public static void Same(object? expected, object? actual, string? message = null)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new AssertionException(
                $"Assert.Same failed (references differ).{UserMessage(message)}\n  Expected: {Format(expected)}\n  Actual:   {Format(actual)}");
        }
    }

    public static void NotSame(object? notExpected, object? actual, string? message = null)
    {
        if (ReferenceEquals(notExpected, actual))
        {
            throw new AssertionException($"Assert.NotSame failed (same reference).{UserMessage(message)}\n  Value: {Format(actual)}");
        }
    }

    /// <summary>Asserts that <paramref name="action"/> throws <typeparamref name="T"/> (or a type derived from it).</summary>
    public static T Throws<T>(Action action, string? message = null)
        where T : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new AssertionException(
                $"Assert.Throws failed.{UserMessage(message)}\n  Expected: {typeof(T).Name}\n  Actual:   {ex.GetType().Name}: {ex.Message}", ex);
        }
        throw new AssertionException($"Assert.Throws failed.{UserMessage(message)}\n  Expected: {typeof(T).Name}\n  Actual:   no exception was thrown");
    }

    /// <summary>Asserts that the task produced by <paramref name="action"/> faults with <typeparamref name="T"/> (or a derived type).</summary>
    public static async Task<T> ThrowsAsync<T>(Func<Task> action, string? message = null)
        where T : Exception
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (T ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new AssertionException(
                $"Assert.ThrowsAsync failed.{UserMessage(message)}\n  Expected: {typeof(T).Name}\n  Actual:   {ex.GetType().Name}: {ex.Message}", ex);
        }
        throw new AssertionException($"Assert.ThrowsAsync failed.{UserMessage(message)}\n  Expected: {typeof(T).Name}\n  Actual:   no exception was thrown");
    }

    public static void Contains(string expectedSubstring, string? actual, string? message = null)
    {
        if (actual is null || !actual.Contains(expectedSubstring, StringComparison.Ordinal))
        {
            throw new AssertionException(
                $"Assert.Contains failed.{UserMessage(message)}\n  Expected substring: {Format(expectedSubstring)}\n  Actual string:      {Format(actual)}");
        }
    }

    public static void DoesNotContain(string unexpectedSubstring, string? actual, string? message = null)
    {
        if (actual is not null && actual.Contains(unexpectedSubstring, StringComparison.Ordinal))
        {
            throw new AssertionException(
                $"Assert.DoesNotContain failed.{UserMessage(message)}\n  Unexpected substring: {Format(unexpectedSubstring)}\n  Actual string:        {Format(actual)}");
        }
    }

    public static void Contains<T>(T expected, IEnumerable<T> collection, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var list = collection.ToList();
        if (!list.Contains(expected))
        {
            throw new AssertionException(
                $"Assert.Contains failed.{UserMessage(message)}\n  Expected item: {Format(expected)}\n  Collection:    {Format(list)}");
        }
    }

    /// <summary>Asserts that at least one element satisfies <paramref name="predicate"/>; returns the first such element.</summary>
    public static T Contains<T>(IEnumerable<T> collection, Func<T, bool> predicate, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(predicate);
        var list = collection.ToList();
        foreach (var item in list)
        {
            if (predicate(item))
            {
                return item;
            }
        }
        throw new AssertionException($"Assert.Contains failed: no element matched the predicate.{UserMessage(message)}\n  Collection: {Format(list)}");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var e = expected.ToList();
        var a = actual.ToList();
        var comparer = EqualityComparer<T>.Default;
        int n = Math.Min(e.Count, a.Count);
        for (int i = 0; i < n; i++)
        {
            if (!comparer.Equals(e[i], a[i]))
            {
                throw new AssertionException(
                    $"Assert.SequenceEqual failed at index {i}.{UserMessage(message)}\n  Expected item: {Format(e[i])}\n  Actual item:   {Format(a[i])}\n  Expected: {Format(e)}\n  Actual:   {Format(a)}");
            }
        }
        if (e.Count != a.Count)
        {
            throw new AssertionException(
                $"Assert.SequenceEqual failed: lengths differ (expected {e.Count}, actual {a.Count}).{UserMessage(message)}\n  Expected: {Format(e)}\n  Actual:   {Format(a)}");
        }
    }

    public static T Single<T>(IEnumerable<T> collection, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var list = collection.ToList();
        if (list.Count != 1)
        {
            throw new AssertionException($"Assert.Single failed: the collection has {list.Count} elements.{UserMessage(message)}\n  Collection: {Format(list)}");
        }
        return list[0];
    }

    public static void Empty<T>(IEnumerable<T> collection, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var list = collection.ToList();
        if (list.Count != 0)
        {
            throw new AssertionException($"Assert.Empty failed: the collection has {list.Count} elements.{UserMessage(message)}\n  Collection: {Format(list)}");
        }
    }

    [DoesNotReturn]
    public static void Fail(string message) => throw new AssertionException(message);

    // ----- Formatting -------------------------------------------------------

    internal static string Format(object? value) => value switch
    {
        null => "null",
        string s => "\"" + Escape(s, 300) + "\"",
        char c => "'" + Escape(c.ToString(), 10) + "'",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable seq => FormatSequence(seq),
        _ => value.ToString() ?? value.GetType().Name,
    };

    private static string FormatSequence(IEnumerable seq)
    {
        var sb = new StringBuilder("[");
        int count = 0;
        foreach (var item in seq)
        {
            if (count > 0)
            {
                sb.Append(", ");
            }
            if (count == 20)
            {
                sb.Append('…');
                break;
            }
            sb.Append(Format(item));
            count++;
        }
        return sb.Append(']').ToString();
    }

    private static string Escape(string s, int max)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Length > max ? s[..max] : s)
        {
            switch (c)
            {
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < 0x20 || c is (char)0x2028 or (char)0x2029)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        if (s.Length > max)
        {
            sb.Append(CultureInfo.InvariantCulture, $"… ({s.Length} chars)");
        }
        return sb.ToString();
    }

    private static int FirstDifference(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            if (a[i] != b[i])
            {
                return i;
            }
        }
        return n;
    }

    private static string DescribeCharAt(string s, int i) =>
        i < s.Length ? Format(s[i]) + " (context " + Format(s.Substring(Math.Max(0, i - 10), Math.Min(s.Length, i + 10) - Math.Max(0, i - 10))) + ")" : "<end of string>";

    private static string UserMessage(string? message) => string.IsNullOrEmpty(message) ? string.Empty : " " + message;

    private static void AppendUserMessage(StringBuilder sb, string? message)
    {
        if (!string.IsNullOrEmpty(message))
        {
            sb.Append(' ').Append(message);
        }
    }
}
