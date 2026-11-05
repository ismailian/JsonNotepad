using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Paths;

namespace JsonNotepad.Core.IO;

/// <summary>Result of parsing: the root value plus any non-fatal findings.</summary>
public sealed record ParseResult(JNode Root, IReadOnlyList<Diagnostic> Warnings, long NodeCount);

/// <summary>
/// Streaming JSON parser that builds a <see cref="JNode"/> tree.
/// </summary>
/// <remarks>
/// Built on <see cref="Utf8JsonReader"/> from the BCL (no packages): it is one of the
/// fastest JSON tokenizers available for .NET, works directly on UTF-8 bytes, and
/// supports incremental input via <see cref="JsonReaderState"/>. We feed it 1 MB
/// blocks from the stream so memory used for input is constant regardless of file
/// size; only the resulting tree grows with the document.
/// </remarks>
public static partial class JsonParser
{
    private const int InitialBufferSize = 1 << 20;
    private const int CancellationCheckInterval = 1 << 16;

    /// <summary>Parses a complete JSON text held in memory.</summary>
    public static JNode Parse(string text, ParseOptions? options = null) =>
        ParseWithDiagnostics(text, options).Root;

    public static ParseResult ParseWithDiagnostics(string text, ParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= ParseOptions.Default;
        var bytes = Encoding.UTF8.GetBytes(text);
        var builder = new TreeBuilder(options);
        try
        {
            var reader = new Utf8JsonReader(bytes, isFinalBlock: true, new JsonReaderState(CreateReaderOptions(options)));
            ReadTokens(ref reader, builder, CancellationToken.None);
        }
        catch (JsonException ex)
        {
            throw Translate(ex);
        }
        return builder.Complete();
    }

    /// <summary>Tries to parse; returns false with a message instead of throwing.</summary>
    public static bool TryParse(string text, ParseOptions? options, out JNode? root, out string? error)
    {
        try
        {
            root = Parse(text, options);
            error = null;
            return true;
        }
        catch (JsonParseException ex)
        {
            root = null;
            error = ex.Line > 0 ? $"{ex.Message} (line {ex.Line}, column {ex.Column})" : ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Parses UTF-8 JSON from <paramref name="utf8Stream"/> in blocks. Synchronous by design:
    /// <see cref="Utf8JsonReader"/> is a ref struct and cannot live across awaits, so callers
    /// run this on a worker thread (see <see cref="JsonLoader"/>).
    /// </summary>
    public static ParseResult ParseStream(Stream utf8Stream, ParseOptions options, Action? blockProcessed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(options);

        var builder = new TreeBuilder(options);
        var state = new JsonReaderState(CreateReaderOptions(options));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
        int length = 0;
        bool isFinal = false;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (!isFinal && length < buffer.Length)
                {
                    int read = utf8Stream.Read(buffer, length, buffer.Length - length);
                    if (read == 0)
                    {
                        isFinal = true;
                        break;
                    }
                    length += read;
                }

                var reader = new Utf8JsonReader(new ReadOnlySpan<byte>(buffer, 0, length), isFinal, state);
                ReadTokens(ref reader, builder, cancellationToken);
                int consumed = (int)reader.BytesConsumed;
                state = reader.CurrentState;
                blockProcessed?.Invoke();

                if (isFinal)
                {
                    break;
                }

                int remaining = length - consumed;
                if (consumed == 0 && remaining == buffer.Length)
                {
                    // A single token (e.g. a huge string) is larger than the buffer: grow it.
                    var bigger = ArrayPool<byte>.Shared.Rent(checked(buffer.Length * 2));
                    Buffer.BlockCopy(buffer, 0, bigger, 0, remaining);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = bigger;
                }
                else if (remaining > 0)
                {
                    Buffer.BlockCopy(buffer, consumed, buffer, 0, remaining);
                }
                length = remaining;
            }
        }
        catch (JsonException ex)
        {
            throw Translate(ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return builder.Complete();
    }

    private static JsonReaderOptions CreateReaderOptions(ParseOptions options) => new()
    {
        AllowTrailingCommas = options.AllowTrailingCommas,
        CommentHandling = options.AllowComments ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
        MaxDepth = Math.Max(1, options.MaxDepth),
    };

    private static void ReadTokens(ref Utf8JsonReader reader, TreeBuilder builder, CancellationToken ct)
    {
        int countdown = CancellationCheckInterval;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    builder.StartContainer(JNode.CreateObject());
                    break;
                case JsonTokenType.StartArray:
                    builder.StartContainer(JNode.CreateArray());
                    break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    builder.EndContainer();
                    break;
                case JsonTokenType.PropertyName:
                    builder.SetPendingName(reader.ValueIsEscaped
                        ? builder.Names.Get(reader.GetString()!)
                        : builder.Names.Get(reader.ValueSpan));
                    break;
                case JsonTokenType.String:
                    builder.AddValue(JNode.CreateString(ReadStringValue(ref reader, builder)));
                    break;
                case JsonTokenType.Number:
                    var span = reader.ValueSpan;
                    builder.AddValue(JNode.CreateNumberUnchecked(span.Length <= 6
                        ? builder.Values.Get(span)
                        : Encoding.UTF8.GetString(span)));
                    break;
                case JsonTokenType.True:
                    builder.AddValue(JNode.CreateBoolean(true));
                    break;
                case JsonTokenType.False:
                    builder.AddValue(JNode.CreateBoolean(false));
                    break;
                case JsonTokenType.Null:
                    builder.AddValue(JNode.CreateNull());
                    break;
            }

            if (--countdown == 0)
            {
                countdown = CancellationCheckInterval;
                ct.ThrowIfCancellationRequested();
            }
        }
    }

