using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

public sealed class ParserTests
{
    [Test]
    public void ParsesEveryValueKind()
    {
        var root = JsonParser.Parse("""{"s":"text","n":1.5,"t":true,"f":false,"z":null,"o":{},"a":[]}""");

        Assert.Equal(JNodeKind.Object, root.Kind);
        Assert.Equal(7, root.Count);
        Assert.Null(root.Name);
        Assert.Equal(JNodeKind.String, root.Prop("s").Kind);
        Assert.Equal("text", root.Prop("s").Value);
        Assert.Equal(JNodeKind.Number, root.Prop("n").Kind);
        Assert.Equal("1.5", root.Prop("n").Value);
        Assert.Equal(JNodeKind.Boolean, root.Prop("t").Kind);
        Assert.True(root.Prop("t").BooleanValue);
        Assert.False(root.Prop("f").BooleanValue);
        Assert.Equal("false", root.Prop("f").Value);
        Assert.Equal(JNodeKind.Null, root.Prop("z").Kind);
        Assert.Null(root.Prop("z").Value);
        Assert.Equal(JNodeKind.Object, root.Prop("o").Kind);
        Assert.Equal(0, root.Prop("o").Count);
        Assert.Equal(JNodeKind.Array, root.Prop("a").Kind);
        Assert.Equal(0, root.Prop("a").Count);
    }

    [Test]
    public void ParsesPrimitiveRoots()
    {
        Assert.Equal("hello", JsonParser.Parse("\"hello\"").Value);
        Assert.Equal("42", JsonParser.Parse(" 42 ").Value);
        Assert.Equal(JNodeKind.Boolean, JsonParser.Parse("true").Kind);
        Assert.Equal(JNodeKind.Null, JsonParser.Parse("null").Kind);
    }

    [Test]
    public void ParsesNestedStructureWithParentsAndNames()
    {
        var root = JsonParser.Parse("""{"a":{"b":[1,{"c":[[],[2]]}]}}""");

        var b = root.Prop("a").Prop("b");
        Assert.Equal(2, b.Count);
        Assert.Null(b[0].Name, "array items have no name");
        var c = b[1].Prop("c");
        Assert.Equal("2", c[1][0].Value);
        Assert.Same(c, c[1].Parent);
        Assert.Same(root, c.Root);
        Assert.Equal(6, c[1][0].Depth);
        Assert.True(root.IsAncestorOf(c[1][0]));
        Assert.False(c.IsAncestorOf(root));
    }

    [Test]
    public void NumberLiteralsArePreservedVerbatim()
    {
        string[] literals = ["12345678901234567890", "1.10", "-0", "1e400", "1E+2", "-1.5e-7", "0.000001", "100000000000000000000000000000.0"];
        var root = JsonParser.Parse("[" + string.Join(",", literals) + "]");

        Assert.SequenceEqual(literals, root.Children.Select(c => c.Value!));
        Assert.True(root.Children.All(c => c.Kind == JNodeKind.Number));
        Assert.Equal("[" + string.Join(",", literals) + "]", Json.Compact(root), "writer must emit the literal text");
    }

    [Test]
    public void StringEscapesAreDecoded()
    {
        var root = JsonParser.Parse("""["\"\\\/\b\f\n\r\t", "é中", "😀", "a\u0000b"]""");

        Assert.Equal("\"\\/\b\f\n\r\t", root[0].Value);
        Assert.Equal("é中", root[1].Value);
        Assert.Equal("😀", root[2].Value, "surrogate pair escapes combine into one code point");
        Assert.Equal(1, root[2].Value!.EnumerateRunes().Count());
        Assert.Equal(0x1F600, root[2].Value!.EnumerateRunes().First().Value);
        Assert.Equal("a\0b", root[3].Value);
    }

    [Test]
    public void EscapedPropertyNamesAreDecoded()
    {
        var root = JsonParser.Parse("""{"a\"b":1,"é":2,"tab\there":3}""");
        Assert.Equal("a\"b", root[0].Name);
        Assert.Equal("é", root[1].Name);
        Assert.Equal("tab\there", root[2].Name);
    }

    [Test]
    public void RawNonAsciiTextIsDecoded()
    {
        var text = "{\"kéy\":\"café 中文 😀\"}";
        var root = JsonParser.Parse(text);
        Assert.Equal("kéy", root[0].Name);
        Assert.Equal("café 中文 😀", root[0].Value);
    }

    [Test]
    public void CommentsAndTrailingCommasAreAllowedByDefault()
    {
        const string text = """
            // leading comment
            {
              /* block */ "a": [1, 2,], // trailing comma in array
              "b": { "c": true, },
            }
            """;
        var root = JsonParser.Parse(text);
        Assert.Equal("""{"a":[1,2],"b":{"c":true}}""", Json.Compact(root));
    }

