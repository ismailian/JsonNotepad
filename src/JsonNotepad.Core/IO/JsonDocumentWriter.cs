using System.Text;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.IO;

/// <summary>
/// Serializes the model to text.
/// </summary>
/// <remarks>
/// A hand-written writer (rather than <c>Utf8JsonWriter</c>) because we need three things
/// the BCL writer does not offer together: number literals written verbatim, any
/// <see cref="TextWriter"/>/encoding as target, and a map from each node (in pre-order)
/// to the line it starts on, which powers Tree ⇄ Text view synchronization.
/// The traversal is iterative so document depth is limited only by memory.
/// </remarks>
public static class JsonDocumentWriter
{
    private const int CancellationCheckInterval = 4096;

    public static string ToJson(JNode node, FormatOptions? options = null)
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        Write(node, writer, options ?? FormatOptions.Default, null, CancellationToken.None);
        return sb.ToString();
    }

    /// <summary>
    /// Writes <paramref name="root"/> and its subtree.
    /// </summary>
    /// <param name="nodeLines">When provided, receives the 0-based starting line of every node in pre-order.</param>
    /// <returns>The number of lines written.</returns>
    public static int Write(JNode root, TextWriter writer, FormatOptions options, List<int>? nodeLines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);

        var state = new WriterState(writer, options, nodeLines);
        state.RecordNode();
        if (!state.WriteValueOrOpen(root))
        {
            var stack = new Stack<Frame>();
            stack.Push(new Frame(root));
            int countdown = CancellationCheckInterval;

            while (stack.Count > 0)
            {
                var frame = stack.Peek();
                var container = frame.Node;
                if (frame.Next < container.Count)
                {
                    var child = container[frame.Next];
                    if (frame.Next > 0)
                    {
                        writer.Write(',');
                    }
                    frame.Next++;
                    state.NewLine(stack.Count);
                    if (container.Kind == JNodeKind.Object)
                    {
                        state.WriteString(child.Name ?? string.Empty);
                        writer.Write(options.Indented ? ": " : ":");
                    }
                    state.RecordNode();
                    if (!state.WriteValueOrOpen(child))
                    {
                        stack.Push(new Frame(child));
                    }

                    if (--countdown == 0)
                    {
                        countdown = CancellationCheckInterval;
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                else
                {
                    stack.Pop();
                    state.NewLine(stack.Count);
                    writer.Write(container.Kind == JNodeKind.Object ? '}' : ']');
                }
            }
        }

        if (options.Indented && options.TrailingNewLine)
        {
            writer.Write(options.NewLine);
            state.Lines++;
        }
        return state.Lines + 1;
    }

    /// <summary>Writes a JSON string literal (with quotes) for <paramref name="value"/>.</summary>
    public static string QuoteString(string value, bool escapeNonAscii = false)
    {
        var sb = new StringBuilder(value.Length + 2);
        using var w = new StringWriter(sb);
        WriteEscaped(w, value, escapeNonAscii);
        return sb.ToString();
    }

    internal static void WriteEscaped(TextWriter w, string value, bool escapeNonAscii)
    {
        w.Write('"');
        int runStart = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            string? esc = c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\b' => "\\b",
                '\f' => "\\f",
                _ => null,
            };
            bool needsUnicode = esc is null && (c < 0x20 || (escapeNonAscii && c > 0x7E) || c == (char)0x2028 || c == (char)0x2029);
            if (esc is null && !needsUnicode)
            {
                continue;
            }
            if (i > runStart)
            {
                w.Write(value.AsSpan(runStart, i - runStart));
            }
            if (esc is not null)
            {
                w.Write(esc);
            }
            else
            {
                w.Write("\\u");
                w.Write(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
            }
            runStart = i + 1;
        }
        if (runStart < value.Length)
        {
            w.Write(value.AsSpan(runStart));
        }
        w.Write('"');
    }

    private sealed class Frame(JNode node)
    {
        public JNode Node { get; } = node;

        public int Next { get; set; }
    }

    private sealed class WriterState
    {
        private readonly TextWriter _writer;
        private readonly FormatOptions _options;
        private readonly List<int>? _nodeLines;
        private readonly string _indentUnit;
        private readonly List<string> _indentCache = new() { string.Empty };

        public WriterState(TextWriter writer, FormatOptions options, List<int>? nodeLines)
        {
            _writer = writer;
            _options = options;
            _nodeLines = nodeLines;
            _indentUnit = options.IndentUnit;
        }

        public int Lines { get; set; }

        public void RecordNode() => _nodeLines?.Add(Lines);

        public void NewLine(int depth)
        {
            if (!_options.Indented)
            {
                return;
            }
            _writer.Write(_options.NewLine);
            Lines++;
            _writer.Write(Indent(depth));
        }

        public void WriteString(string s) => WriteEscaped(_writer, s, _options.EscapeNonAscii);

        /// <summary>Writes a primitive or empty container and returns true; returns false after writing an opening bracket.</summary>
        public bool WriteValueOrOpen(JNode node)
        {
            switch (node.Kind)
            {
                case JNodeKind.Object:
                    if (node.Count == 0)
                    {
                        _writer.Write("{}");
                        return true;
                    }
                    _writer.Write('{');
                    return false;
                case JNodeKind.Array:
                    if (node.Count == 0)
                    {
                        _writer.Write("[]");
                        return true;
                    }
                    _writer.Write('[');
                    return false;
                case JNodeKind.String:
                    WriteString(node.Value ?? string.Empty);
                    return true;
                case JNodeKind.Number:
                    _writer.Write(node.Value ?? "0");
                    return true;
                case JNodeKind.Boolean:
                    _writer.Write(node.BooleanValue ? "true" : "false");
                    return true;
                default:
                    _writer.Write("null");
                    return true;
            }
        }

        private string Indent(int depth)
        {
            while (_indentCache.Count <= depth)
            {
                _indentCache.Add(_indentCache[^1] + _indentUnit);
            }
            return _indentCache[depth];
        }
    }
}
