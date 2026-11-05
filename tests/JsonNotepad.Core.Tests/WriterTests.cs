using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

public sealed class WriterTests
{
    private const string SampleCompact = """{"a":1,"b":[true,null],"c":{},"d":[],"e":"x"}""";

    private static readonly FormatOptions Lf = FormatOptions.Default with { NewLine = "\n" };

    private static readonly string SampleIndented =
        "{\n" +
        "  \"a\": 1,\n" +
        "  \"b\": [\n" +
        "    true,\n" +
        "    null\n" +
        "  ],\n" +
        "  \"c\": {},\n" +
        "  \"d\": [],\n" +
        "  \"e\": \"x\"\n" +
        "}\n";

    [Test]
    public void IndentedOutputIsExact()
    {
        var root = JsonParser.Parse(SampleCompact);
        Assert.Equal(SampleIndented, JsonDocumentWriter.ToJson(root, Lf));
        Assert.Equal(SampleIndented.Replace("\n", "\r\n", StringComparison.Ordinal), JsonDocumentWriter.ToJson(root), "default newline is CRLF");
    }

    [Test]
    public void CompactOutputIsExact()
    {
        var root = JsonParser.Parse(SampleIndented);
        Assert.Equal(SampleCompact, JsonDocumentWriter.ToJson(root, FormatOptions.Compact));
    }

    [Test]
    public void IndentSizeTabsAndTrailingNewLineOptions()
    {
        var root = JsonParser.Parse("""{"a":[1]}""");
        Assert.Equal("{\n    \"a\": [\n        1\n    ]\n}\n", JsonDocumentWriter.ToJson(root, Lf with { IndentSize = 4 }));
        Assert.Equal("{\n\t\"a\": [\n\t\t1\n\t]\n}\n", JsonDocumentWriter.ToJson(root, Lf with { UseTabs = true }));
        Assert.Equal("{\n  \"a\": [\n    1\n  ]\n}", JsonDocumentWriter.ToJson(root, Lf with { TrailingNewLine = false }));
    }

    [Test]
    public void PrimitiveAndEmptyRoots()
    {
        Assert.Equal("42", JsonDocumentWriter.ToJson(JNode.CreateNumber("42"), FormatOptions.Compact));
        Assert.Equal("\"s\"\n", JsonDocumentWriter.ToJson(JNode.CreateString("s"), Lf));
        Assert.Equal("{}", JsonDocumentWriter.ToJson(JNode.CreateObject(), FormatOptions.Compact));
        Assert.Equal("[]\n", JsonDocumentWriter.ToJson(JNode.CreateArray(), Lf));
        Assert.Equal("null", JsonDocumentWriter.ToJson(JNode.CreateNull(), FormatOptions.Compact));
        Assert.Equal("false", JsonDocumentWriter.ToJson(JNode.CreateBoolean(false), FormatOptions.Compact));
    }

    [Test]
    public void EscapesQuotesBackslashesAndControlCharacters()
    {
        var input = "q\"b\\s/n\nr\rt\tb\bf\f" + (char)0x01 + (char)0x1F + "end";
        Assert.Equal("\"q\\\"b\\\\s/n\\nr\\rt\\tb\\bf\\f\\u0001\\u001fend\"", JsonDocumentWriter.QuoteString(input));
        Assert.Equal("\"\\u0000\"", JsonDocumentWriter.QuoteString("\0"));
        Assert.Equal("\"\"", JsonDocumentWriter.QuoteString(string.Empty));
    }

    [Test]
    public void EscapesLineAndParagraphSeparators()
    {
        // U+2028/U+2029 are valid in JSON but break JavaScript string literals, so they are always escaped.
        var input = "a" + (char)0x2028 + "b" + (char)0x2029 + "c";
        Assert.Equal("\"a\\u2028b\\u2029c\"", JsonDocumentWriter.QuoteString(input));
    }

    [Test]
    public void NonAsciiIsKeptUnlessEscapeNonAscii()
    {
        var input = "caf\u00e9 \ud83d\ude00" + (char)0x7F;
        Assert.Equal("\"" + input + "\"", JsonDocumentWriter.QuoteString(input));
        Assert.Equal("\"caf\\u00e9 \\ud83d\\ude00\\u007f\"", JsonDocumentWriter.QuoteString(input, escapeNonAscii: true));

        var root = JsonParser.Parse("{\"k\u00e9y\":\"\u4e2d\"}");
        Assert.Equal("{\"k\\u00e9y\":\"\\u4e2d\"}", JsonDocumentWriter.ToJson(root, FormatOptions.Compact with { EscapeNonAscii = true }));
    }

    [Test]
    public void NamesAreEscapedLikeValues()
    {
        var root = JsonParser.Parse("""{"a\"b\\c":1}""");
        Assert.Equal("""{"a\"b\\c":1}""", Json.Compact(root));
    }

    [Test]
    public void RoundTripPreservesEverything()
    {
        const string text = """
            {
              "string": "line\nbreak \"quoted\" \\ back \u0001 \u00e9 \ud83d\ude00",
              "numbers": [0, -0, 1.10, 12345678901234567890, 1e400, -2.5E-3],
              "bools": [true, false],
              "nothing": null,
              "nested": { "a": { "b": [[], {}, [1, [2, [3]]]] } },
              "": "empty key",
              "unicode \u4e2d": "\u2028"
            }
            """;
        var original = JsonParser.Parse(text);
        foreach (var options in new[] { FormatOptions.Default, FormatOptions.Compact, Lf with { UseTabs = true }, Lf with { EscapeNonAscii = true, IndentSize = 3 } })
        {
            var written = JsonDocumentWriter.ToJson(original, options);
            var reparsed = JsonParser.Parse(written);
            Assert.True(JsonEquality.DeepEquals(original, reparsed), "round trip with " + options);
            Assert.Equal(written, JsonDocumentWriter.ToJson(reparsed, options), "serialization is idempotent");
            Assert.SequenceEqual(
                original.DescendantsAndSelf().Select(n => n.Value),
                reparsed.DescendantsAndSelf().Select(n => n.Value),
                "literals survive byte-for-byte");
        }
    }

