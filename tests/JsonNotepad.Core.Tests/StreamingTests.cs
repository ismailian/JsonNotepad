using System.Globalization;
using System.Text;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

public sealed class StreamingTests
{
    private const int RecordCount = 25_000;
    private const int BigStringLength = 1_600_000; // > the parser's 1 MB initial buffer

    /// <summary>
    /// A >3 MB document: a huge string first (forces the buffer to grow), then many records
    /// whose tokens straddle block boundaries, with multi-byte UTF-8 and escapes mixed in.
    /// </summary>
    private static (byte[] Bytes, string BigString) BuildDocument()
    {
        var big = new StringBuilder(BigStringLength);
        for (int i = 0; big.Length < BigStringLength; i++)
        {
            big.Append(i % 97 == 0 ? 'é' : (char)('a' + (i % 26)));
        }
        big.Insert(BigStringLength / 2, "\n\"quoted\"\t");
        var bigString = big.ToString();

        var sb = new StringBuilder(4 << 20);
        sb.Append("{\"big\":").Append(JsonDocumentWriter.QuoteString(bigString)).Append(",\n\"records\":[\n");
        for (int i = 0; i < RecordCount; i++)
        {
            if (i > 0)
            {
                sb.Append(",\n");
            }
            sb.Append(CultureInfo.InvariantCulture,
                $"  {{\"id\":{i},\"name\":\"Record é {i}\",\"value\":{i}.25,\"ok\":{(i % 2 == 0 ? "true" : "false")},\"none\":null,\"tags\":[\"t{i % 7}\",\"x\\u0041\"]}}");
        }
        sb.Append("\n],\n\"tail\":\"end\"}");
        return (Encoding.UTF8.GetBytes(sb.ToString()), bigString);
    }

    private static void VerifyDocument(ParseResult result, string bigString)
    {
        var root = result.Root;
        Assert.Equal(3, root.Count);
        Assert.Equal(bigString.Length, root.Prop("big").Value!.Length);
        Assert.Equal(bigString, root.Prop("big").Value);

        var records = root.Prop("records");
        Assert.Equal(RecordCount, records.Count);
        foreach (int i in new[] { 0, 1, 4095, 12_345, RecordCount - 1 })
        {
            var r = records[i];
            Assert.Equal(i.ToString(CultureInfo.InvariantCulture), r.Prop("id").Value);
            Assert.Equal("Record é " + i.ToString(CultureInfo.InvariantCulture), r.Prop("name").Value);
            Assert.Equal(i.ToString(CultureInfo.InvariantCulture) + ".25", r.Prop("value").Value);
            Assert.Equal(i % 2 == 0, r.Prop("ok").BooleanValue);
            Assert.Equal(JNodeKind.Null, r.Prop("none").Kind);
            Assert.Equal("xA", r.Prop("tags")[1].Value);
        }
        Assert.Equal("end", root.Prop("tail").Value);

        // root + big + records array + tail + per record (object + 6 members + 2 tags)
        long expectedNodes = 4 + (RecordCount * 9L);
        Assert.Equal(expectedNodes, result.NodeCount);
        Assert.Equal(expectedNodes, (long)root.DescendantsAndSelf().Count());
    }

    [Test]
    public void ParsesLargeDocumentFromMemoryStream()
    {
        var (bytes, bigString) = BuildDocument();
        Assert.True(bytes.Length > 3 * 1024 * 1024, $"document should exceed 3 MB (is {bytes.Length})");

        int blocks = 0;
        using var stream = new MemoryStream(bytes);
        var result = JsonParser.ParseStream(stream, ParseOptions.Default, () => blocks++, CancellationToken.None);

        VerifyDocument(result, bigString);
        Assert.True(blocks >= 3, $"expected several blocks, got {blocks}");
        TestContext.Log(string.Create(CultureInfo.InvariantCulture, $"{bytes.Length / 1048576.0:F1} MB in {blocks} blocks"));
    }

