using JsonNotepad.Core.Editing;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.Tests;

/// <summary>Small conveniences shared by the test classes.</summary>
internal static class Json
{
    /// <summary>The named member of an object; fails the test when it is missing.</summary>
    public static JNode Prop(this JNode node, string name) =>
        node.FindProperty(name) ?? throw new Framework.AssertionException($"Expected a member named \"{name}\" in {node}.");

    /// <summary>Compact serialization, the easiest way to compare whole trees in assertions.</summary>
    public static string Compact(JNode node) => JsonDocumentWriter.ToJson(node, FormatOptions.Compact);

    public static string Compact(JDocument document) => Compact(document.Root);

    public static JDocument Doc(string json) => new(JsonParser.Parse(json));

    public static (JDocument Document, UndoManager Undo, NodeEditor Editor) Editor(string json, int maxHistory = 1000)
    {
        var doc = Doc(json);
        var undo = new UndoManager(maxHistory);
        return (doc, undo, new NodeEditor(doc, undo));
    }
}

/// <summary>A unique scratch directory under the system temp folder, deleted on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "JsonNotepad.Core.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort: a lingering handle (e.g. a file watcher) must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Records progress reports synchronously (unlike <see cref="Progress{T}"/>, which posts).</summary>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    private readonly List<T> _reports = new();

    public IReadOnlyList<T> Reports
    {
        get
        {
            lock (_reports)
            {
                return _reports.ToList();
            }
        }
    }

    public void Report(T value)
    {
        lock (_reports)
        {
            _reports.Add(value);
        }
    }
}

/// <summary>A read-only stream that hands out data in small, irregular chunks to stress buffer boundaries.</summary>
internal sealed class ChunkedStream(byte[] data, int maxChunk) : Stream
{
    private int _position;
    private int _reads;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => data.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        // Vary the chunk size so token boundaries land in many different places.
        int chunk = Math.Max(1, maxChunk - (_reads++ * 7919 % Math.Max(1, maxChunk / 2)));
        int n = Math.Min(Math.Min(chunk, buffer.Length), data.Length - _position);
        data.AsSpan(_position, n).CopyTo(buffer);
        _position += n;
        return n;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
