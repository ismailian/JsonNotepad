using JsonNotepad.Core.Editing;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

public sealed class NodeEditorTests
{
    // ----- Insert ------------------------------------------------------------

    [Test]
    public void InsertChildGeneratesUniqueNames()
    {
        var (doc, undo, ed) = Json.Editor("{}");
        var n1 = ed.Insert(doc.Root, InsertPosition.Child, JNodeKind.String);
        var n2 = ed.Insert(doc.Root, InsertPosition.Child, JNodeKind.Number);
        var n3 = ed.Insert(doc.Root, InsertPosition.Child, JNodeKind.Object);

        Assert.Equal("newProperty", n1.Name);
        Assert.Equal("newProperty1", n2.Name);
        Assert.Equal("newProperty2", n3.Name);
        Assert.Equal("""{"newProperty":"","newProperty1":0,"newProperty2":{}}""", Json.Compact(doc));
        Assert.Equal(3, undo.Count);
        Assert.Equal("Insert Number", GetUndoName(undo, 1));

        undo.Undo();
        Assert.Equal("""{"newProperty":"","newProperty1":0}""", Json.Compact(doc));
    }

    private static string GetUndoName(UndoManager undo, int stepsBack)
    {
        for (int i = 0; i < stepsBack; i++)
        {
            undo.Undo();
        }
        var name = undo.NextUndo!.Name;
        for (int i = 0; i < stepsBack; i++)
        {
            undo.Redo();
        }
        return name;
    }

    [Test]
    public void InsertBeforeAndAfter()
    {
        var (doc, _, ed) = Json.Editor("""{"a":1,"b":2}""");
        var b = doc.Root.Prop("b");
        ed.Insert(b, InsertPosition.Before, JNodeKind.Null);
        ed.Insert(b, InsertPosition.After, JNodeKind.Boolean);
        Assert.Equal("""{"a":1,"newProperty":null,"b":2,"newProperty1":false}""", Json.Compact(doc));

        var (arrDoc, _, arrEd) = Json.Editor("[1,2]");
        var inserted = arrEd.Insert(arrDoc.Root[0], InsertPosition.After, JNodeKind.Array);
        arrEd.Insert(arrDoc.Root[0], InsertPosition.Before, JNodeKind.String);
        Assert.Equal("""["",1,[],2]""", Json.Compact(arrDoc));
        Assert.Null(inserted.Name, "array items are unnamed");
    }

    [Test]
    public void InsertNodeKeepsGivenNameButMakesItUnique()
    {
        var (doc, _, ed) = Json.Editor("""{"id":1}""");
        var node = ed.InsertNode(doc.Root, InsertPosition.Child, JNode.CreateNumber("2").WithName("id"));
        Assert.Equal("id1", node.Name);
        var other = ed.InsertNode(doc.Root, InsertPosition.Child, JNode.CreateNumber("3").WithName("other"));
        Assert.Equal("other", other.Name);
    }

    [Test]
    public void InvalidInsertsAreRejected()
    {
        var (doc, undo, ed) = Json.Editor("""{"a":1}""");
        Assert.False(NodeEditor.CanInsert(doc.Root, InsertPosition.Before));
        Assert.False(NodeEditor.CanInsert(doc.Root, InsertPosition.After));
        Assert.True(NodeEditor.CanInsert(doc.Root, InsertPosition.Child));
        Assert.False(NodeEditor.CanInsert(doc.Root.Prop("a"), InsertPosition.Child));
        Assert.False(NodeEditor.CanInsert(null, InsertPosition.Child));

        Assert.Throws<InvalidOperationException>(() => ed.Insert(doc.Root, InsertPosition.After, JNodeKind.Null));
        Assert.Throws<InvalidOperationException>(() => ed.Insert(doc.Root.Prop("a"), InsertPosition.Child, JNodeKind.Null));
        Assert.Equal(0, undo.Count);
    }

    // ----- Delete / duplicate --------------------------------------------------

