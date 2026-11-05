using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Paths;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

public sealed class JsonPathTests
{
    private const string Tricky = """
        {
          "store": {
            "book": [ { "title": "x", "tags": ["a", "b"] }, { "title": "y" } ],
            "first name": "j",
            "it's": 1,
            "a\\b": 2,
            "1abc": 3,
            "_ok$": 4,
            "": 5,
            "dot.ted": 6,
            "br[ack]et": 7,
            "名前": 8,
            "q\"uote": 9
          },
          "matrix": [[1, 2], [3, [4, 5]]]
        }
        """;

    private static readonly JNode Root = JsonParser.Parse(Tricky);

    private static JNode Store => Root.Prop("store");

    [Test]
    public void BuildRootAndIdentifiers()
    {
        Assert.Equal("$", JsonPath.Build(Root));
        Assert.Equal("$.store", JsonPath.Build(Store));
        Assert.Equal("$.store.book[0].title", JsonPath.Build(Store.Prop("book")[0].Prop("title")));
        Assert.Equal("$.store.book[0].tags[1]", JsonPath.Build(Store.Prop("book")[0].Prop("tags")[1]));
        Assert.Equal("$.store._ok$", JsonPath.Build(Store.Prop("_ok$")));
        Assert.Equal("$.store.名前", JsonPath.Build(Store.Prop("名前")), "unicode letters are identifiers");
        Assert.Equal("$.matrix[1][1][0]", JsonPath.Build(Root.Prop("matrix")[1][1][0]));
    }

    [Test]
    public void BuildQuotesNonIdentifierKeys()
    {
        Assert.Equal("$.store['first name']", JsonPath.Build(Store.Prop("first name")));
        Assert.Equal("$.store['it\\'s']", JsonPath.Build(Store.Prop("it's")));
        Assert.Equal("$.store['a\\\\b']", JsonPath.Build(Store.Prop("a\\b")));
        Assert.Equal("$.store['1abc']", JsonPath.Build(Store.Prop("1abc")));
        Assert.Equal("$.store['']", JsonPath.Build(Store.Prop(string.Empty)));
        Assert.Equal("$.store['dot.ted']", JsonPath.Build(Store.Prop("dot.ted")));
        Assert.Equal("$.store['br[ack]et']", JsonPath.Build(Store.Prop("br[ack]et")));
        Assert.Equal("$.store['q\"uote']", JsonPath.Build(Store.Prop("q\"uote")));
    }

    [Test]
    public void FormatHelpers()
    {
        Assert.Equal("[12]", JsonPath.FormatIndex(12));
        Assert.Equal(".name", JsonPath.FormatMember("name"));
        Assert.Equal("['two words']", JsonPath.FormatMember("two words"));
    }

    [Test]
    public void ResolveSupportedSyntax()
    {
        var title = Store.Prop("book")[0].Prop("title");
        Assert.Same(Root, JsonPath.Resolve(Root, "$"));
        Assert.Same(title, JsonPath.Resolve(Root, "$.store.book[0].title"));
        Assert.Same(title, JsonPath.Resolve(Root, "$['store']['book'][0]['title']"));
        Assert.Same(title, JsonPath.Resolve(Root, "$[\"store\"][\"book\"][0][\"title\"]"));
        Assert.Same(title, JsonPath.Resolve(Root, "  $.store.book[ 0 ].title  "), "whitespace is tolerated");
        Assert.Same(Store.Prop("it's"), JsonPath.Resolve(Root, "$.store['it\\'s']"));
        Assert.Same(Store.Prop("it's"), JsonPath.Resolve(Root, "$.store[\"it's\"]"), "the other quote needs no escape");
        Assert.Same(Store.Prop("first name"), JsonPath.Resolve(Root, "$.store['first name']"));
        Assert.Same(Store.Prop(string.Empty), JsonPath.Resolve(Root, "$.store['']"));
    }

    [Test]
    public void ResolveNegativeIndexes()
    {
        var book = Store.Prop("book");
        Assert.Same(book[1], JsonPath.Resolve(Root, "$.store.book[-1]"));
        Assert.Same(book[0], JsonPath.Resolve(Root, "$.store.book[-2]"));
        Assert.Null(JsonPath.Resolve(Root, "$.store.book[-3]"));
        Assert.Null(JsonPath.Resolve(Root, "$.store.book[2]"));
    }

    [Test]
    public void ResolveShorthandWithoutDollar()
    {
        Assert.Same(Store.Prop("book")[1], JsonPath.Resolve(Root, "store.book[1]"));
        Assert.Same(Store, JsonPath.Resolve(Root, ".store"));
        Assert.Same(Store, JsonPath.Resolve(Root, "['store']"));

        var array = JsonParser.Parse("[[1],[2,3]]");
        Assert.Same(array[1][0], JsonPath.Resolve(array, "[1][0]"));
    }

    [Test]
    public void InvalidOrMissingPathsResolveToNull()
    {
        string[] invalid = [string.Empty, "   ", "$.", "$.store.", "$..store", "$[abc]", "$[1.5]", "$['unterminated", "$['x'", "$['x'.y", "$[0", "$x", "$.store book"];
        foreach (var path in invalid)
        {
            Assert.Null(JsonPath.Resolve(Root, path), "path: " + path);
        }
        foreach (var path in invalid.Take(12))
        {
            Assert.False(JsonPath.TryParse(path, out _, out var error), "path: " + path);
            Assert.NotNull(error, "path: " + path);
        }

        Assert.Null(JsonPath.Resolve(Root, "$.missing"));
        Assert.Null(JsonPath.Resolve(Root, "$.store.book[0].title.deeper"), "primitives have no members");
        Assert.Null(JsonPath.Resolve(Root, "$.store.book[0].title[0]"), "primitives have no items");
        Assert.Null(JsonPath.Resolve(Root, "$.Store"), "names are case-sensitive");
    }

    [Test]
    public void TryParseProducesSegments()
    {
        Assert.True(JsonPath.TryParse("$.a['b c'][3][-1]", out var segments, out var error));
        Assert.Null(error);
        Assert.SequenceEqual(
            new[] { new JsonPath.Segment("a", null), new JsonPath.Segment("b c", null), new JsonPath.Segment(null, 3), new JsonPath.Segment(null, -1) },
            segments);
        Assert.True(JsonPath.TryParse("$", out var none, out _));
        Assert.Empty(none);
    }

    [Test]
    public void BuildResolveRoundTripOnEveryNode()
    {
        int count = 0;
        foreach (var node in Root.DescendantsAndSelf())
        {
            var path = JsonPath.Build(node);
            Assert.Same(node, JsonPath.Resolve(Root, path), "path: " + path);
            count++;
        }
        Assert.Equal(29, count);
    }

    [Test]
    public void RoundTripOnGeneratedKeys()
    {
        // Every printable ASCII character in a key, alone and embedded.
        var obj = JNode.CreateObject();
        var doc = new JDocument(obj);
        for (char c = ' '; c <= '~'; c++)
        {
            doc.InsertChild(obj, obj.Count, JNode.CreateNumber("1").WithName(c.ToString()));
            doc.InsertChild(obj, obj.Count, JNode.CreateNumber("2").WithName("k" + c + "v"));
        }
        foreach (var node in obj.Children)
        {
            var path = JsonPath.Build(node);
            Assert.Same(node, JsonPath.Resolve(obj, path), "key: " + node.Name + " path: " + path);
        }
    }
}