    private static string ReadStringValue(ref Utf8JsonReader reader, TreeBuilder builder)
    {
        // Short, unescaped values (enum-like: "active", "USD", "en-US") are atomized too.
        if (!reader.ValueIsEscaped && reader.ValueSpan.Length <= 24)
        {
            return builder.Values.Get(reader.ValueSpan);
        }
        return reader.GetString()!;
    }

    private static JsonParseException Translate(JsonException ex)
    {
        long line = (ex.LineNumber ?? -1) + 1;
        long column = (ex.BytePositionInLine ?? -1) + 1;
        var message = LocationSuffix().Replace(ex.Message, string.Empty).Trim();
        return new JsonParseException(message, line, column, ex);
    }

    [GeneratedRegex(@"\s*(Path: .*?\|\s*)?LineNumber: \d+ \| BytePositionInLine: \d+\.?$")]
    private static partial Regex LocationSuffix();

    /// <summary>Accumulates tokens into a tree (Builder pattern).</summary>
    private sealed class TreeBuilder
    {
        private readonly ParseOptions _options;
        private readonly List<JNode> _stack = new(64);
        private readonly List<HashSet<string>?> _keySets = new(64);
        private readonly Stack<HashSet<string>> _setPool = new();
        private readonly List<Diagnostic> _warnings = new();
        private JNode? _root;
        private string? _pendingName;
        private long _nodeCount;

        public TreeBuilder(ParseOptions options)
        {
            _options = options;
        }

        public NameTable Names { get; } = new();

        public NameTable Values { get; } = new();

        public void SetPendingName(string name) => _pendingName = name;

        public void StartContainer(JNode node)
        {
            Attach(node);
            _stack.Add(node);
            _keySets.Add(node.Kind == JNodeKind.Object && _options.DetectDuplicateKeys ? RentSet() : null);
        }

        public void EndContainer()
        {
            int top = _stack.Count - 1;
            _stack[top].TrimExcess();
            _stack.RemoveAt(top);
            if (_keySets[top] is { } set)
            {
                set.Clear();
                _setPool.Push(set);
            }
            _keySets.RemoveAt(top);
        }

        public void AddValue(JNode node) => Attach(node);

        public ParseResult Complete()
        {
            if (_root is null)
            {
                throw new JsonParseException("The input does not contain a JSON value.", 1, 1);
            }
            return new ParseResult(_root, _warnings, _nodeCount);
        }

        private void Attach(JNode node)
        {
            _nodeCount++;
            int top = _stack.Count - 1;
            if (top < 0)
            {
                _root = node;
                return;
            }
            var parent = _stack[top];
            if (parent.Kind == JNodeKind.Object)
            {
                var name = _pendingName ?? string.Empty;
                node.Name = name;
                _pendingName = null;
                if (_keySets[top] is { } keys && !keys.Add(name) && _warnings.Count < _options.MaxWarnings)
                {
                    _warnings.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Duplicate key \"{name}\". Most JSON readers keep only the last occurrence.",
                        JsonPath.Build(parent) + JsonPath.FormatMember(name),
                        Source: "Parser"));
                }
            }
            parent.AddChildCore(node);
        }

        private HashSet<string> RentSet() =>
            _setPool.Count > 0 ? _setPool.Pop() : new HashSet<string>(StringComparer.Ordinal);
    }
}
