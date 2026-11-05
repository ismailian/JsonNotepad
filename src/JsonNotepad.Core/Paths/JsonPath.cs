using System.Globalization;
using System.Text;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.Paths;

/// <summary>
/// Builds and resolves normalized JSONPath locations such as <c>$.store.book[0]['first name']</c>.
/// This is the JSON counterpart of XML Notepad's XPath generator: it powers the status bar,
/// "Copy Path", "Go to Path" and error-list navigation.
/// </summary>
public static class JsonPath
{
    public const string RootToken = "$";

    public static string Build(JNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var segments = new List<string>();
        for (var n = node; n.Parent is not null; n = n.Parent)
        {
            segments.Add(n.Parent.Kind == JNodeKind.Object
                ? FormatMember(n.Name ?? string.Empty)
                : FormatIndex(n.GetIndex()));
        }
        var sb = new StringBuilder(RootToken);
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            sb.Append(segments[i]);
        }
        return sb.ToString();
    }

    public static string FormatIndex(int index) => "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

    public static string FormatMember(string name) =>
        IsIdentifier(name) ? "." + name : "['" + name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "']";

    /// <summary>Resolves a path against <paramref name="root"/>; returns null when any segment is missing.</summary>
    public static JNode? Resolve(JNode root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!TryParse(path, out var segments, out _))
        {
            return null;
        }
        var current = root;
        foreach (var seg in segments)
        {
            if (seg.Index is int index)
            {
                if (current.Kind != JNodeKind.Array && current.Kind != JNodeKind.Object)
                {
                    return null;
                }
                if (index < 0)
                {
                    index += current.Count;
                }
                if ((uint)index >= (uint)current.Count)
                {
                    return null;
                }
                current = current[index];
            }
            else
            {
                var next = current.FindProperty(seg.Name!);
                if (next is null)
                {
                    return null;
                }
                current = next;
            }
        }
        return current;
    }

    public readonly record struct Segment(string? Name, int? Index);

    /// <summary>Parses the supported subset: <c>$</c>, <c>.name</c>, <c>['name']</c>, <c>["name"]</c>, <c>[n]</c>.</summary>
    public static bool TryParse(string path, out List<Segment> segments, out string? error)
    {
        segments = new List<Segment>();
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "The path is empty.";
            return false;
        }
        var s = path.Trim();
        int i = 0;
        if (s[0] == '$')
        {
            i = 1;
        }
        else if (s[0] != '.' && s[0] != '[')
        {
            // Allow "a.b[0]" as shorthand for "$.a.b[0]".
            s = "." + s;
        }

        while (i < s.Length)
        {
            char c = s[i];
            if (c == '.')
            {
                i++;
                int start = i;
                while (i < s.Length && s[i] != '.' && s[i] != '[')
                {
                    i++;
                }
                if (i == start)
                {
                    error = $"Expected a member name at position {start + 1}.";
                    return false;
                }
                segments.Add(new Segment(s[start..i], null));
            }
            else if (c == '[')
            {
                i++;
                if (i < s.Length && (s[i] == '\'' || s[i] == '"'))
                {
                    char quote = s[i++];
                    var sb = new StringBuilder();
                    while (i < s.Length && s[i] != quote)
                    {
                        if (s[i] == '\\' && i + 1 < s.Length)
                        {
                            i++;
                        }
                        sb.Append(s[i++]);
                    }
                    if (i >= s.Length)
                    {
                        error = "Unterminated quoted name.";
                        return false;
                    }
                    i++; // closing quote
                    if (i >= s.Length || s[i] != ']')
                    {
                        error = "Expected ']'.";
                        return false;
                    }
                    i++;
                    segments.Add(new Segment(sb.ToString(), null));
                }
                else
                {
                    int start = i;
                    while (i < s.Length && s[i] != ']')
                    {
                        i++;
                    }
                    if (i >= s.Length)
                    {
                        error = "Expected ']'.";
                        return false;
                    }
                    var text = s[start..i].Trim();
                    i++;
                    if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int index))
                    {
                        error = $"'{text}' is not an array index.";
                        return false;
                    }
                    segments.Add(new Segment(null, index));
                }
            }
            else
            {
                error = $"Unexpected character '{c}' at position {i + 1}.";
                return false;
            }
        }
        return true;
    }

    private static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] is '_' or '$'))
        {
            return false;
        }
        foreach (char c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '$'))
            {
                return false;
            }
        }
        return true;
    }
}
