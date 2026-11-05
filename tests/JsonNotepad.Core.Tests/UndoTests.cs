using JsonNotepad.Core.Editing;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

public sealed class UndoTests
{
    private const string Start = """{"a":1,"b":"two","c":[10,20,30],"d":{"x":true}}""";

    /// <summary>Executes, undoes and redoes <paramref name="command"/>, checking the document text at each step.</summary>
    private static void AssertDoUndoRedo(JDocument doc, UndoManager undo, EditCommand command, string expectedAfter)
    {
        var before = Json.Compact(doc);
        undo.Execute(command);
        Assert.Equal(expectedAfter, Json.Compact(doc), "after Do");
        Assert.True(undo.CanUndo);
        Assert.Same(command, undo.NextUndo);

        Assert.Same(command, undo.Undo());
        Assert.Equal(before, Json.Compact(doc), "after Undo");
        Assert.True(undo.CanRedo);
        Assert.Same(command, undo.NextRedo);

        Assert.Same(command, undo.Redo());
        Assert.Equal(expectedAfter, Json.Compact(doc), "after Redo");

        undo.Undo();
        Assert.Equal(before, Json.Compact(doc), "after second Undo");
        undo.Redo();
        Assert.Equal(expectedAfter, Json.Compact(doc), "after second Redo");
    }

