using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Search;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

public sealed class FindEngineTests
{
    private const string People = """
        {"Name":"Alice","nickname":"alice cooper","age":30,"tags":["admin","user"],
         "nested":{"name":"bob","note":"names list"},"flag":true,"nothing":null}
        """;

    private static readonly JNode Root = JsonParser.Parse(People);

    private static List<string> FindAll(FindOptions options) =>
        new FindEngine(options).FindAll(Root).Select(Describe).ToList();

    private static string Describe(FindMatch m) => (m.Field == FindField.Name ? "name:" : "value:") + (m.Field == FindField.Name ? m.Node.Name : m.Node.Value ?? "null");

    [Test]
    public void DefaultSearchIsCaseInsensitiveOverNamesAndValues()
    {
        Assert.SequenceEqual(new[] { "value:Alice", "value:alice cooper" }, FindAll(new FindOptions { Text = "alice" }));
        Assert.SequenceEqual(new[] { "name:Name", "name:nickname", "name:name", "value:names list" }, FindAll(new FindOptions { Text = "NAME" }));
    }

    [Test]
    public void MatchCase()
    {
        Assert.SequenceEqual(new[] { "value:Alice" }, FindAll(new FindOptions { Text = "Alice", MatchCase = true }));
        Assert.SequenceEqual(new[] { "name:nickname", "name:name", "value:names list" }, FindAll(new FindOptions { Text = "name", MatchCase = true }));
    }

    [Test]
    public void WholeWord()
    {
        Assert.SequenceEqual(new[] { "name:Name", "name:name" }, FindAll(new FindOptions { Text = "name", WholeWord = true }));
        Assert.SequenceEqual(new[] { "value:alice cooper" }, FindAll(new FindOptions { Text = "cooper", WholeWord = true }));
        Assert.Empty(FindAll(new FindOptions { Text = "coop", WholeWord = true }));
        Assert.SequenceEqual(new[] { "value:names list" }, FindAll(new FindOptions { Text = "names list", WholeWord = true }), "phrases work too");
    }

    [Test]
    public void RegularExpressions()
    {
        Assert.SequenceEqual(new[] { "value:admin" }, FindAll(new FindOptions { Text = @"^a\w+n$", UseRegex = true }));
        Assert.SequenceEqual(new[] { "value:30" }, FindAll(new FindOptions { Text = @"\d+", UseRegex = true }));
        Assert.SequenceEqual(new[] { "value:Alice", "value:admin" }, FindAll(new FindOptions { Text = "^a(lice|dmin)$", UseRegex = true }));
        Assert.SequenceEqual(new[] { "value:Alice" }, FindAll(new FindOptions { Text = "^A", UseRegex = true, MatchCase = true }));
        Assert.SequenceEqual(new[] { "value:admin", "value:user" }, FindAll(new FindOptions { Text = "admin|user", UseRegex = true, WholeWord = true }), "whole word wraps alternations");

        // In literal mode regex metacharacters are plain text.
        Assert.Empty(FindAll(new FindOptions { Text = @"\d+" }));
    }

    [Test]
    public void ScopeNamesOrValues()
    {
        Assert.SequenceEqual(new[] { "name:Name", "name:nickname", "name:name" }, FindAll(new FindOptions { Text = "name", Scope = FindScope.Names }));
        Assert.SequenceEqual(new[] { "value:names list" }, FindAll(new FindOptions { Text = "name", Scope = FindScope.Values }));
        Assert.SequenceEqual(new[] { "value:null" }, FindAll(new FindOptions { Text = "null", Scope = FindScope.Values }), "null is searchable as text");
        Assert.SequenceEqual(new[] { "value:true" }, FindAll(new FindOptions { Text = "true" }));
    }

    [Test]
    public void ArrayItemsAndContainersHaveNoSearchableName()
    {
        var root = JsonParser.Parse("""[{"a":"x"},["x"]]""");
        var engine = new FindEngine(new FindOptions { Text = "x" });
        Assert.False(engine.IsMatch(root[1], FindField.Name));
        Assert.False(engine.IsMatch(root[1], FindField.Value), "containers have no value text");
        Assert.True(engine.IsMatch(root[1][0], FindField.Value));
        Assert.Equal(2, engine.FindAll(root).Count());
    }