    [Test]
    public void DeleteReturnsTheNodeToSelectNext()
    {
        var (doc, undo, ed) = Json.Editor("""{"a":1,"b":2,"c":3,"o":{"only":true}}""");
        Assert.Same(doc.Root.Prop("c"), ed.Delete(doc.Root.Prop("b")), "next sibling");
        Assert.Same(doc.Root.Prop("c"), ed.Delete(doc.Root.Prop("o")), "previous sibling when deleting the last");
        var only = JsonParser.Parse("""{"x":{"y":1}}""");
        var d2 = new JDocument(only);
        var ed2 = new NodeEditor(d2, new UndoManager());
        Assert.Same(d2.Root.Prop("x"), ed2.Delete(d2.Root.Prop("x").Prop("y")), "parent when deleting the only child");

        Assert.Equal("""{"a":1,"c":3}""", Json.Compact(doc));
        Assert.False(NodeEditor.CanDelete(doc.Root));
        Assert.False(NodeEditor.CanDelete(null));
        Assert.Throws<InvalidOperationException>(() => ed.Delete(doc.Root));
        undo.Undo();
        undo.Undo();
        Assert.Equal("""{"a":1,"b":2,"c":3,"o":{"only":true}}""", Json.Compact(doc));
    }

    [Test]
    public void DuplicateCopiesDeeplyWithUniqueName()
    {
        var (doc, undo, ed) = Json.Editor("""{"a":{"x":[1,2]},"b":0}""");
        var a = doc.Root.Prop("a");
        var copy = ed.Duplicate(a);

        Assert.Equal("a1", copy.Name);
        Assert.Same(copy, doc.Root[1], "inserted right after the original");
        Assert.NotSame(a, copy);
        Assert.NotSame(a.Prop("x"), copy.Prop("x"));
        Assert.True(JsonEquality.DeepEquals(a, copy));
        Assert.Equal("""{"a":{"x":[1,2]},"a1":{"x":[1,2]},"b":0}""", Json.Compact(doc));

        // Editing the copy does not touch the original.
        ed.SetValue(copy.Prop("x")[0], "9");
        Assert.Equal("1", a.Prop("x")[0].Value);

        var item = ed.Duplicate(a.Prop("x")[1]);
        Assert.Null(item.Name);
        Assert.Equal("[1,2,2]", Json.Compact(a.Prop("x")));
        Assert.Throws<InvalidOperationException>(() => ed.Duplicate(doc.Root));
        Assert.Equal(3, undo.Count);
    }

    // ----- Rename / SetValue ----------------------------------------------------

    [Test]
    public void RenameRejectsDuplicatesAndNonMembers()
    {
        var (doc, undo, ed) = Json.Editor("""{"a":1,"b":2,"arr":[1]}""");
        var b = doc.Root.Prop("b");

        var dup = ed.Rename(b, "a");
        Assert.False(dup.Success);
        Assert.Contains("already has a member named \"a\"", dup.Error);
        Assert.Equal("b", b.Name);

        Assert.True(ed.Rename(b, "b").Success, "renaming to the same name is allowed (and a noop)");
        Assert.Equal(0, undo.Count);

        var ok = ed.Rename(b, "B");
        Assert.True(ok.Success);
        Assert.Same(b, ok.Node);
        Assert.Equal("B", b.Name);

        Assert.False(ed.Rename(doc.Root.Prop("arr")[0], "x").Success);
        Assert.False(ed.Rename(doc.Root, "x").Success);
        Assert.False(NodeEditor.CanRename(doc.Root));
        Assert.True(ed.Rename(b, string.Empty).Success, "the empty string is a legal JSON name");
        Assert.Equal(2, undo.Count);
    }

