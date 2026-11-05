using System.Text;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

public sealed class SaverTests : IDisposable
{
    private static readonly JNode Sample = JsonParser.Parse("{\"name\":\"café\",\"list\":[1,2.50,{\"x\":null}]}");

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static FileFormatInfo Utf8(bool bom) => new() { Encoding = new UTF8Encoding(bom), HasByteOrderMark = bom };

    private void AssertNoTempFiles()
    {
        var leftovers = Directory.GetFiles(_temp.Path, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(leftovers, "atomic save must clean up its temporary file");
    }

    [Test]
    public void SaveWritesFileContent()
    {
        var path = _temp.File("out.json");
        JsonFileSaver.Save(Sample, path, Utf8(false), FormatOptions.Default, CancellationToken.None);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(JsonDocumentWriter.ToJson(Sample), Encoding.UTF8.GetString(bytes));
        Assert.NotEqual(0xEF, bytes[0]);
        AssertNoTempFiles();
    }

    [Test]
    public void Utf8BomIsWrittenOnlyWhenRequested()
    {
        var withBom = _temp.File("bom.json");
        JsonFileSaver.Save(Sample, withBom, Utf8(true), FormatOptions.Compact, CancellationToken.None);
        var bytes = File.ReadAllBytes(withBom);
        Assert.SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        Assert.Equal(Json.Compact(Sample), Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));

        // A BOM-emitting encoding object must not force a BOM when the format says there is none.
        var noBom = _temp.File("nobom.json");
        JsonFileSaver.Save(Sample, noBom, new FileFormatInfo { Encoding = new UTF8Encoding(true), HasByteOrderMark = false }, FormatOptions.Compact, CancellationToken.None);
        Assert.Equal((byte)'{', File.ReadAllBytes(noBom)[0]);
    }

    [Test]
    public void Utf16EncodingsArePreserved()
    {
        var le = _temp.File("le.json");
        JsonFileSaver.Save(Sample, le, new FileFormatInfo { Encoding = new UnicodeEncoding(false, true), HasByteOrderMark = true }, FormatOptions.Compact, CancellationToken.None);
        var leBytes = File.ReadAllBytes(le);
        Assert.SequenceEqual(new byte[] { 0xFF, 0xFE, (byte)'{', 0 }, leBytes.Take(4));
        Assert.Equal(Json.Compact(Sample), Encoding.Unicode.GetString(leBytes, 2, leBytes.Length - 2));

        var be = _temp.File("be.json");
        JsonFileSaver.Save(Sample, be, new FileFormatInfo { Encoding = new UnicodeEncoding(true, true), HasByteOrderMark = true }, FormatOptions.Compact, CancellationToken.None);
        Assert.SequenceEqual(new byte[] { 0xFE, 0xFF, 0, (byte)'{' }, File.ReadAllBytes(be).Take(4));
    }

    [Test]
    public void LoadThenSaveReproducesTheOriginalBytes()
    {
        var cases = new (string Name, FormatOptions Options, Encoding Encoding)[]
        {
            ("utf8-bom-4-lf.json", FormatOptions.Default with { IndentSize = 4, NewLine = "\n" }, new UTF8Encoding(true)),
            ("utf8-tabs-crlf.json", FormatOptions.Default with { UseTabs = true }, new UTF8Encoding(false)),
            ("utf8-compact.json", FormatOptions.Compact, new UTF8Encoding(false)),
            ("utf16be-2.json", FormatOptions.Default with { TrailingNewLine = false }, new UnicodeEncoding(true, true)),
        };
        foreach (var (name, options, encoding) in cases)
        {
            var path = _temp.File(name);
            byte[] original = [.. encoding.GetPreamble(), .. encoding.GetBytes(JsonDocumentWriter.ToJson(Sample, options))];
            File.WriteAllBytes(path, original);

            var loaded = JsonLoader.LoadFile(path, ParseOptions.Default, null, CancellationToken.None);
            JsonFileSaver.Save(loaded.Document.CreateSnapshot(), path, loaded.Format, loaded.Format.ToFormatOptions(FormatOptions.Default), CancellationToken.None);

            Assert.SequenceEqual(original, File.ReadAllBytes(path), name);
        }
        AssertNoTempFiles();
    }

    [Test]
    public void OverwritesExistingFile()
    {
        var path = _temp.File("existing.json");
        File.WriteAllText(path, new string('x', 10_000));
        JsonFileSaver.Save(JsonParser.Parse("[1]"), path, Utf8(false), FormatOptions.Compact, CancellationToken.None);
        Assert.Equal("[1]", File.ReadAllText(path), "a shorter file must not keep stale bytes");

        JsonFileSaver.Save(JsonParser.Parse("[2]"), path, Utf8(false), FormatOptions.Compact, CancellationToken.None);
        Assert.Equal("[2]", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_temp.Path));
        AssertNoTempFiles();
    }

    [Test]
    public void ReadOnlyTargetFailsWithoutLeavingTempFiles()
    {
        var path = _temp.File("readonly.json");
        File.WriteAllText(path, "{}");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<Exception>(() => JsonFileSaver.Save(Sample, path, Utf8(false), FormatOptions.Compact, CancellationToken.None));
            Assert.Equal("{}", File.ReadAllText(path), "the original is untouched");
            AssertNoTempFiles();
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Test]
    public void CreatesMissingDirectories()
    {
        var path = Path.Combine(_temp.Path, "a", "b", "c.json");
        JsonFileSaver.Save(Sample, path, Utf8(false), FormatOptions.Compact, CancellationToken.None);
        Assert.Equal(Json.Compact(Sample), File.ReadAllText(path));
    }

    [Test]
    public void CancelledSaveLeavesOriginalAndNoTempFile()
    {
        var path = _temp.File("cancel.json");
        File.WriteAllText(path, "\"original\"");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => JsonFileSaver.Save(Sample, path, Utf8(false), FormatOptions.Compact, cts.Token));
        Assert.Equal("\"original\"", File.ReadAllText(path));
        AssertNoTempFiles();
    }

    [Test]
    public async Task SaveAsyncWritesSnapshot()
    {
        var path = _temp.File("async.json");
        var doc = new JDocument(Sample.DeepClone());
        var snapshot = doc.CreateSnapshot();
        var task = JsonFileSaver.SaveAsync(snapshot, path, Utf8(false), FormatOptions.Compact);
        doc.SetValue(doc.Root.Prop("name"), "changed while saving");
        await task;
        Assert.Equal(Json.Compact(Sample), await File.ReadAllTextAsync(path));
        AssertNoTempFiles();
    }

    [Test]
    public void ManySavesNeverLeaveTempFiles()
    {
        var path = _temp.File("many.json");
        for (int i = 0; i < 20; i++)
        {
            JsonFileSaver.Save(JNode.CreateNumber(i.ToString(System.Globalization.CultureInfo.InvariantCulture)), path, Utf8(i % 2 == 0), FormatOptions.Compact, CancellationToken.None);
        }
        Assert.Equal("19", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_temp.Path));
    }
}