    [Test]
    public void ParsesLargeDocumentFromChunkedStream()
    {
        // Short irregular reads: the parser must keep filling its buffer and handle
        // tokens (including multi-byte UTF-8 sequences) split across reads.
        var (bytes, bigString) = BuildDocument();
        using var stream = new ChunkedStream(bytes, 65_521);
        var result = JsonParser.ParseStream(stream, ParseOptions.Default, null, CancellationToken.None);
        VerifyDocument(result, bigString);
    }

    [Test]
    public void StreamResultMatchesInMemoryParse()
    {
        var (bytes, _) = BuildDocument();
        var fromText = JsonParser.Parse(Encoding.UTF8.GetString(bytes));
        using var stream = new ChunkedStream(bytes, 333_333);
        var fromStream = JsonParser.ParseStream(stream, ParseOptions.Default, null, CancellationToken.None).Root;
        Assert.True(JsonEquality.DeepEquals(fromText, fromStream));
        Assert.Equal(Json.Compact(fromText).Length, Json.Compact(fromStream).Length);
    }

    [Test]
    public void SeveralHugeStringsInARow()
    {
        // Each string is larger than the current buffer, so the buffer grows more than once.
        var a = new string('a', 1_200_000);
        var b = new string('b', 2_600_000);
        var bytes = Encoding.UTF8.GetBytes($"[\"{a}\",\"{b}\",\"{a}\"]");
        using var stream = new MemoryStream(bytes);
        var root = JsonParser.ParseStream(stream, ParseOptions.Default, null, CancellationToken.None).Root;
        Assert.Equal(3, root.Count);
        Assert.Equal(a, root[0].Value);
        Assert.Equal(b, root[1].Value);
        Assert.Equal(a, root[2].Value);
    }

    [Test]
    public void DuplicateKeysAndCommentsWorkWhenStreaming()
    {
        var bytes = Encoding.UTF8.GetBytes("/* header */ {\"a\":1, \"a\":2, \"list\":[1,2,],}");
        using var stream = new ChunkedStream(bytes, 3);
        var result = JsonParser.ParseStream(stream, ParseOptions.Default, null, CancellationToken.None);
        Assert.Equal("$.a", Assert.Single(result.Warnings).Path);
        Assert.Equal(2, result.Root.Prop("list").Count);
    }

    [Test]
    public void ErrorLocationIsTrackedAcrossBlocks()
    {
        var sb = new StringBuilder("[\n");
        const int lines = 120_000; // ~1.6 MB, so the error is in a later block
        for (int i = 0; i < lines; i++)
        {
            sb.Append("  \"line number ").Append(i).Append("\",\n");
        }
        sb.Append("  oops\n]");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));

        var ex = Assert.Throws<JsonParseException>(() => JsonParser.ParseStream(stream, ParseOptions.Default, null, CancellationToken.None));
        Assert.Equal((long)lines + 2, ex.Line);
        Assert.Equal(3L, ex.Column);
    }

    [Test]
    public void EmptyStreamIsAnError()
    {
        using var stream = new MemoryStream();
        Assert.Throws<JsonParseException>(() => JsonParser.ParseStream(stream, ParseOptions.Default, null, CancellationToken.None));
    }

    [Test]
    public void PreCancelledTokenThrows()
    {
        var (bytes, _) = BuildDocument();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var stream = new MemoryStream(bytes);
        Assert.Throws<OperationCanceledException>(() => JsonParser.ParseStream(stream, ParseOptions.Default, null, cts.Token));
    }

    [Test]
    public void CancellationDuringParseThrows()
    {
        var (bytes, _) = BuildDocument();
        using var cts = new CancellationTokenSource();
        using var stream = new MemoryStream(bytes);
        int blocks = 0;
        Assert.Throws<OperationCanceledException>(() => JsonParser.ParseStream(stream, ParseOptions.Default, () =>
        {
            if (++blocks == 1)
            {
                cts.Cancel();
            }
        }, cts.Token));
        Assert.Equal(1, blocks);
    }
}