    [Test]
    public void SetValueNormalizesNumbers()
    {
        var (doc, undo, ed) = Json.Editor("[0]");
        var n = doc.Root[0];
        var cases = new (string Input, string Expected)[]
        {
            (" 42 ", "42"), (".5", "0.5"), ("-.5", "-0.5"), ("+3", "3"), ("7.", "7"),
            ("1.10", "1.10"), ("1e400", "1e400"), ("-0", "-0"), ("007", "7"), ("12345678901234567890", "12345678901234567890"),
        };
        foreach (var (input, expected) in cases)
        {
            var r = ed.SetValue(n, input);
            Assert.True(r.Success, input);
            Assert.Equal(expected, n.Value, input);
        }

        int count = undo.Count;
        foreach (var bad in new[] { "abc", "", "1,5", "1 2", "0x10", "NaN", "--1", "1e" })
        {
            var r = ed.SetValue(n, bad);
            Assert.False(r.Success, "'" + bad + "' must be rejected");
            Assert.NotNull(r.Error);
        }
        Assert.Equal(count, undo.Count, "rejected input records nothing");
        Assert.Equal("12345678901234567890", n.Value);

        ed.SetValue(n, " 12345678901234567890 ");
        Assert.Equal(count, undo.Count, "normalizing to the current value is a noop");
    }

    [Test]
    public void SetValuePerKind()
    {
        var (doc, _, ed) = Json.Editor("""{"s":"x","b":false,"z":null,"o":{},"a":[]}""");
        Assert.True(ed.SetValue(doc.Root.Prop("s"), "  any text, even \"quotes\"  ").Success);
        Assert.Equal("  any text, even \"quotes\"  ", doc.Root.Prop("s").Value, "strings are stored verbatim");
        Assert.True(ed.SetValue(doc.Root.Prop("s"), string.Empty).Success);

        Assert.True(ed.SetValue(doc.Root.Prop("b"), " TRUE ").Success);
        Assert.Equal("true", doc.Root.Prop("b").Value);
        Assert.True(ed.SetValue(doc.Root.Prop("b"), "False").Success);
        Assert.Equal("false", doc.Root.Prop("b").Value);
        Assert.False(ed.SetValue(doc.Root.Prop("b"), "yes").Success);
        Assert.False(ed.SetValue(doc.Root.Prop("b"), "1").Success);

        Assert.False(ed.SetValue(doc.Root.Prop("z"), "1").Success);
        Assert.False(ed.SetValue(doc.Root.Prop("o"), "1").Success);
        Assert.False(ed.SetValue(doc.Root.Prop("a"), "1").Success);

        Assert.True(NodeEditor.CanEditValue(doc.Root.Prop("s")));
        Assert.False(NodeEditor.CanEditValue(doc.Root.Prop("z")));
        Assert.False(NodeEditor.CanEditValue(doc.Root.Prop("o")));
        Assert.False(NodeEditor.CanEditValue(null));
    }

    [Test]
    public void ToggleBoolean()
    {
        var (doc, undo, ed) = Json.Editor("[true,1]");
        ed.ToggleBoolean(doc.Root[0]);
        Assert.Equal("false", doc.Root[0].Value);
        ed.ToggleBoolean(doc.Root[1]);
        Assert.Equal("1", doc.Root[1].Value);
        Assert.Equal(1, undo.Count);
    }

    // ----- ChangeKind / KindConverter ---------------------------------------

    [Test]
    public void ChangeKindObjectAndArray()
    {
        var (doc, undo, ed) = Json.Editor("""{"m":{"x":1,"y":[true]}}""");
        var original = doc.Root.Prop("m");

        var arr = ed.ChangeKind(original, JNodeKind.Array);
        Assert.Equal("m", arr.Name);
        Assert.Equal("""{"m":[1,[true]]}""", Json.Compact(doc));
        Assert.True(arr.Children.All(c => c.Name is null));

        var obj = ed.ChangeKind(arr, JNodeKind.Object);
        Assert.Equal("""{"m":{"0":1,"1":[true]}}""", Json.Compact(doc));
        Assert.Equal("m", obj.Name);

        undo.Undo();
        undo.Undo();
        Assert.Same(original, doc.Root.Prop("m"), "undo puts the original node back");
        Assert.Same(original, ed.ChangeKind(original, JNodeKind.Object), "same kind: nothing to do");
        Assert.Equal(2, undo.Count);
    }