    [Test]
    public void FindNextForwardWithWrap()
    {
        var root = JsonParser.Parse("""{"a":"x","b":"x","c":{"d":"x"},"e":1}""");
        var engine = new FindEngine(new FindOptions { Text = "x", Scope = FindScope.Values });
        var a = root.Prop("a");
        var b = root.Prop("b");
        var d = root.Prop("c").Prop("d");

        var m1 = Assert.NotNull(engine.FindNext(root, null));
        Assert.Same(a, m1.Node);
        Assert.Equal(FindField.Value, m1.Field);
        Assert.Same(b, engine.FindNext(root, a, FindField.Value)?.Node);
        Assert.Same(d, engine.FindNext(root, b, FindField.Value)?.Node);
        Assert.Same(a, engine.FindNext(root, d, FindField.Value)?.Node, "wraps around to the top");
        Assert.Same(b, engine.FindNext(root, a)?.Node, "start without a field: search starts after the node");

        var noWrap = new FindEngine(new FindOptions { Text = "x", Scope = FindScope.Values, Wrap = false });
        Assert.Null(noWrap.FindNext(root, d, FindField.Value));
        Assert.Same(a, noWrap.FindNext(root, null)?.Node);
    }

    [Test]
    public void FindNextBackwards()
    {
        var root = JsonParser.Parse("""{"a":"x","b":"x","c":{"d":"x"},"e":1}""");
        var engine = new FindEngine(new FindOptions { Text = "x", Scope = FindScope.Values, Backwards = true });
        var a = root.Prop("a");
        var b = root.Prop("b");
        var d = root.Prop("c").Prop("d");

        Assert.Same(d, engine.FindNext(root, null)?.Node, "from the top backwards: wraps to the end");
        Assert.Same(b, engine.FindNext(root, d, FindField.Value)?.Node);
        Assert.Same(a, engine.FindNext(root, b, FindField.Value)?.Node);
        Assert.Same(d, engine.FindNext(root, a, FindField.Value)?.Node, "wraps around to the bottom");

        var noWrap = new FindEngine(new FindOptions { Text = "x", Scope = FindScope.Values, Backwards = true, Wrap = false });
        Assert.Null(noWrap.FindNext(root, a, FindField.Value));
    }

    [Test]
    public void FindNextVisitsNameThenValueOfTheSameNode()
    {
        var root = JsonParser.Parse("""{"x":"x"}""");
        var node = root.Prop("x");
        var engine = new FindEngine(new FindOptions { Text = "x" });

        Assert.Equal(new FindMatch(node, FindField.Name), engine.FindNext(root, null));
        Assert.Equal(new FindMatch(node, FindField.Value), engine.FindNext(root, node, FindField.Name));
        Assert.Equal(new FindMatch(node, FindField.Name), engine.FindNext(root, node, FindField.Value), "wraps to the name");

        var back = new FindEngine(new FindOptions { Text = "x", Backwards = true });
        Assert.Equal(new FindMatch(node, FindField.Name), back.FindNext(root, node, FindField.Value));
    }

    [Test]
    public void SingleMatchIsFoundAgainAfterWrapping()
    {
        var root = JsonParser.Parse("""{"a":1,"b":"only","c":2}""");
        var b = root.Prop("b");
        var engine = new FindEngine(new FindOptions { Text = "only" });
        Assert.Equal(new FindMatch(b, FindField.Value), engine.FindNext(root, b, FindField.Value));
    }

    [Test]
    public void NoMatchReturnsNull()
    {
        var engine = new FindEngine(new FindOptions { Text = "zzz" });
        Assert.Null(engine.FindNext(Root, null));
        Assert.Null(engine.FindNext(Root, Root.Prop("age"), FindField.Value));
        Assert.Null(new FindEngine(new FindOptions { Text = "zzz", Backwards = true }).FindNext(Root, Root.Prop("age")));
        Assert.Empty(engine.FindAll(Root));
    }

    [Test]
    public void InvalidPatternsAreReported()
    {
        Assert.Throws<ArgumentException>(() => new FindEngine(new FindOptions { Text = string.Empty }));
        Assert.Throws<ArgumentException>(() => new FindEngine(new FindOptions { Text = "(unclosed", UseRegex = true }));
        Assert.NotNull(FindEngine.Validate(new FindOptions { Text = "[", UseRegex = true }));
        Assert.Null(FindEngine.Validate(new FindOptions { Text = "[", UseRegex = false }), "literal text is always valid");
        Assert.NotNull(FindEngine.Validate(new FindOptions()));
        Assert.Throws<ArgumentNullException>(() => new FindEngine(null!));
    }