    [Test]
    public void NodeLinesMapEachNodeToItsStartLine()
    {
        var root = JsonParser.Parse(SampleCompact);
        var lines = new List<int>();
        using var writer = new StringWriter();
        JsonDocumentWriter.Write(root, writer, Lf, lines, CancellationToken.None);

        // root, a, b, true, null, c, d, e  (line 5 is the closing "]," of b)
        Assert.SequenceEqual(new[] { 0, 1, 2, 3, 4, 6, 7, 8 }, lines);
        Assert.Equal(root.DescendantsAndSelf().Count(), lines.Count);

        // Every recorded line actually contains the node it points to.
        var textLines = writer.ToString().Split('\n');
        int i = 0;
        foreach (var node in root.DescendantsAndSelf())
        {
            var line = textLines[lines[i++]];
            if (node.Name is { } name)
            {
                Assert.Contains("\"" + name + "\"", line);
            }
            else if (node.Kind is JNodeKind.Boolean or JNodeKind.Null)
            {
                Assert.Contains(node.Kind == JNodeKind.Null ? "null" : node.Value!, line);
            }
        }
    }

    [Test]
    public void NodeLinesInCompactModeAreAllZero()
    {
        var root = JsonParser.Parse(SampleCompact);
        var lines = new List<int>();
        JsonDocumentWriter.Write(root, TextWriter.Null, FormatOptions.Compact, lines, CancellationToken.None);
        Assert.Equal(8, lines.Count);
        Assert.True(lines.All(l => l == 0));
    }

    [Test]
    public void FormattedTextIndexesLines()
    {
        var root = JsonParser.Parse(SampleCompact);
        var ft = FormattedText.Build(root, FormatOptions.Default, sourceVersion: 7, CancellationToken.None);

        Assert.Equal(SampleIndented, ft.Text, "always LF internally, regardless of the file's newline");
        Assert.Equal(7L, ft.SourceVersion);
        Assert.Equal(10, ft.LineCount);
        Assert.Equal(0, ft.LineStarts[0]);
        Assert.Equal(2, ft.LineStarts[1]);
        Assert.Equal("{", ft.GetLine(0).ToString());
        Assert.Equal("  \"a\": 1,", ft.GetLine(1).ToString());
        Assert.Equal("  ],", ft.GetLine(5).ToString());
        Assert.Equal("}", ft.GetLine(9).ToString());
        for (int line = 0; line < ft.LineCount; line++)
        {
            Assert.Equal(ft.Text.Split('\n')[line], ft.GetLine(line).ToString());
        }
        Assert.Equal(SampleIndented.Split('\n').Max(l => l.Length), ft.MaxLineLength);
        Assert.Equal(10, ft.MaxLineLength);
        Assert.SequenceEqual(new[] { 0, 1, 2, 3, 4, 6, 7, 8 }, ft.NodeLines);
    }

    [Test]
    public void FormattedTextMaxLineLengthWithoutTrailingNewLine()
    {
        var root = JsonParser.Parse("""{"short":1,"a much longer member name":"and a value"}""");
        var ft = FormattedText.Build(root, FormatOptions.Default with { TrailingNewLine = false }, 1, CancellationToken.None);
        Assert.Equal(4, ft.LineCount);
        Assert.Equal("  \"a much longer member name\": \"and a value\"".Length, ft.MaxLineLength);

        var compact = FormattedText.Build(root, FormatOptions.Compact, 1, CancellationToken.None);
        Assert.Equal(1, compact.LineCount);
        Assert.Equal(compact.Text.Length, compact.MaxLineLength);
    }

    [Test]
    public void FindNodeIndexAtLine()
    {
        var ft = FormattedText.Build(JsonParser.Parse(SampleCompact), FormatOptions.Default, 1, CancellationToken.None);
        Assert.Equal(0, ft.FindNodeIndexAtLine(0));
        Assert.Equal(1, ft.FindNodeIndexAtLine(1));
        Assert.Equal(3, ft.FindNodeIndexAtLine(3));
        Assert.Equal(4, ft.FindNodeIndexAtLine(5), "a closing-bracket line maps to the last node before it");
        Assert.Equal(5, ft.FindNodeIndexAtLine(6));
        Assert.Equal(7, ft.FindNodeIndexAtLine(9));
        Assert.Equal(7, ft.FindNodeIndexAtLine(1000));

        var compact = FormattedText.Build(JsonParser.Parse(SampleCompact), FormatOptions.Compact, 1, CancellationToken.None);
        Assert.Equal(0, compact.FindNodeIndexAtLine(0), "several nodes on one line: pick the first");

        Assert.Equal(-1, FormattedText.Empty.FindNodeIndexAtLine(0));
        Assert.Equal(1, FormattedText.Empty.LineCount);
    }

    [Test]
    public void FormattedTextHonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => FormattedText.Build(JsonParser.Parse("[1]"), FormatOptions.Default, 1, cts.Token));
    }
}