    [Test]
    public void StringifyAndParseEmbeddedJson()
    {
        var (doc, undo, ed) = Json.Editor("""{"m":{"x":1,"y":[true]},"s":"{\"a\": [1, 2]}","bad":"not json","shape":"[1]"}""");

        var str = ed.ChangeKind(doc.Root.Prop("m"), JNodeKind.String);
        Assert.Equal("""{"x":1,"y":[true]}""", str.Value);
        Assert.Equal("m", str.Name);

        var parsed = ed.ChangeKind(doc.Root.Prop("s"), JNodeKind.Object);
        Assert.Equal("""{"a":[1,2]}""", Json.Compact(parsed));
        Assert.Equal("s", parsed.Name);

        Assert.Equal("{}", Json.Compact(ed.ChangeKind(doc.Root.Prop("bad"), JNodeKind.Object)), "unparseable text gives an empty object");
        Assert.Equal("{}", Json.Compact(ed.ChangeKind(doc.Root.Prop("shape"), JNodeKind.Object)), "JSON of the wrong shape gives an empty object");

        var (d2, _, e2) = Json.Editor("""["[1,[2]]"]""");
        Assert.Equal("[1,[2]]", Json.Compact(e2.ChangeKind(d2.Root[0], JNodeKind.Array)));

        Assert.Equal(4, undo.Count);
    }

    [Test]
    public void KindConverterPrimitiveConversions()
    {
        static JNode C(string json, JNodeKind kind) => KindConverter.Convert(JsonParser.Parse(json), kind);

        Assert.Equal("42", C("\" 42 \"", JNodeKind.Number).Value);
        Assert.Equal("0.5", C("\".5\"", JNodeKind.Number).Value);
        Assert.Equal("0", C("\"abc\"", JNodeKind.Number).Value);
        Assert.Equal("1", C("true", JNodeKind.Number).Value);
        Assert.Equal("0", C("false", JNodeKind.Number).Value);
        Assert.Equal("0", C("null", JNodeKind.Number).Value);
        Assert.Equal("3", C("[1,2,3]", JNodeKind.Number).Value, "containers become their count");

        Assert.True(C("\"true\"", JNodeKind.Boolean).BooleanValue);
        Assert.True(C("\" YES \"", JNodeKind.Boolean).BooleanValue);
        Assert.True(C("\"1\"", JNodeKind.Boolean).BooleanValue);
        Assert.False(C("\"no\"", JNodeKind.Boolean).BooleanValue);
        Assert.False(C("0", JNodeKind.Boolean).BooleanValue);
        Assert.False(C("0.0", JNodeKind.Boolean).BooleanValue);
        Assert.True(C("2.5", JNodeKind.Boolean).BooleanValue);
        Assert.True(C("[0]", JNodeKind.Boolean).BooleanValue);
        Assert.False(C("{}", JNodeKind.Boolean).BooleanValue);
        Assert.False(C("null", JNodeKind.Boolean).BooleanValue);

        Assert.Equal("1.10", C("1.10", JNodeKind.String).Value, "the number literal is kept as text");
        Assert.Equal("true", C("true", JNodeKind.String).Value);
        Assert.Equal(string.Empty, C("null", JNodeKind.String).Value);

        Assert.Equal(JNodeKind.Null, C("{\"a\":1}", JNodeKind.Null).Kind);
        Assert.Equal("[]", Json.Compact(C("5", JNodeKind.Array)));
        Assert.Equal("{}", Json.Compact(C("true", JNodeKind.Object)));
    }

    [Test]
    public void KindConverterSameKindClonesAndKeepsName()
    {
        var source = JsonParser.Parse("""{"a":[1]}""").Prop("a");
        var clone = KindConverter.Convert(source, JNodeKind.Array);
        Assert.NotSame(source, clone);
        Assert.Equal("a", clone.Name);
        Assert.Null(clone.Parent);

        var str = KindConverter.Convert(source, JNodeKind.String);
        Assert.Equal("[1]", str.Value);
        Assert.Equal("a", str.Name);
        Assert.NotNull(source.Parent, "the source is not modified");
    }