    [Test]
    public void StrictOptionsRejectCommentsAndTrailingCommas()
    {
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\"a\":1 // c\n}", ParseOptions.Strict));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("/* c */ 1", ParseOptions.Strict));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("[1,2,]", ParseOptions.Strict));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\"a\":1,}", ParseOptions.Strict));

        // Individually configurable.
        var commentsOnly = ParseOptions.Strict with { AllowComments = true };
        Assert.Equal("1", JsonParser.Parse("/* c */ 1", commentsOnly).Value);
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("[1,]", commentsOnly));
    }

    [Test]
    public void DuplicateKeysProduceWarningWithPath()
    {
        var result = JsonParser.ParseWithDiagnostics("""{"o":{"k":1,"k":2},"list":[{"x":1,"x":2}],"a b":{"q":0,"q":0}}""");

        Assert.Equal(3, result.Warnings.Count);
        Assert.True(result.Warnings.All(w => w.Severity == DiagnosticSeverity.Warning));
        Assert.Equal("$.o.k", result.Warnings[0].Path);
        Assert.Equal("$.list[0].x", result.Warnings[1].Path);
        Assert.Equal("$['a b'].q", result.Warnings[2].Path);
        Assert.Contains("Duplicate key \"k\"", result.Warnings[0].Message);
        Assert.Equal("Parser", result.Warnings[0].Source);

        // Both members stay in the model so nothing the user wrote is silently dropped.
        Assert.Equal(2, result.Root.Prop("o").Count);
    }

    [Test]
    public void DuplicateKeyDetectionCanBeDisabledAndIsCapped()
    {
        var off = JsonParser.ParseWithDiagnostics("""{"k":1,"k":2}""", ParseOptions.Default with { DetectDuplicateKeys = false });
        Assert.Empty(off.Warnings);

        var capped = JsonParser.ParseWithDiagnostics("""{"k":1,"k":2,"k":3,"k":4,"k":5}""", ParseOptions.Default with { MaxWarnings = 2 });
        Assert.Equal(2, capped.Warnings.Count);
    }

    [Test]
    public void SameKeyInSiblingObjectsIsNotADuplicate()
    {
        var result = JsonParser.ParseWithDiagnostics("""[{"id":1},{"id":2},{"id":3,"inner":{"id":4}}]""");
        Assert.Empty(result.Warnings);
    }

    [Test]
    public void MalformedInputReportsLineAndColumn()
    {
        var ex = Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\n  \"a\": 1,\n  \"b\": }"));
        Assert.Equal(3L, ex.Line);
        Assert.Equal(8L, ex.Column);
        Assert.DoesNotContain("LineNumber:", ex.Message, "location suffix of System.Text.Json is stripped");
        Assert.True(ex.Message.Length > 0);
        Assert.NotNull(ex.InnerException);

        var ex2 = Assert.Throws<JsonParseException>(() => JsonParser.Parse("[1, 2 3]"));
        Assert.Equal(1L, ex2.Line);
        Assert.Equal(7L, ex2.Column);
    }

    [Test]
    public void UnterminatedInputThrows()
    {
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\"a\": [1, 2"));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("\"open string"));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\"a\" 1}"));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("[tru]"));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("[01]"));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("{'a':1}"));
    }

    [Test]
    public void TryParseReturnsErrorWithLocation()
    {
        Assert.True(JsonParser.TryParse("[1]", null, out var ok, out var noError));
        Assert.NotNull(ok);
        Assert.Null(noError);

        Assert.False(JsonParser.TryParse("[1,\n x]", null, out var root, out var error));
        Assert.Null(root);
        Assert.Contains("line 2", error);
    }

    [Test]
    public void MaxDepthIsEnforced()
    {
        var options = ParseOptions.Default with { MaxDepth = 3 };
        Assert.Equal(JNodeKind.Array, JsonParser.Parse("[[[1]]]", options).Kind);
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("[[[[1]]]]", options));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("{\"a\":{\"b\":{\"c\":{}}}}", options));
    }

    [Test]
    public void DefaultMaxDepthAllowsDeepDocuments()
    {
        const int depth = 1000;
        var text = new string('[', depth) + new string(']', depth);
        var root = JsonParser.Parse(text);
        var n = root;
        int levels = 1;
        while (n.HasChildren)
        {
            n = n[0];
            levels++;
        }
        Assert.Equal(depth, levels);
        Assert.Equal(text, Json.Compact(root), "the writer is iterative too");
    }

    [Test]
    public void EmptyInputIsAnError()
    {
        Assert.Throws<JsonParseException>(() => JsonParser.Parse(string.Empty));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("   \r\n\t "));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("// only a comment"));
    }

    [Test]
    public void MultipleTopLevelValuesAreAnError()
    {
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("{} {}"));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("1 2"));
        Assert.Throws<JsonParseException>(() => JsonParser.Parse("[1]\n[2]"));
    }

    [Test]
    public void PropertyNamesAreInterned()
    {
        var root = JsonParser.Parse("""[{"name":1,"id":2},{"name":3,"id":4},{"name":5}]""");

        Assert.Same(root[0][0].Name, root[1][0].Name);
        Assert.Same(root[0][1].Name, root[1][1].Name);
        Assert.Same(root[0][0].Name, root[2][0].Name, "escaped names are interned as well");
    }

    [Test]
    public void ShortStringValuesAreInterned()
    {
        var root = JsonParser.Parse("""[{"status":"active"},{"status":"active"}]""");
        Assert.Same(root[0][0].Value, root[1][0].Value);
    }

    [Test]
    public void NodeCountCountsEveryValue()
    {
        var result = JsonParser.ParseWithDiagnostics("""{"a":[1,2,{"b":null}],"c":"x"}""");
        Assert.Equal(7L, result.NodeCount);
        Assert.Equal(7L, (long)result.Root.DescendantsAndSelf().Count());
    }
}
