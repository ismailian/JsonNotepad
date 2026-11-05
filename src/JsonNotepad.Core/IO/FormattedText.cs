using System.Text;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.IO;

/// <summary>
/// The serialized form of a document as displayed by the Text View, with the indexes
/// a virtualized viewer needs: where each line starts, the longest line, and the line
/// each node (in pre-order) starts on.
/// </summary>
public sealed class FormattedText
{
    private FormattedText(string text, int[] lineStarts, int maxLineLength, int[] nodeLines, long version)
    {
        Text = text;
        LineStarts = lineStarts;
        MaxLineLength = maxLineLength;
        NodeLines = nodeLines;
        SourceVersion = version;
    }

    public static FormattedText Empty { get; } = new(string.Empty, new[] { 0 }, 0, Array.Empty<int>(), -1);

    public string Text { get; }

    public int[] LineStarts { get; }

    public int LineCount => LineStarts.Length;

    public int MaxLineLength { get; }

    /// <summary>Starting line of each node in pre-order (index 0 is the root).</summary>
    public int[] NodeLines { get; }

    /// <summary>The <see cref="JDocument.Version"/> this text was produced from.</summary>
    public long SourceVersion { get; }

    public ReadOnlySpan<char> GetLine(int line)
    {
        int start = LineStarts[line];
        int end = line + 1 < LineStarts.Length ? LineStarts[line + 1] : Text.Length;
        var span = Text.AsSpan(start, end - start);
        return span.TrimEnd("\r\n");
    }

    /// <summary>Finds the node (pre-order index) whose text starts at or before <paramref name="line"/>.</summary>
    public int FindNodeIndexAtLine(int line)
    {
        if (NodeLines.Length == 0)
        {
            return -1;
        }
        int idx = Array.BinarySearch(NodeLines, line);
        if (idx < 0)
        {
            idx = ~idx - 1;
        }
        else
        {
            // Several nodes can start on the same line in compact output; pick the first.
            while (idx > 0 && NodeLines[idx - 1] == line)
            {
                idx--;
            }
        }
        return Math.Max(0, idx);
    }

    /// <summary>
    /// Builds the text for a snapshot. CPU-bound; call from a worker thread.
    /// </summary>
    public static FormattedText Build(JNode snapshotRoot, FormatOptions options, long sourceVersion, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder(4096);
        var nodeLines = new List<int>(1024);
        using (var writer = new StringWriter(sb))
        {
            JsonDocumentWriter.Write(snapshotRoot, writer, options with { NewLine = "\n" }, nodeLines, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var text = sb.ToString();
        sb.Clear();

        var starts = new List<int>(nodeLines.Count + 1) { 0 };
        int maxLen = 0;
        int pos = 0;
        var span = text.AsSpan();
        while (true)
        {
            int nl = span[pos..].IndexOf('\n');
            if (nl < 0)
            {
                maxLen = Math.Max(maxLen, span.Length - pos);
                break;
            }
            maxLen = Math.Max(maxLen, nl);
            pos += nl + 1;
            if (pos >= span.Length)
            {
                break;
            }
            starts.Add(pos);
        }
        return new FormattedText(text, starts.ToArray(), maxLen, nodeLines.ToArray(), sourceVersion);
    }
}