    [Test]
    public void ChangeRootKind()
    {
        var (doc, undo, ed) = Json.Editor("""{"a":1}""");
        var arr = ed.ChangeKind(doc.Root, JNodeKind.Array);
        Assert.Same(arr, doc.Root);
        Assert.Null(arr.Name);
        Assert.Equal("[1]", Json.Compact(doc));
        undo.Undo();
        Assert.Equal("""{"a":1}""", Json.Compact(doc));
    }

    [Test]
    public void ReplaceWithJson()
    {
        var (doc, undo, ed) = Json.Editor("""{"a":1}""");
        var r = ed.ReplaceWithJson(doc.Root.Prop("a"), """{"deep":[true]}""", ParseOptions.Default);
        Assert.True(r.Success);
        Assert.Equal("""{"a":{"deep":[true]}}""", Json.Compact(doc));

        var bad = ed.ReplaceWithJson(doc.Root.Prop("a"), "{oops", ParseOptions.Default);
        Assert.False(bad.Success);
        Assert.Contains("line 1", bad.Error);
        Assert.Equal(1, undo.Count);
    }

    // ----- Nudge -------------------------------------------------------------

    private const string NudgeDoc = """{"obj":{"a":1,"b":2},"arr":[10,{"k":0},[]],"z":true}""";

    [Test]
    public void NudgeUpAndDown()
    {
        var (doc, undo, ed) = Json.Editor(NudgeDoc);
        var obj = doc.Root.Prop("obj");
        var a = obj.Prop("a");
        var b = obj.Prop("b");

        Assert.False(NodeEditor.CanNudge(a, NudgeDirection.Up));
        Assert.True(NodeEditor.CanNudge(a, NudgeDirection.Down));
        Assert.False(NodeEditor.CanNudge(b, NudgeDirection.Down));
        Assert.False(NodeEditor.CanNudge(doc.Root, NudgeDirection.Up));
        Assert.False(NodeEditor.CanNudge(null, NudgeDirection.Up));

        ed.Nudge(b, NudgeDirection.Up);
        Assert.Equal("""{"b":2,"a":1}""", Json.Compact(obj));
        ed.Nudge(b, NudgeDirection.Down);
        Assert.Equal("""{"a":1,"b":2}""", Json.Compact(obj));

        var arr = doc.Root.Prop("arr");
        ed.Nudge(arr[0], NudgeDirection.Down);
        Assert.Equal("""[{"k":0},10,[]]""", Json.Compact(arr));

        int count = undo.Count;
        ed.Nudge(a, NudgeDirection.Up); // not allowed: ignored
        Assert.Equal(count, undo.Count);
        Assert.Equal(3, count);

        undo.Undo();
        undo.Undo();
        undo.Undo();
        Assert.Equal(NudgeDoc, Json.Compact(doc));
    }

    [Test]
    public void NudgeLeftMovesOutOfTheContainer()
    {
        var (doc, undo, ed) = Json.Editor(NudgeDoc);
        var a = doc.Root.Prop("obj").Prop("a");
        Assert.True(NodeEditor.CanNudge(a, NudgeDirection.Left));
        Assert.False(NodeEditor.CanNudge(doc.Root.Prop("obj"), NudgeDirection.Left), "the parent is the root");

        ed.Nudge(a, NudgeDirection.Left);
        Assert.Equal("""{"obj":{"b":2},"a":1,"arr":[10,{"k":0},[]],"z":true}""", Json.Compact(doc), "placed right after its old parent");
        Assert.Same(doc.Root, a.Parent);

        // Array item into an object: gets a generated name based on its old index.
        var ten = doc.Root.Prop("arr")[0];
        ed.Nudge(ten, NudgeDirection.Left);
        Assert.Equal("item0", ten.Name);
        Assert.Equal("""{"obj":{"b":2},"a":1,"arr":[{"k":0},[]],"item0":10,"z":true}""", Json.Compact(doc));

        undo.Undo();
        Assert.Null(ten.Name, "undo restores the unnamed array item");
        Assert.Same(ten, doc.Root.Prop("arr")[0]);
        undo.Undo();
        Assert.Equal(NudgeDoc, Json.Compact(doc));
    }