    [Test]
    public void InsertNodeCommand()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        var node = JNode.CreateNumber("25");
        var cmd = new InsertNodeCommand(doc, doc.Root.Prop("c"), 2, node);
        AssertDoUndoRedo(doc, undo, cmd, """{"a":1,"b":"two","c":[10,20,25,30],"d":{"x":true}}""");
        Assert.Same(node, cmd.SelectionAfterDo);
        Assert.Same(doc.Root.Prop("c")[1], cmd.SelectionAfterUndo);
        Assert.Equal("Insert", cmd.Name);
    }

    [Test]
    public void InsertIntoObjectAtStart()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        var cmd = new InsertNodeCommand(doc, doc.Root, 0, JNode.CreateNull().WithName("first"));
        AssertDoUndoRedo(doc, undo, cmd, """{"first":null,"a":1,"b":"two","c":[10,20,30],"d":{"x":true}}""");
        Assert.Same(doc.Root, cmd.SelectionAfterUndo);
    }

    [Test]
    public void DeleteNodeCommand()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        var b = doc.Root.Prop("b");
        var cmd = new DeleteNodeCommand(doc, b);
        AssertDoUndoRedo(doc, undo, cmd, """{"a":1,"c":[10,20,30],"d":{"x":true}}""");
        Assert.Same(doc.Root.Prop("c"), cmd.SelectionAfterDo);
        Assert.Same(b, cmd.SelectionAfterUndo);
        undo.Undo();
        Assert.Same(b, doc.Root[1], "undo re-inserts the very same node at its index");
    }

    [Test]
    public void DeleteRootFailsAndRecordsNothing()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        Assert.Throws<InvalidOperationException>(() => undo.Execute(new DeleteNodeCommand(doc, doc.Root)));
        Assert.Equal(0, undo.Count);
        Assert.False(undo.CanUndo);
        Assert.True(undo.IsAtSavePoint);
    }

    [Test]
    public void RenameNodeCommand()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        var a = doc.Root.Prop("a");
        AssertDoUndoRedo(doc, undo, new RenameNodeCommand(doc, a, "alpha"), """{"alpha":1,"b":"two","c":[10,20,30],"d":{"x":true}}""");
        Assert.Equal("alpha", a.Name);
    }

    [Test]
    public void EditValueCommand()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        AssertDoUndoRedo(doc, undo, new EditValueCommand(doc, doc.Root.Prop("b"), "three"), """{"a":1,"b":"three","c":[10,20,30],"d":{"x":true}}""");
        AssertDoUndoRedo(doc, undo, new EditValueCommand(doc, doc.Root.Prop("a"), "1.50"), """{"a":1.50,"b":"three","c":[10,20,30],"d":{"x":true}}""");
        AssertDoUndoRedo(doc, undo, new EditValueCommand(doc, doc.Root.Prop("d").Prop("x"), "false"), """{"a":1.50,"b":"three","c":[10,20,30],"d":{"x":false}}""");
        Assert.Equal(3, undo.Count);
    }

    [Test]
    public void InvalidValueFailsAndRecordsNothing()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        long version = doc.Version;
        Assert.Throws<FormatException>(() => undo.Execute(new EditValueCommand(doc, doc.Root.Prop("a"), "not a number")));
        Assert.Throws<FormatException>(() => undo.Execute(new EditValueCommand(doc, doc.Root.Prop("d").Prop("x"), "yes")));
        Assert.Throws<InvalidOperationException>(() => undo.Execute(new EditValueCommand(doc, doc.Root.Prop("c"), "x")));
        Assert.Equal(0, undo.Count);
        Assert.Equal(version, doc.Version);
        Assert.Equal(Start, Json.Compact(doc));
    }

    [Test]
    public void ReplaceNodeCommand()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        var old = doc.Root.Prop("d");
        var replacement = JsonParser.Parse("[1,2]");
        var cmd = new ReplaceNodeCommand(doc, old, replacement);
        AssertDoUndoRedo(doc, undo, cmd, """{"a":1,"b":"two","c":[10,20,30],"d":[1,2]}""");
        Assert.Equal("d", replacement.Name, "the member name carries over");
        Assert.Same(replacement, cmd.SelectionAfterDo);
        Assert.Same(old, cmd.SelectionAfterUndo);
        Assert.Equal("Change Type", cmd.Name);
    }

    [Test]
    public void ReplaceRootCommand()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        var oldRoot = doc.Root;
        AssertDoUndoRedo(doc, undo, new ReplaceNodeCommand(doc, oldRoot, JsonParser.Parse("[true]"), "Edit JSON"), "[true]");
        undo.Undo();
        Assert.Same(oldRoot, doc.Root);
    }

    [Test]
    public void MoveNodeCommandWithinParent()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        var ten = doc.Root.Prop("c")[0];
        AssertDoUndoRedo(doc, undo, new MoveNodeCommand(doc, ten, doc.Root.Prop("c"), 2, null), """{"a":1,"b":"two","c":[20,30,10],"d":{"x":true}}""");
    }

    [Test]
    public void MoveNodeCommandAcrossObjectAndArray()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        var x = doc.Root.Prop("d").Prop("x");

        // object -> array: the name is dropped, and restored by undo
        AssertDoUndoRedo(doc, undo, new MoveNodeCommand(doc, x, doc.Root.Prop("c"), 0, null), """{"a":1,"b":"two","c":[true,10,20,30],"d":{}}""");
        Assert.Null(x.Name);
        undo.Undo();
        Assert.Equal("x", x.Name);

        // array -> object: explicit name, or "item" fallback
        var twenty = doc.Root.Prop("c")[1];
        AssertDoUndoRedo(doc, undo, new MoveNodeCommand(doc, twenty, doc.Root.Prop("d"), 1, "twenty"), """{"a":1,"b":"two","c":[10,30],"d":{"x":true,"twenty":20}}""");
        undo.Undo();
        var thirty = doc.Root.Prop("c")[2];
        AssertDoUndoRedo(doc, undo, new MoveNodeCommand(doc, thirty, doc.Root.Prop("d"), 0, null), """{"a":1,"b":"two","c":[10,20],"d":{"item":30,"x":true}}""");
        undo.Undo();
        Assert.Null(thirty.Name);
    }

    [Test]
    public void MoveNodeIntoItselfIsRejected()
    {
        var doc = Json.Doc("""{"a":{"b":{"c":1}}}""");
        var a = doc.Root.Prop("a");
        Assert.Throws<InvalidOperationException>(() => new MoveNodeCommand(doc, a, a, 0, null));
        Assert.Throws<InvalidOperationException>(() => new MoveNodeCommand(doc, a, a.Prop("b"), 0, null));
    }

    [Test]
    public void FailedMoveRestoresTheNode()
    {
        var doc = Json.Doc("""{"a":[1,2],"b":[]}""");
        var undo = new UndoManager();
        var one = doc.Root.Prop("a")[0];
        Assert.Throws<ArgumentOutOfRangeException>(() => undo.Execute(new MoveNodeCommand(doc, one, doc.Root.Prop("b"), 5, null)));
        Assert.Equal("""{"a":[1,2],"b":[]}""", Json.Compact(doc));
        Assert.Same(one, doc.Root.Prop("a")[0]);
        Assert.Equal(0, undo.Count);
    }

    [Test]
    public void ReorderChildrenCommand()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        var order = doc.Root.Children.Reverse().ToList();
        var cmd = new ReorderChildrenCommand(doc, doc.Root, order);
        AssertDoUndoRedo(doc, undo, cmd, """{"d":{"x":true},"c":[10,20,30],"b":"two","a":1}""");
        Assert.Same(doc.Root, cmd.SelectionAfterDo);
    }

    [Test]
    public void ReorderRequiresAPermutation()
    {
        var doc = Json.Doc("[1,2,3]");
        var undo = new UndoManager();
        var bad = new[] { doc.Root[0], doc.Root[0], doc.Root[1] };
        Assert.Throws<ArgumentException>(() => undo.Execute(new ReorderChildrenCommand(doc, doc.Root, bad)));
        Assert.Equal("[1,2,3]", Json.Compact(doc));
        Assert.Equal(0, undo.Count);
    }

    [Test]
    public void NoopCommandsAreNotRecorded()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        int events = 0;
        undo.StateChanged += (_, _) => events++;
        long version = doc.Version;

        undo.Execute(new RenameNodeCommand(doc, doc.Root.Prop("a"), "a"));
        undo.Execute(new EditValueCommand(doc, doc.Root.Prop("b"), "two"));
        undo.Execute(new ReorderChildrenCommand(doc, doc.Root, doc.Root.Children.ToList()));

        Assert.Equal(0, undo.Count);
        Assert.False(undo.CanUndo);
        Assert.Equal(0, events);
        Assert.Equal(version, doc.Version);
    }

    [Test]
    public void NewEditDiscardsRedoBranch()
    {
        var doc = Json.Doc("[1]");
        var undo = new UndoManager();
        undo.Execute(new EditValueCommand(doc, doc.Root[0], "2"));
        undo.Execute(new EditValueCommand(doc, doc.Root[0], "3"));
        undo.Undo();
        Assert.True(undo.CanRedo);
        undo.Execute(new EditValueCommand(doc, doc.Root[0], "4"));
        Assert.False(undo.CanRedo);
        Assert.Null(undo.Redo());
        Assert.Equal(2, undo.Count);
        undo.Undo();
        Assert.Equal("[2]", Json.Compact(doc));
        undo.Undo();
        Assert.Equal("[1]", Json.Compact(doc));
        Assert.Null(undo.Undo());
    }

    [Test]
    public void SavePointSemantics()
    {
        var doc = Json.Doc("[0]");
        var undo = new UndoManager();
        void Set(string v) => undo.Execute(new EditValueCommand(doc, doc.Root[0], v));

        Assert.True(undo.IsAtSavePoint, "a fresh document is clean");
        Set("1");
        Assert.False(undo.IsAtSavePoint, "dirty after an edit");
        undo.Undo();
        Assert.True(undo.IsAtSavePoint, "undo back to the initial state is clean");
        undo.Redo();
        Set("2");
        undo.MarkSavePoint();
        Assert.True(undo.IsAtSavePoint, "clean right after saving");

        undo.Undo();
        Assert.False(undo.IsAtSavePoint, "undo past the save point is dirty");
        undo.Redo();
        Assert.True(undo.IsAtSavePoint, "redo back to the save point is clean");

        undo.Undo();
        Set("3");
        Assert.False(undo.IsAtSavePoint, "a new branch after undoing past the save point stays dirty");
        undo.Undo();
        Assert.False(undo.IsAtSavePoint, "the saved state is no longer reachable");
        Assert.Equal("[1]", Json.Compact(doc));
    }

    [Test]
    public void SavePointSurvivesHistoryTrimming()
    {
        var doc = Json.Doc("[0]");
        var undo = new UndoManager(maxHistory: 3);
        void Set(string v) => undo.Execute(new EditValueCommand(doc, doc.Root[0], v));

        Set("1");
        undo.MarkSavePoint();           // saved at state after "1"
        Set("2");
        Set("3");
        Set("4");                       // "1" is trimmed out of the history now
        Assert.Equal(3, undo.Count);
        Assert.False(undo.IsAtSavePoint);

        Assert.NotNull(undo.Undo());
        Assert.NotNull(undo.Undo());
        Assert.NotNull(undo.Undo());
        Assert.Null(undo.Undo(), "only MaxHistory steps can be undone");
        Assert.Equal("[1]", Json.Compact(doc));
        Assert.True(undo.IsAtSavePoint, "the oldest reachable state is exactly the saved one");

        // Saved at the initial state, then trimmed: the initial state can no longer be reached.
        var doc2 = Json.Doc("[0]");
        var undo2 = new UndoManager(maxHistory: 2);
        for (int i = 1; i <= 3; i++)
        {
            undo2.Execute(new EditValueCommand(doc2, doc2.Root[0], i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        while (undo2.Undo() is not null)
        {
        }
        Assert.Equal("[1]", Json.Compact(doc2));
        Assert.False(undo2.IsAtSavePoint);
    }

    [Test]
    public void MarkSavePointForEarlierStateAndInvalidate()
    {
        var doc = Json.Doc("[0]");
        var undo = new UndoManager();
        undo.Execute(new EditValueCommand(doc, doc.Root[0], "1"));
        long savedState = undo.CurrentState;   // a background save snapshots here...
        undo.Execute(new EditValueCommand(doc, doc.Root[0], "2"));
        undo.MarkSavePoint(savedState);        // ...and completes after another edit
        Assert.False(undo.IsAtSavePoint);
        undo.Undo();
        Assert.True(undo.IsAtSavePoint);

        undo.InvalidateSavePoint();
        Assert.False(undo.IsAtSavePoint);

        undo.Clear();
        Assert.True(undo.IsAtSavePoint);
        Assert.Equal(0, undo.Count);
        Assert.False(undo.CanUndo);
    }

    [Test]
    public void CompoundIsOneUndoStep()
    {
        var doc = Json.Doc(Start);
        var undo = new UndoManager();
        var executed = new List<(string, UndoAction)>();
        undo.CommandExecuted += (_, e) => executed.Add((e.Command.Name, e.Action));

        using (undo.BeginCompound("Batch"))
        {
            undo.Execute(new EditValueCommand(doc, doc.Root.Prop("a"), "2"));
            Assert.False(undo.CanUndo, "undo is unavailable while a compound is open");
            undo.Execute(new RenameNodeCommand(doc, doc.Root.Prop("b"), "bee"));
            undo.Execute(new DeleteNodeCommand(doc, doc.Root.Prop("c")));
            undo.Execute(new RenameNodeCommand(doc, doc.Root.Prop("d"), "d")); // noop inside compound: ignored
        }

        Assert.Equal(1, undo.Count);
        Assert.Equal("Batch", undo.NextUndo!.Name);
        Assert.Equal(3, ((CompoundCommand)undo.NextUndo).Commands.Count);
        Assert.SequenceEqual(new[] { ("Batch", UndoAction.Do) }, executed);
        const string after = """{"a":2,"bee":"two","d":{"x":true}}""";
        Assert.Equal(after, Json.Compact(doc));

        undo.Undo();
        Assert.Equal(Start, Json.Compact(doc));
        undo.Redo();
        Assert.Equal(after, Json.Compact(doc));
    }

    [Test]
    public void NestedAndEmptyCompounds()
    {
        var doc = Json.Doc("[0,0]");
        var undo = new UndoManager();
        using (undo.BeginCompound("Outer"))
        {
            undo.Execute(new EditValueCommand(doc, doc.Root[0], "1"));
            using (undo.BeginCompound("Inner"))
            {
                undo.Execute(new EditValueCommand(doc, doc.Root[1], "1"));
            }
            Assert.Equal(0, undo.Count, "closing the inner scope does not record anything yet");
        }
        Assert.Equal(1, undo.Count);
        Assert.Equal("Outer", undo.NextUndo!.Name);
        undo.Undo();
        Assert.Equal("[0,0]", Json.Compact(doc));

        var scope = undo.BeginCompound("Nothing");
        scope.Dispose();
        scope.Dispose(); // idempotent
        Assert.Equal(1, undo.Count, "an empty compound is not recorded");
        Assert.True(undo.CanRedo, "and does not discard the redo branch");
    }

    [Test]
    public void CompoundDoRollsBackOnFailure()
    {
        var doc = Json.Doc("[1]");
        var compound = new CompoundCommand("Atomic");
        compound.AddExecuted(new EditValueCommand(doc, doc.Root[0], "2"));
        compound.AddExecuted(new ThrowingCommand());

        Assert.Throws<InvalidOperationException>(compound.Do);
        Assert.Equal("[1]", Json.Compact(doc), "the first command was rolled back");
    }

    [Test]
    public void CompoundSelectionComesFromFirstAndLastCommands()
    {
        var doc = Json.Doc("[1,2]");
        var undo = new UndoManager();
        var first = doc.Root[0];
        var inserted = JNode.CreateNull();
        using (undo.BeginCompound("Two"))
        {
            undo.Execute(new EditValueCommand(doc, first, "5"));
            undo.Execute(new InsertNodeCommand(doc, doc.Root, 2, inserted));
        }
        var compound = (CompoundCommand)undo.NextUndo!;
        Assert.Same(inserted, compound.SelectionAfterDo);
        Assert.Same(first, compound.SelectionAfterUndo);
        Assert.Null(new CompoundCommand("empty").SelectionAfterDo);
        Assert.True(new CompoundCommand("empty").IsNoop);
    }

    [Test]
    public void EventsAndVersion()
    {
        var doc = Json.Doc("{\"a\":1}");
        var undo = new UndoManager();
        var kinds = new List<JDocumentChangeKind>();
        doc.Changed += (_, e) => kinds.Add(e.Kind);
        var actions = new List<UndoAction>();
        undo.CommandExecuted += (_, e) => actions.Add(e.Action);

        undo.Execute(new RenameNodeCommand(doc, doc.Root.Prop("a"), "b"));
        undo.Undo();
        undo.Redo();
        undo.Execute(new InsertNodeCommand(doc, doc.Root, 0, JNode.CreateNull().WithName("n")));
        undo.Execute(new DeleteNodeCommand(doc, doc.Root.Prop("n")));

        Assert.SequenceEqual(new[] { JDocumentChangeKind.Renamed, JDocumentChangeKind.Renamed, JDocumentChangeKind.Renamed, JDocumentChangeKind.Inserted, JDocumentChangeKind.Removed }, kinds);
        Assert.SequenceEqual(new[] { UndoAction.Do, UndoAction.Undo, UndoAction.Redo, UndoAction.Do, UndoAction.Do }, actions);
        Assert.Equal(5L, doc.Version);
    }

    private sealed class ThrowingCommand : EditCommand
    {
        public override string Name => "Throw";

        public override void Do() => throw new InvalidOperationException("boom");

        public override void Undo()
        {
        }
    }
}
