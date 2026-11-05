using System.Text;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

public sealed class LoaderTests : IDisposable
{
    private const string Sample = "{\n  \"name\": \"café 中\",\n  \"list\": [1, 2]\n}\n";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string WriteBytes(string name, byte[] bytes)
    {
        var path = _temp.File(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string WriteText(string name, string text, Encoding encoding, bool bom)
    {
        var preamble = bom ? encoding.GetPreamble() : Array.Empty<byte>();
        return WriteBytes(name, [.. preamble, .. encoding.GetBytes(text)]);
    }

    private static LoadResult Load(string path) => JsonLoader.LoadFile(path, ParseOptions.Default, null, CancellationToken.None);

    private static void AssertSampleContent(LoadResult result)
    {
        Assert.Equal("café 中", result.Document.Root.Prop("name").Value);
        Assert.Equal(2, result.Document.Root.Prop("list").Count);
    }

    [Test]
    public void Utf8WithoutBom()
    {
        var path = WriteText("plain.json", Sample, new UTF8Encoding(false), bom: false);
        var result = Load(path);
        AssertSampleContent(result);
        Assert.Equal(65001, result.Format.Encoding.CodePage);
        Assert.False(result.Format.HasByteOrderMark);
        Assert.Equal("UTF-8", result.Format.EncodingDisplayName);
        Assert.Equal(new FileInfo(path).Length, result.FileSize);
        Assert.Equal(5L, result.NodeCount);
        Assert.Empty(result.Diagnostics);
    }

    [Test]
    public void Utf8WithBom()
    {
        var path = WriteText("bom.json", Sample, new UTF8Encoding(true), bom: true);
        Assert.Equal(0xEF, File.ReadAllBytes(path)[0]);
        var result = Load(path);
        AssertSampleContent(result);
        Assert.Equal(65001, result.Format.Encoding.CodePage);
        Assert.True(result.Format.HasByteOrderMark);
        Assert.Equal("UTF-8 BOM", result.Format.EncodingDisplayName);
    }

    [Test]
    public void Utf16LittleEndianWithBom()
    {
        var path = WriteText("le.json", Sample, new UnicodeEncoding(bigEndian: false, byteOrderMark: true), bom: true);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);

        var result = Load(path);
        AssertSampleContent(result);
        Assert.Equal(1200, result.Format.Encoding.CodePage);
        Assert.True(result.Format.HasByteOrderMark);
        Assert.Equal("UTF-16 LE BOM", result.Format.EncodingDisplayName);
        Assert.Equal("\n", result.Format.NewLine);
    }

    [Test]
    public void Utf16BigEndianWithBom()
    {
        var path = WriteText("be.json", Sample, new UnicodeEncoding(bigEndian: true, byteOrderMark: true), bom: true);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0xFE, bytes[0]);
        Assert.Equal(0xFF, bytes[1]);

        var result = Load(path);
        AssertSampleContent(result);
        Assert.Equal(1201, result.Format.Encoding.CodePage);
        Assert.Equal("UTF-16 BE BOM", result.Format.EncodingDisplayName);
    }

    [Test]
    public void Utf16WithoutBomIsSniffed()
    {
        var le = Load(WriteText("le-nobom.json", Sample, new UnicodeEncoding(false, false), bom: false));
        AssertSampleContent(le);
        Assert.Equal(1200, le.Format.Encoding.CodePage);
        Assert.False(le.Format.HasByteOrderMark);

        var be = Load(WriteText("be-nobom.json", Sample, new UnicodeEncoding(true, false), bom: false));
        AssertSampleContent(be);
        Assert.Equal(1201, be.Format.Encoding.CodePage);
    }

    [Test]
    public void DetectsTwoSpaceIndentAndLf()
    {
        var path = WriteText("two.json", "{\n  \"a\": {\n    \"b\": 1\n  }\n}\n", new UTF8Encoding(false), false);
        var f = Load(path).Format;
        Assert.Equal(2, f.IndentSize);
        Assert.False(f.UseTabs);
        Assert.Equal("\n", f.NewLine);
        Assert.Equal("LF", f.NewLineDisplayName);
        Assert.False(f.WasCompact);
        Assert.True(f.HadTrailingNewLine);
    }

    [Test]
    public void DetectsFourSpaceIndentAndCrLf()
    {
        var path = WriteText("four.json", "{\r\n    \"a\": {\r\n        \"b\": 1\r\n    }\r\n}", new UTF8Encoding(false), false);
        var f = Load(path).Format;
        Assert.Equal(4, f.IndentSize);
        Assert.False(f.UseTabs);
        Assert.Equal("\r\n", f.NewLine);
        Assert.Equal("CRLF", f.NewLineDisplayName);
        Assert.False(f.HadTrailingNewLine);
    }

    [Test]
    public void DetectsTabs()
    {
        var path = WriteText("tabs.json", "{\n\t\"a\": [\n\t\t1\n\t]\n}\n", new UTF8Encoding(false), false);
        var f = Load(path).Format;
        Assert.True(f.UseTabs);
        Assert.False(f.WasCompact);
        var options = f.ToFormatOptions(FormatOptions.Default);
        Assert.True(options.UseTabs);
        Assert.Equal("\n", options.NewLine);
    }

