using System.Diagnostics;
using System.Text;
using JsonNotepad.Core.Model;

namespace JsonNotepad.Core.IO;

/// <summary>Progress report for long-running loads.</summary>
public readonly record struct LoadProgress(long BytesRead, long TotalBytes)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesRead / TotalBytes, 0, 1);
}

public sealed record LoadResult(
    JDocument Document,
    FileFormatInfo Format,
    IReadOnlyList<Diagnostic> Diagnostics,
    long FileSize,
    long NodeCount,
    TimeSpan Elapsed);

/// <summary>
/// Opens files from disk: sniffs encoding and style, transcodes non-UTF-8 input,
/// and runs the streaming parser on a worker thread with progress and cancellation.
/// </summary>
public static class JsonLoader
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    public static Task<LoadResult> LoadFileAsync(
        string path,
        ParseOptions options,
        IProgress<LoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(options);
        // Utf8JsonReader is synchronous by nature; offload the CPU-bound parse so the UI stays responsive.
        return Task.Run(() => LoadFile(path, options, progress, cancellationToken), cancellationToken);
    }

    public static LoadResult LoadFile(string path, ParseOptions options, IProgress<LoadProgress>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, // don't block editors/loggers that keep the file open
            bufferSize: 1 << 16,
            FileOptions.SequentialScan);

        long length = file.Length;
        var head = new byte[(int)Math.Min(length, FormatDetector.SampleSize)];
        file.ReadExactly(head);

        var (encoding, bomLength) = FormatDetector.DetectEncoding(head);
        bool wholeFile = head.Length == length;
        var sample = encoding.GetString(head, bomLength, head.Length - bomLength);
        var format = FormatDetector.DetectStyle(sample, encoding, bomLength > 0, wholeFile);
        if (!wholeFile && encoding is UTF8Encoding)
        {
            format = format with { HadTrailingNewLine = EndsWithLineFeed(file, length) };
        }

        if (wholeFile && string.IsNullOrWhiteSpace(sample))
        {
            var empty = JDocument.CreateEmpty();
            var info = new Diagnostic(DiagnosticSeverity.Information, "The file was empty; a new object was created.", "$", Source: "Loader");
            return new LoadResult(empty, format, new[] { info }, length, 1, stopwatch.Elapsed);
        }

        file.Position = bomLength;
        using var input = encoding is UTF8Encoding
            ? (Stream)new NonClosingStream(file)
            : Encoding.CreateTranscodingStream(file, encoding, Encoding.UTF8, leaveOpen: true);

        var lastReport = Stopwatch.StartNew();
        progress?.Report(new LoadProgress(0, length));
        void OnBlock()
        {
            if (progress is not null && lastReport.Elapsed >= ProgressInterval)
            {
                lastReport.Restart();
                progress.Report(new LoadProgress(file.Position, length));
            }
        }

        var result = JsonParser.ParseStream(input, options, OnBlock, cancellationToken);
        progress?.Report(new LoadProgress(length, length));

        return new LoadResult(new JDocument(result.Root), format, result.Warnings, length, result.NodeCount, stopwatch.Elapsed);
    }

    private static bool EndsWithLineFeed(FileStream file, long length)
    {
        if (length < 1)
        {
            return false;
        }
        long saved = file.Position;
        try
        {
            file.Position = Math.Max(0, length - 8);
            Span<byte> tail = stackalloc byte[(int)Math.Min(8, length)];
            file.ReadExactly(tail);
            var trimmed = tail.TrimEnd((byte)' ');
            return trimmed.Length > 0 && trimmed[^1] == (byte)'\n';
        }
        finally
        {
            file.Position = saved;
        }
    }

    /// <summary>Lets the parser own a stream wrapper without closing the underlying file early.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