    [Test]
    public void NudgeLeftAvoidsNameCollisions()
    {
        var (doc, _, ed) = Json.Editor("""{"o":{"z":1},"z":2}""");
        var inner = doc.Root.Prop("o").Prop("z");
        ed.Nudge(inner, NudgeDirection.Left);
        Assert.Equal("z1", inner.Name);
        Assert.Equal("""{"o":{},"z1":1,"z":2}""", Json.Compact(doc));

        var (arrDoc, _, arrEd) = Json.Editor("""[[{"named":1}]]""");
        var named = arrDoc.Root[0][0].Prop("named");
        arrEd.Nudge(named, NudgeDirection.Left);
        Assert.Null(named.Name, "moving into an array drops the name");
        Assert.Equal("""[[{},1]]""", Json.Compact(arrDoc));
    }

    [Test]
    public void NudgeRightMovesIntoPreviousContainer()
    {
        var (doc, undo, ed) = Json.Editor(NudgeDoc);
        var arr = doc.Root.Prop("arr");
        Assert.False(NodeEditor.CanNudge(arr[0], NudgeDirection.Right), "first child has no previous sibling");
        Assert.False(NodeEditor.CanNudge(arr[1], NudgeDirection.Right), "previous sibling is a primitive");
        Assert.True(NodeEditor.CanNudge(arr[2], NudgeDirection.Right));
        Assert.False(NodeEditor.CanNudge(doc.Root.Prop("obj"), NudgeDirection.Right));

        // Root member into the previous object: keeps its name.
        ed.Nudge(arr, NudgeDirection.Right);
        Assert.Equal("""{"obj":{"a":1,"b":2,"arr":[10,{"k":0},[]]},"z":true}""", Json.Compact(doc));
        Assert.Equal("arr", arr.Name);

        // Array item into a previous object sibling: named after its index.
        var empty = arr[2];
        ed.Nudge(empty, NudgeDirection.Right);
        Assert.Equal("item2", empty.Name);
        Assert.Equal("""[10,{"k":0,"item2":[]}]""", Json.Compact(arr));

        // Object member into a previous array sibling: the name is dropped (appended at the end).
        var (d2, _, e2) = Json.Editor("""{"list":[1],"x":2}""");
        var x = d2.Root.Prop("x");
        e2.Nudge(x, NudgeDirection.Right);
        Assert.Null(x.Name);
        Assert.Equal("""{"list":[1,2]}""", Json.Compact(d2));

        undo.Undo();
        undo.Undo();
        Assert.Equal(NudgeDoc, Json.Compact(doc));
    }

    // ----- Move (drag and drop) -------------------------------------------------

    [Test]
    public void MoveWithinTheSameArray()
    {
        var (doc, undo, ed) = Json.Editor("[1,2,3,4]");
        var root = doc.Root;
        Assert.True(ed.Move(root[0], root[2], InsertPosition.After));
        Assert.Equal("[2,3,1,4]", Json.Compact(doc));
        Assert.True(ed.Move(root[3], root[0], InsertPosition.Before));
        Assert.Equal("[4,2,3,1]", Json.Compact(doc));
        Assert.True(ed.Move(root[1], root[2], InsertPosition.Before), "dropping just before the next sibling");
        Assert.Equal("[4,2,3,1]", Json.Compact(doc));
        Assert.True(ed.Move(root[2], root[1], InsertPosition.Before));
        Assert.Equal("[4,3,2,1]", Json.Compact(doc));
        undo.Undo();
        undo.Undo();
        undo.Undo();
        undo.Undo();
        Assert.Equal("[1,2,3,4]", Json.Compact(doc));
    }

