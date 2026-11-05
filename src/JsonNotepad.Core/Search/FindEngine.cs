using System.Text.RegularExpressions;
using JsonNotepad.Core.Editing;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.Search;

[Flags]
public enum FindScope
{
    Names = 1,
    Values = 2,
    All = Names | Values,
}

public sealed record FindOptions
{
    public string Text { get; init; } = string.Empty;

    public bool MatchCase { get; init; }

    public bool WholeWord { get; init; }

    public bool UseRegex { get; init; }

    public FindScope Scope { get; init; } = FindScope.All;

    public bool Backwards { get; init; }

    public bool Wrap { get; init; } = true;
}

public enum FindField
{
    Name,
    Value,
}

public readonly record struct FindMatch(JNode Node, FindField Field);

/// <summary>
/// Find / replace over the model in document order. Matching is done with a single
/// <see cref="Regex"/> built from the options (literal text is escaped), so "match case",
/// "whole word" and regex mode share one code path. A match timeout guards against
/// catastrophic patterns typed by the user.
/// </summary>
public sealed class FindEngine
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);
    private readonly Regex _regex;

    public FindEngine(FindOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrEmpty(options.Text))
        {
            throw new ArgumentException("Search text is empty.", nameof(options));
        }
        var pattern = options.UseRegex ? options.Text : Regex.Escape(options.Text);
        if (options.WholeWord)
        {
            pattern = @"(?<![\p{L}\p{Nd}_])(?:" + pattern + @")(?![\p{L}\p{Nd}_])";
        }
        var regexOptions = RegexOptions.CultureInvariant;
        if (!options.MatchCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }
        _regex = new Regex(pattern, regexOptions, MatchTimeout);
    }

    public FindOptions Options { get; }

    /// <summary>Validates a pattern without searching; returns an error message or null.</summary>
    public static string? Validate(FindOptions options)
    {
        try
        {
            _ = new FindEngine(options);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    public bool IsMatch(JNode node, FindField field)
    {
        var text = GetText(node, field);
        return text is not null && _regex.IsMatch(text);
    }

    /// <summary>
    /// Finds the next match after (<paramref name="start"/>, <paramref name="startField"/>), in the
    /// configured direction, wrapping around once. Pass <c>start = null</c> to search from the top.
    /// </summary>
    public FindMatch? FindNext(JNode root, JNode? start, FindField? startField = null)
    {
        var cursor = new PreorderCursor(root, start);
        var fields = Options.Backwards ? new[] { FindField.Value, FindField.Name } : new[] { FindField.Name, FindField.Value };

        // Remaining fields on the start node itself (e.g. the value after a name match).
        if (start is not null && startField is not null)
        {
            bool after = false;
            foreach (var f in fields)
            {
                if (after && InScope(f) && IsMatch(start, f))
                {
                    return new FindMatch(start, f);
                }
                if (f == startField)
                {
                    after = true;
                }
            }
        }
        else if (start is null)
        {
            // Searching from the top: the root itself is a candidate.
            foreach (var f in fields)
            {
                if (InScope(f) && IsMatch(root, f))
                {
                    return new FindMatch(root, f);
                }
            }
        }

        bool wrapped = false;
        while (true)
        {
            bool moved = Options.Backwards ? cursor.MovePrevious() : cursor.MoveNext();
            if (!moved)
            {
                if (!Options.Wrap || wrapped)
                {
                    return null;
                }
                wrapped = true;
                if (Options.Backwards)
                {
                    cursor.MoveToLast();
                }
                else
                {
                    cursor.MoveToFirst();
                }
            }

            var node = cursor.Current;
            foreach (var f in fields)
            {
                if (InScope(f) && IsMatch(node, f))
                {
                    return new FindMatch(node, f);
                }
            }
            if (start is not null && ReferenceEquals(node, start))
            {
                return null; // came full circle
            }
        }
    }

    public IEnumerable<FindMatch> FindAll(JNode root)
    {
        foreach (var node in root.DescendantsAndSelf())
        {
            if (InScope(FindField.Name) && IsMatch(node, FindField.Name))
            {
                yield return new FindMatch(node, FindField.Name);
            }
            if (InScope(FindField.Value) && IsMatch(node, FindField.Value))
            {
                yield return new FindMatch(node, FindField.Value);
            }
        }
    }

    public string Replace(string input, string replacement)
    {
        // In literal mode "$" in the replacement must not be interpreted as a group reference.
        var repl = Options.UseRegex ? replacement : replacement.Replace("$", "$$", StringComparison.Ordinal);
        return _regex.Replace(input, repl);
    }

    /// <summary>Applies a replacement to one match. Returns false when the result is invalid for the field.</summary>
    public EditResult ReplaceMatch(NodeEditor editor, FindMatch match, string replacement)
    {
        var text = GetText(match.Node, match.Field);
        if (text is null)
        {
            return EditResult.Fail("Nothing to replace.");
        }
        var result = Replace(text, replacement);
        return match.Field == FindField.Name
            ? editor.Rename(match.Node, result)
            : editor.SetValue(match.Node, result);
    }

    /// <summary>Replaces every match as a single undo step. Returns (replaced, skipped).</summary>
    public (int Replaced, int Skipped) ReplaceAll(NodeEditor editor, string replacement)
    {
        var matches = FindAll(editor.Document.Root)
            .Where(m => m.Field == FindField.Name || NodeEditor.CanEditValue(m.Node))
            .ToList();
        int replaced = 0;
        int skipped = 0;
        using (editor.UndoManager.BeginCompound("Replace All"))
        {
            foreach (var m in matches)
            {
                if (ReplaceMatch(editor, m, replacement).Success)
                {
                    replaced++;
                }
                else
                {
                    skipped++;
                }
            }
        }
        return (replaced, skipped);
    }

    private bool InScope(FindField field) =>
        field == FindField.Name ? Options.Scope.HasFlag(FindScope.Names) : Options.Scope.HasFlag(FindScope.Values);

    private static string? GetText(JNode node, FindField field) => field switch
    {
        FindField.Name => node.Parent?.Kind == JNodeKind.Object ? node.Name : null,
        _ => node.Kind switch
        {
            JNodeKind.Null => "null",
            JNodeKind.Object or JNodeKind.Array => null,
            _ => node.Value,
        },
    };
}
