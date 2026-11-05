using System.Diagnostics;
using System.Globalization;
using System.Text;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Search;
using JsonNotepad.Core.Services;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

/// <summary>
/// Smoke tests on a realistic large file (~200k records, ~35 MB). They assert that
/// things complete and are not pathologically slow, and log throughput for the record.
/// Exclude them with <c>--exclude-category perf</c>.
/// </summary>
[Category("perf")]
public sealed class PerformanceSmokeTests : IDisposable
{
    private const int Records = 200_000;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string GenerateFile()
    {
        var path = _temp.File("big.json");
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false), 1 << 20);
        writer.Write("[\n");
        for (int i = 0; i < Records; i++)
        {
            if (i > 0)
            {
                writer.Write(",\n");
            }
            writer.Write(string.Create(CultureInfo.InvariantCulture,
                $"{{\"id\":{i},\"name\":\"Customer {i}\",\"active\":{(i % 3 == 0 ? "false" : "true")},\"balance\":{i * 1.37:F2},\"tags\":[\"alpha\",\"beta\",\"tier-{i % 5}\"],\"address\":{{\"city\":\"City {i % 977}\",\"zip\":\"{10000 + (i % 89999)}\",\"geo\":[{i % 90}.123456,-{i % 180}.654321]}}}}"));
        }
        writer.Write("\n]\n");
        return path;
    }

    private static string Rate(long bytes, TimeSpan elapsed) =>
        string.Create(CultureInfo.InvariantCulture, $"{bytes / 1048576.0:F1} MB in {elapsed.TotalMilliseconds:F0} ms = {bytes / 1048576.0 / Math.Max(elapsed.TotalSeconds, 1e-6):F0} MB/s");

    [Test]
    public void LoadLargeFile()
    {
        var sw = Stopwatch.StartNew();
        var path = GenerateFile();
        long size = new FileInfo(path).Length;
        TestContext.Log(string.Create(CultureInfo.InvariantCulture, $"generated {size / 1048576.0:F1} MB in {sw.ElapsedMilliseconds} ms"));
        Assert.True(size > 30L * 1024 * 1024, $"file should be > 30 MB (is {size})");

        long before = GC.GetTotalMemory(forceFullCollection: true);
        var progress = new RecordingProgress<LoadProgress>();
        var result = JsonLoader.LoadFile(path, ParseOptions.Default, progress, CancellationToken.None);
        long after = GC.GetTotalMemory(forceFullCollection: true);

        // 1 root + per record: object, 6 members, 3 tags, 3 address members, 2 geo items
        long expectedNodes = 1 + (Records * 15L);
        Assert.Equal(expectedNodes, result.NodeCount);
        Assert.Equal(Records, result.Document.Root.Count);
        Assert.Equal("Customer 123456", result.Document.Root[123_456].Prop("name").Value);
        Assert.Equal("-19.654321", result.Document.Root[199_999].Prop("address").Prop("geo")[1].Value);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(size, progress.Reports[^1].BytesRead);
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(10), "load took " + result.Elapsed);

        GC.KeepAlive(result);
        TestContext.Log("load: " + Rate(size, result.Elapsed) + string.Create(CultureInfo.InvariantCulture, $", {result.NodeCount:N0} nodes, ~{(after - before) / 1048576.0:F0} MB managed heap for the tree, {progress.Reports.Count} progress reports"));
    }

    [Test]
    public void CloneWriteSearchAndStatistics()
    {
        var path = GenerateFile();
        long size = new FileInfo(path).Length;
        var doc = JsonLoader.LoadFile(path, ParseOptions.Default, null, CancellationToken.None).Document;

        var sw = Stopwatch.StartNew();
        var snapshot = doc.CreateSnapshot();
        var cloneTime = sw.Elapsed;
        Assert.Equal(doc.Root.Count, snapshot.Count);
        Assert.NotSame(doc.Root[0], snapshot[0]);

        sw.Restart();
        var outPath = _temp.File("out.json");
        JsonFileSaver.Save(snapshot, outPath, FileFormatInfo.Default, FormatOptions.Default with { NewLine = "\n" }, CancellationToken.None);
        var saveTime = sw.Elapsed;
        long outSize = new FileInfo(outPath).Length;

        sw.Restart();
        var text = FormattedText.Build(snapshot, FormatOptions.Default, doc.Version, CancellationToken.None);
        var textTime = sw.Elapsed;
        Assert.Equal((int)doc.CountNodes(), text.NodeLines.Length);

        sw.Restart();
        var match = new FindEngine(new FindOptions { Text = "Customer 199999", WholeWord = true }).FindNext(snapshot, null);
        var findTime = sw.Elapsed;
        Assert.Equal("$[199999].name", Paths.JsonPath.Build(Assert.NotNull(match).Node));

        sw.Restart();
        var stats = DocumentStatistics.Compute(snapshot, CancellationToken.None);
        var statsTime = sw.Elapsed;
        Assert.Equal(1 + (Records * 15L), stats.TotalNodes);
        Assert.Equal(new KeyValuePair<string, long>("active", Records), stats.TopKeys[0]);

        TestContext.Log(string.Create(CultureInfo.InvariantCulture, $"deep clone: {cloneTime.TotalMilliseconds:F0} ms"));
        TestContext.Log("save:   " + Rate(outSize, saveTime));
        TestContext.Log(string.Create(CultureInfo.InvariantCulture, $"text view: {textTime.TotalMilliseconds:F0} ms, {text.LineCount:N0} lines, {text.Text.Length / 1048576.0:F1} M chars"));
        TestContext.Log(string.Create(CultureInfo.InvariantCulture, $"find (last record): {findTime.TotalMilliseconds:F0} ms; statistics: {statsTime.TotalMilliseconds:F0} ms"));
        TestContext.Log(string.Create(CultureInfo.InvariantCulture, $"input {size / 1048576.0:F1} MB, output {outSize / 1048576.0:F1} MB"));

        Assert.True(cloneTime < TimeSpan.FromSeconds(5), "clone took " + cloneTime);
        Assert.True(saveTime < TimeSpan.FromSeconds(10), "save took " + saveTime);
    }
}