    [Test]
    public void MoveAsChild()
    {
        var (doc, _, ed) = Json.Editor("""{"a":1,"b":2,"target":{"a":0},"list":[]}""");
        var a = doc.Root.Prop("a");
        Assert.True(ed.Move(a, doc.Root.Prop("target"), InsertPosition.Child));
        Assert.Equal("a1", a.Name, "unique within the destination");
        Assert.Equal("""{"b":2,"target":{"a":0,"a1":1},"list":[]}""", Json.Compact(doc));

        var b = doc.Root.Prop("b");
        Assert.True(ed.Move(b, doc.Root, InsertPosition.Child));
        Assert.Equal("""{"target":{"a":0,"a1":1},"list":[],"b":2}""", Json.Compact(doc), "same parent as child: moves to the end");
        Assert.Equal("b", b.Name);

        Assert.True(ed.Move(b, doc.Root.Prop("list"), InsertPosition.Child));
        Assert.Null(b.Name);
        Assert.Equal("""{"target":{"a":0,"a1":1},"list":[2]}""", Json.Compact(doc));
    }

    [Test]
    public void InvalidMovesReturnFalse()
    {
        var (doc, undo, ed) = Json.Editor("""{"a":{"b":{"c":1}},"d":2}""");
        var a = doc.Root.Prop("a");
        Assert.False(ed.Move(a, a, InsertPosition.Child), "onto itself");
        Assert.False(ed.Move(a, a.Prop("b"), InsertPosition.Child), "into a descendant");
        Assert.False(ed.Move(a, a.Prop("b").Prop("c"), InsertPosition.After), "next to a descendant");
        Assert.False(ed.Move(doc.Root, doc.Root.Prop("d"), InsertPosition.After), "the root cannot move");
        Assert.False(ed.Move(doc.Root.Prop("d"), doc.Root, InsertPosition.Before), "no siblings for the root");
        Assert.False(ed.Move(a, doc.Root.Prop("d"), InsertPosition.Child), "primitives have no children");
        Assert.Equal(0, undo.Count);
    }

    // ----- Sort ----------------------------------------------------------------

    [Test]
    public void SortPropertiesIsCaseInsensitiveThenOrdinal()
    {
        var (doc, undo, ed) = Json.Editor("""{"b":1,"a":3,"A":2,"c":{"z":1,"y":2},"_":0}""");
        ed.SortProperties(doc.Root, recursive: false);
        Assert.Equal("""{"A":2,"a":3,"b":1,"c":{"z":1,"y":2},"_":0}""", Json.Compact(doc), "OrdinalIgnoreCase upper-cases, so letters sort before underscore");
        Assert.Equal(1, undo.Count);
        undo.Undo();
        Assert.Equal("""{"b":1,"a":3,"A":2,"c":{"z":1,"y":2},"_":0}""", Json.Compact(doc));
    }

    [Test]
    public void SortPropertiesRecursiveIsOneUndoStep()
    {
        const string text = """{"b":{"z":1,"y":{"q":1,"p":2}},"a":[{"d":1,"c":2}]}""";
        var (doc, undo, ed) = Json.Editor(text);
        ed.SortProperties(doc.Root, recursive: true);
        Assert.Equal("""{"a":[{"c":2,"d":1}],"b":{"y":{"p":2,"q":1},"z":1}}""", Json.Compact(doc));
        Assert.Equal(1, undo.Count);
        Assert.Equal("Sort Properties (Recursive)", undo.NextUndo!.Name);
        undo.Undo();
        Assert.Equal(text, Json.Compact(doc));

        // Already sorted: nothing recorded.
        var (sorted, sortedUndo, sortedEd) = Json.Editor("""{"a":1,"b":{"c":1,"d":2}}""");
        sortedEd.SortProperties(sorted.Root, recursive: true);
        Assert.Equal(0, sortedUndo.Count);
    }

    // ----- Clipboard -----------------------------------------------------------

    [Test]
    public void ParseClipboardTextAsValue()
    {
        var nodes = Assert.NotNull(NodeEditor.ParseClipboardText("  [1, {\"a\": true}]  ", ParseOptions.Default, out var error));
        Assert.Null(error);
        var node = Assert.Single(nodes);
        Assert.Equal("""[1,{"a":true}]""", Json.Compact(node));

        var str = Assert.Single(Assert.NotNull(NodeEditor.ParseClipboardText("\"just text\"", ParseOptions.Default, out _)));
        Assert.Equal("just text", str.Value);
    }