    [Test]
    public void DetectsCompactFile()
    {
        var path = WriteText("min.json", "{\"a\":[1,2,{\"b\":null}]}", new UTF8Encoding(false), false);
        var f = Load(path).Format;
        Assert.True(f.WasCompact);
        Assert.False(f.HadTrailingNewLine);
        var options = f.ToFormatOptions(FormatOptions.Default);
        Assert.False(options.Indented);
    }

    [Test]
    public void EmptyFileYieldsEmptyObjectAndInfoDiagnostic()
    {
        foreach (var (name, content) in new[] { ("empty.json", Array.Empty<byte>()), ("ws.json", "  \r\n\t "u8.ToArray()), ("bomonly.json", new byte[] { 0xEF, 0xBB, 0xBF }) })
        {
            var result = Load(WriteBytes(name, content));
            Assert.Equal(JNodeKind.Object, result.Document.Root.Kind, name);
            Assert.Equal(0, result.Document.Root.Count, name);
            var info = Assert.Single(result.Diagnostics, name);
            Assert.Equal(DiagnosticSeverity.Information, info.Severity, name);
            Assert.Equal("$", info.Path, name);
            Assert.Equal(1L, result.NodeCount, name);
        }
    }

    [Test]
    public void ProgressIsReportedFromZeroToTotal()
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < 50_000; i++)
        {
            sb.Append(i == 0 ? string.Empty : ",").Append("{\"i\":").Append(i).Append('}');
        }
        sb.Append(']');
        var path = WriteText("progress.json", sb.ToString(), new UTF8Encoding(false), false);
        long length = new FileInfo(path).Length;

        var progress = new RecordingProgress<LoadProgress>();
        var result = JsonLoader.LoadFile(path, ParseOptions.Default, progress, CancellationToken.None);

        Assert.Equal(50_000, result.Document.Root.Count);
        var reports = progress.Reports;
        Assert.True(reports.Count >= 2, "at least a start and a completion report");
        Assert.Equal(new LoadProgress(0, length), reports[0]);
        Assert.Equal(new LoadProgress(length, length), reports[^1]);
        Assert.Equal(1.0, reports[^1].Fraction);
        for (int i = 1; i < reports.Count; i++)
        {
            Assert.True(reports[i].BytesRead >= reports[i - 1].BytesRead, "progress never goes backwards");
        }
        Assert.Equal(0.0, new LoadProgress(5, 0).Fraction);
    }

    [Test]
    public async Task LoadFileAsyncWorksAndHonoursCancellation()
    {
        var path = WriteText("async.json", Sample, new UTF8Encoding(false), false);
        var result = await JsonLoader.LoadFileAsync(path, ParseOptions.Default);
        AssertSampleContent(result);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => JsonLoader.LoadFileAsync(path, ParseOptions.Default, null, cts.Token));
    }

    [Test]
    public void ParseErrorsAndWarningsPropagate()
    {
        var bad = WriteText("bad.json", "{\n  \"a\": nope\n}", new UTF8Encoding(false), false);
        var ex = Assert.Throws<JsonParseException>(() => Load(bad));
        Assert.Equal(2L, ex.Line);

        var dup = WriteText("dup.json", "{\"a\":1,\"a\":2}", new UTF8Encoding(false), false);
        var warning = Assert.Single(Load(dup).Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("$.a", warning.Path);

        Assert.Throws<FileNotFoundException>(() => Load(_temp.File("missing.json")));
    }

    [Test]
    public void LargeFileTrailingNewLineIsDetectedFromTheEnd()
    {
        // Bigger than the 64 KB style sample, so the trailing newline must be read from the file's tail.
        var sb = new StringBuilder("[\n");
        for (int i = 0; i < 20_000; i++)
        {
            sb.Append("  ").Append(i).Append(",\n");
        }
        sb.Append("  0\n]");
        var noNewLine = Load(WriteText("big1.json", sb.ToString(), new UTF8Encoding(false), false));
        Assert.False(noNewLine.Format.HadTrailingNewLine);

        var withNewLine = Load(WriteText("big2.json", sb + "\n", new UTF8Encoding(false), false));
        Assert.True(withNewLine.Format.HadTrailingNewLine);
        Assert.Equal(20_001, withNewLine.Document.Root.Count);
    }

    [Test]
    public void FormatDetectorEncodingTable()
    {
        Assert.Equal((1200, 2), Sniff(0xFF, 0xFE, 0x7B, 0x00));
        Assert.Equal((1201, 2), Sniff(0xFE, 0xFF, 0x00, 0x7B));
        Assert.Equal((65001, 3), Sniff(0xEF, 0xBB, 0xBF, 0x7B));
        Assert.Equal((12000, 4), Sniff(0xFF, 0xFE, 0x00, 0x00));
        Assert.Equal((12001, 4), Sniff(0x00, 0x00, 0xFE, 0xFF));
        Assert.Equal((65001, 0), Sniff(0x7B, 0x7D));
        Assert.Equal((65001, 0), Sniff());

        static (int, int) Sniff(params byte[] head)
        {
            var (encoding, bom) = FormatDetector.DetectEncoding(head);
            return (encoding.CodePage, bom);
        }
    }
}