    [Test]
    public void ReplaceTreatsDollarLiterallyInTextMode()
    {
        var literal = new FindEngine(new FindOptions { Text = "cost" });
        Assert.Equal("$5: 5", literal.Replace("cost: 5", "$5"));
        Assert.Equal("$0 and $$ and ${x}", literal.Replace("cost and $$ and ${x}", "$0"));
        Assert.Equal("$1$-$1$", literal.Replace("COST-cost", "$1$"), "case-insensitive by default");

        var regex = new FindEngine(new FindOptions { Text = @"(\w+)@(\w+)", UseRegex = true });
        Assert.Equal("host@me", regex.Replace("me@host", "$2@$1"));
        Assert.Equal("$ only", regex.Replace("a@b only", "$$"));

        var word = new FindEngine(new FindOptions { Text = "cat", WholeWord = true });
        Assert.Equal("dog concat dog", word.Replace("cat concat cat", "dog"));
    }

    [Test]
    public void ReplaceMatchEditsNamesAndValues()
    {
        var (doc, undo, ed) = Json.Editor("""{"color":"red","n":15,"flag":true}""");
        var engine = new FindEngine(new FindOptions { Text = "col" });
        var nameMatch = new FindMatch(doc.Root.Prop("color"), FindField.Name);
        Assert.True(engine.ReplaceMatch(ed, nameMatch, "kol").Success);
        Assert.Equal("kolor", doc.Root[0].Name);

        var five = new FindEngine(new FindOptions { Text = "5" });
        Assert.True(five.ReplaceMatch(ed, new FindMatch(doc.Root.Prop("n"), FindField.Value), "7").Success);
        Assert.Equal("17", doc.Root.Prop("n").Value);
        var seven = new FindEngine(new FindOptions { Text = "7" });
        var invalid = seven.ReplaceMatch(ed, new FindMatch(doc.Root.Prop("n"), FindField.Value), "x");
        Assert.False(invalid.Success, "\"1x\" is not a number");
        Assert.NotNull(invalid.Error);
        Assert.Equal("17", doc.Root.Prop("n").Value);

        var t = new FindEngine(new FindOptions { Text = "true" });
        Assert.True(t.ReplaceMatch(ed, new FindMatch(doc.Root.Prop("flag"), FindField.Value), "false").Success);
        Assert.False(doc.Root.Prop("flag").BooleanValue);
        Assert.False(t.ReplaceMatch(ed, new FindMatch(doc.Root, FindField.Name), "x").Success, "nothing to replace on the root");
        Assert.Equal(3, undo.Count);
    }

    [Test]
    public void ReplaceAllIsOneUndoStep()
    {
        const string text = """{"a":"foo","b":"foo bar","n":12,"foo":"x","list":["food"]}""";
        var (doc, undo, ed) = Json.Editor(text);
        var engine = new FindEngine(new FindOptions { Text = "foo" });

        var (replaced, skipped) = engine.ReplaceAll(ed, "baz");

        Assert.Equal(4, replaced);
        Assert.Equal(0, skipped);
        Assert.Equal("""{"a":"baz","b":"baz bar","n":12,"baz":"x","list":["bazd"]}""", Json.Compact(doc));
        Assert.Equal(1, undo.Count);
        Assert.Equal("Replace All", undo.NextUndo!.Name);
        undo.Undo();
        Assert.Equal(text, Json.Compact(doc));
    }

    [Test]
    public void ReplaceAllSkipsInvalidNumbersAndNameCollisions()
    {
        var (doc, undo, ed) = Json.Editor("""{"n":12,"m":1,"s":"1","ab":true,"ac":false}""");

        var (replaced, skipped) = new FindEngine(new FindOptions { Text = "1" }).ReplaceAll(ed, "x");
        Assert.Equal(1, replaced, "only the string can hold \"x\"");
        Assert.Equal(2, skipped, "12 -> x2 and 1 -> x are not numbers");
        Assert.Equal("""{"n":12,"m":1,"s":"x","ab":true,"ac":false}""", Json.Compact(doc));

        var (r2, s2) = new FindEngine(new FindOptions { Text = "1" }).ReplaceAll(ed, "9");
        Assert.Equal(2, r2);
        Assert.Equal(0, s2);
        Assert.Equal("""{"n":92,"m":9,"s":"x","ab":true,"ac":false}""", Json.Compact(doc));

        var (r3, s3) = new FindEngine(new FindOptions { Text = "b", Scope = FindScope.Names }).ReplaceAll(ed, "c");
        Assert.Equal(0, r3);
        Assert.Equal(1, s3, "renaming ab to ac would collide");

        Assert.Equal(2, undo.Count, "the all-skipped pass records nothing");
        var (r4, s4) = new FindEngine(new FindOptions { Text = "zzz" }).ReplaceAll(ed, "q");
        Assert.Equal((0, 0), (r4, s4));
        Assert.Equal(2, undo.Count);
    }
}