    [Test]
    public void ParseClipboardTextAsMemberFragment()
    {
        var nodes = Assert.NotNull(NodeEditor.ParseClipboardText("\"a\": 1, \"b\": {\"c\": [2]},", ParseOptions.Default, out var error));
        Assert.Null(error, "a successful parse reports no error");
        Assert.Equal(2, nodes.Count);
        Assert.Equal("a", nodes[0].Name);
        Assert.Equal("1", nodes[0].Value);
        Assert.Equal("b", nodes[1].Name);
        Assert.Equal("""{"c":[2]}""", Json.Compact(nodes[1]));
        Assert.True(nodes.All(n => n.Parent is null), "fragments are detached and ready to insert");

        var single = Assert.Single(Assert.NotNull(NodeEditor.ParseClipboardText("\"key\": \"value\"", ParseOptions.Default, out _)));
        Assert.Equal("key", single.Name);
    }

    [Test]
    public void ParseClipboardTextRejectsGarbage()
    {
        Assert.Null(NodeEditor.ParseClipboardText("not json at all", ParseOptions.Default, out var e1));
        Assert.NotNull(e1);
        Assert.Null(NodeEditor.ParseClipboardText("   ", ParseOptions.Default, out var e2));
        Assert.Contains("empty", e2);
        Assert.Null(NodeEditor.ParseClipboardText("\"a\": ", ParseOptions.Default, out var e3));
        Assert.NotNull(e3);
    }

    [Test]
    public void ClipboardTextHasNoTrailingNewLine()
    {
        var node = JsonParser.Parse("""{"a":[1]}""");
        Assert.Equal("{\n  \"a\": [\n    1\n  ]\n}", NodeEditor.ToClipboardText(node, FormatOptions.Default with { NewLine = "\n" }));
    }

    [Test]
    public void PasteMembersIntoObjectAsOneUndoStep()
    {
        var (doc, undo, ed) = Json.Editor("""{"a":0,"z":9}""");
        var nodes = NodeEditor.ParseClipboardText("\"a\": 1, \"b\": 2", ParseOptions.Default, out _)!;
        var last = ed.Paste(doc.Root, InsertPosition.Child, nodes, null);
        Assert.Equal("""{"a":0,"z":9,"a1":1,"b":2}""", Json.Compact(doc));
        Assert.Equal("b", last!.Name);
        Assert.Equal(1, undo.Count);
        Assert.Equal("Paste", undo.NextUndo!.Name);
        undo.Undo();
        Assert.Equal("""{"a":0,"z":9}""", Json.Compact(doc));
    }

    [Test]
    public void PasteAfterAndBeforeKeepsOrder()
    {
        var (doc, _, ed) = Json.Editor("[1,5]");
        ed.Paste(doc.Root[0], InsertPosition.After, NodeEditor.ParseClipboardText("\"x\": 2, \"y\": 3", ParseOptions.Default, out _)!, null);
        Assert.Equal("[1,2,3,5]", Json.Compact(doc), "names are dropped in arrays, order is kept");
        ed.Paste(doc.Root[0], InsertPosition.Before, [JNode.CreateNumber("-1"), JNode.CreateNumber("0")], null);
        Assert.Equal("[-1,0,1,2,3,5]", Json.Compact(doc));
    }

    [Test]
    public void PasteSingleValueUsesClipboardName()
    {
        var (doc, _, ed) = Json.Editor("""{"price":1}""");
        var pasted = ed.Paste(doc.Root.Prop("price"), InsertPosition.After, [JNode.CreateNumber("2")], "price");
        Assert.Equal("price1", pasted!.Name);
        var unnamed = ed.Paste(doc.Root, InsertPosition.Child, [JNode.CreateNumber("3")], null);
        Assert.Equal("newProperty", unnamed!.Name);
        Assert.Equal("""{"price":1,"price1":2,"newProperty":3}""", Json.Compact(doc));

        Assert.Null(ed.Paste(doc.Root, InsertPosition.After, [JNode.CreateNull()], null), "invalid position");
        Assert.Null(ed.Paste(doc.Root, InsertPosition.Child, [], null), "nothing to paste");
    }
}
