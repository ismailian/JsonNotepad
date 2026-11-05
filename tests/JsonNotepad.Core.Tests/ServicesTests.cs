using System.Collections.Concurrent;
using System.Text;
using JsonNotepad.Core.Editing;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Services;
using JsonNotepad.Core.Session;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

public sealed class ServicesTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // ----- RecentFiles -----------------------------------------------------------

    [Test]
    public void RecentFilesMostRecentFirstAndCaseInsensitiveDedupe()
    {
        var recent = new RecentFiles(capacity: 5);
        int changes = 0;
        recent.Changed += (_, _) => changes++;

        recent.Add(@"C:\a.json");
        recent.Add(@"C:\b.json");
        recent.Add(@"c:\A.JSON");

        Assert.SequenceEqual(new[] { @"c:\A.JSON", @"C:\b.json" }, recent.Items, "re-adding moves to the top with the latest spelling");
        Assert.Equal(3, changes);

        recent.Remove(@"C:\B.json");
        Assert.SequenceEqual(new[] { @"c:\A.JSON" }, recent.Items);
        recent.Remove("not there");
        Assert.Equal(4, changes, "removing a missing entry raises nothing");

        recent.Add("   ");
        Assert.Single(recent.Items);
        recent.Clear();
        Assert.Empty(recent.Items);
    }

    [Test]
    public void RecentFilesCapacity()
    {
        var recent = new RecentFiles(capacity: 3);
        foreach (var name in new[] { "1", "2", "3", "4", "5" })
        {
            recent.Add(name);
        }
        Assert.SequenceEqual(new[] { "5", "4", "3" }, recent.Items);

        recent.SetCapacity(2);
        Assert.SequenceEqual(new[] { "5", "4" }, recent.Items);
        recent.SetCapacity(0);
        Assert.Equal(1, recent.Capacity, "capacity is at least one");
        Assert.SequenceEqual(new[] { "5" }, recent.Items);
    }

    [Test]
    public void RecentFilesInitialListKeepsOrder()
    {
        var recent = new RecentFiles(capacity: 3, initial: ["x", "y", "X", "z", "w"]);
        Assert.SequenceEqual(new[] { "x", "y", "z" }, recent.Items, "the first occurrence (most recent) wins and the list is capped");
    }

    // ----- DocumentStatistics -----------------------------------------------------

    [Test]
    public void DocumentStatisticsCounts()
    {
        var root = JsonParser.Parse("""{"a":[1,2,{"b":"xyz"}],"c":null,"d":true,"e":"hi","f":{}}""");
        var s = DocumentStatistics.Compute(root, CancellationToken.None);

        Assert.Equal(10L, s.TotalNodes);
        Assert.Equal(3L, s.Objects);
        Assert.Equal(1L, s.Arrays);
        Assert.Equal(2L, s.Strings);
        Assert.Equal(2L, s.Numbers);
        Assert.Equal(1L, s.Booleans);
        Assert.Equal(1L, s.Nulls);
        Assert.Equal(3, s.MaxDepth);
        Assert.Equal(3, s.LargestArray);
        Assert.Equal(5, s.LargestObject);
        Assert.Equal(5L, s.TotalStringChars);
        Assert.Equal(3, s.LongestString);
        Assert.Equal(6, s.DistinctKeys);
        Assert.SequenceEqual(new[] { "a", "b", "c", "d", "e", "f" }, s.TopKeys.Select(k => k.Key), "ties are ordered by name");
    }

    [Test]
    public void DocumentStatisticsTopKeys()
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < 20; i++)
        {
            sb.Append(i == 0 ? string.Empty : ",").Append("{\"id\":").Append(i).Append(i % 2 == 0 ? ",\"even\":true" : string.Empty).Append(",\"k").Append(i).Append("\":0}");
        }
        var s = DocumentStatistics.Compute(JsonParser.Parse(sb.Append(']').ToString()), CancellationToken.None);
        Assert.Equal(new KeyValuePair<string, long>("id", 20), s.TopKeys[0]);
        Assert.Equal(new KeyValuePair<string, long>("even", 10), s.TopKeys[1]);
        Assert.Equal(15, s.TopKeys.Count, "only the top 15 are kept");
        Assert.Equal(22, s.DistinctKeys);
        Assert.Equal(20, s.LargestArray);
    }

    [Test]
    public void DocumentStatisticsOfPrimitiveRoot()
    {
        var s = DocumentStatistics.Compute(JNode.CreateString("abcd"), CancellationToken.None);
        Assert.Equal(1L, s.TotalNodes);
        Assert.Equal(0, s.MaxDepth);
        Assert.Equal(4, s.LongestString);
        Assert.Equal(0, s.DistinctKeys);
        Assert.Empty(s.TopKeys);
    }

    // ----- JsonEquality / JsonNumber ----------------------------------------------

    [Test]
    public void JsonEqualityIgnoresMemberOrderAndNumberRepresentation()
    {
        static bool Eq(string a, string b) => JsonEquality.DeepEquals(JsonParser.Parse(a), JsonParser.Parse(b));

        Assert.True(Eq("""{"a":1,"b":[1,2],"c":{"x":null}}""", """{"c":{"x":null},"b":[1,2],"a":1}"""));
        Assert.True(Eq("1", "1.0"));
        Assert.True(Eq("1", "1e0"));
        Assert.True(Eq("100", "1E2"));
        Assert.True(Eq("0", "-0"));
        Assert.True(Eq("12345678901234567890", "12345678901234567890.0"));
        Assert.True(Eq("[]", "[]"));
        Assert.True(Eq("{}", "{}"));

        Assert.False(Eq("[1,2]", "[2,1]"), "array order matters");
        Assert.False(Eq("1", "\"1\""));
        Assert.False(Eq("1", "1.0000001"));
        Assert.False(Eq("""{"a":1}""", """{"a":1,"b":2}"""));
        Assert.False(Eq("""{"a":1}""", """{"b":1}"""));
        Assert.False(Eq("""{"a":{"b":[1,{"c":true}]}}""", """{"a":{"b":[1,{"c":false}]}}"""));
        Assert.False(Eq("\"a\"", "\"A\""));
        Assert.False(Eq("null", "false"));
        Assert.False(Eq("[[]]", "[{}]"));

        var named = JNode.CreateNumber("1").WithName("x");
        Assert.True(JsonEquality.DeepEquals(named, JNode.CreateNumber("1.0")), "the node's own name is not part of its value");
    }

    [Test]
    public void JsonEqualityHashIsConsistent()
    {
        Assert.Equal(JsonEquality.GetHashCode(JsonParser.Parse("1")), JsonEquality.GetHashCode(JsonParser.Parse("1.0")));
        Assert.Equal(
            JsonEquality.GetHashCode(JsonParser.Parse("""{"a":1,"b":2}""")),
            JsonEquality.GetHashCode(JsonParser.Parse("""{"b":2,"a":1}""")));
        Assert.Equal(JsonEquality.GetHashCode(JNode.CreateString("s")), JsonEquality.GetHashCode(JNode.CreateString("s")));
    }

    [Test]
    public void JsonNumberIsValidTable()
    {
        string[] valid = ["0", "-0", "1", "-1", "10", "1.5", "0.5", "-0.0", "1e5", "1E5", "1e+5", "1e-5", "1.5e10", "12345678901234567890", "1e400"];
        string[] invalid = ["", "-", "01", "-01", "1.", ".", ".5", "+1", "1e", "1e+", "0x1", "NaN", "Infinity", " 1", "1 ", "--1", "1.2.3", "1,5", "e5", "1.e5", "\u0661"];
        foreach (var s in valid)
        {
            Assert.True(JsonNumber.IsValid(s), "should be valid: '" + s + "'");
        }
        foreach (var s in invalid)
        {
            Assert.False(JsonNumber.IsValid(s), "should be invalid: '" + s + "'");
        }
    }

    [Test]
    public void JsonNumberTryNormalizeTable()
    {
        var ok = new (string Input, string Expected)[]
        {
            (" 42 ", "42"), (".5", "0.5"), ("-.5", "-0.5"), ("+3", "3"), ("+.25", "0.25"), ("5.", "5"), ("-5.", "-5"),
            ("1.10", "1.10"), ("1e400", "1e400"), ("007", "7"), ("-007.50", "-7.50"), ("\t1E3\n", "1E3"),
        };
        foreach (var (input, expected) in ok)
        {
            Assert.True(JsonNumber.TryNormalize(input, out var literal), "should normalize: '" + input + "'");
            Assert.Equal(expected, literal, input);
            Assert.True(JsonNumber.IsValid(literal));
        }
        foreach (var bad in new[] { null, "", "   ", "abc", "1,5", "1 2", "0x10", "NaN", "Infinity", "1e", "--1", "$5" })
        {
            Assert.False(JsonNumber.TryNormalize(bad, out var literal), "should reject: '" + bad + "'");
            Assert.Equal(string.Empty, literal);
        }
    }

    [Test]
    public void JsonNumberConversions()
    {
        Assert.True(JsonNumber.TryGetDouble("1.5e2", out var d));
        Assert.Equal(150.0, d);
        Assert.True(JsonNumber.TryGetDecimal("0.1", out var m));
        Assert.Equal(0.1m, m);
        Assert.False(JsonNumber.TryGetDecimal("1e400", out _));
        Assert.False(JsonNumber.TryGetDouble(null, out _));
        Assert.True(JsonNumber.NumericEquals("0.1", "1e-1"));
        Assert.False(JsonNumber.NumericEquals("0.1", "0.10000000000000001"), "decimal comparison avoids double rounding");
        Assert.True(JsonNumber.NumericEquals("1e400", "1e400"));
        Assert.Throws<FormatException>(() => JNode.CreateNumber("1."));
    }

    // ----- DelayedActions -------------------------------------------------------

    /// <summary>Runs posted callbacks immediately on the posting (timer) thread.</summary>
    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        public int Posts;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref Posts);
            d(state);
        }
    }

    /// <summary>Queues posted callbacks until the test pumps them, like a UI message loop.</summary>
    private sealed class PumpSynchronizationContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int Pending => _queue.Count;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public int Pump()
        {
            int n = 0;
            while (_queue.TryDequeue(out var item))
            {
                item.Callback(item.State);
                n++;
            }
            return n;
        }
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Timed out waiting for condition.");
            }
            await Task.Delay(10);
        }
    }

    [Test]
    public async Task DelayedActionsOnlyLastOfRapidStartsRuns()
    {
        var context = new InlineSynchronizationContext();
        using var delayed = new DelayedActions(context);
        var ran = new ConcurrentQueue<int>();

        for (int i = 0; i < 10; i++)
        {
            int captured = i;
            delayed.Start("validate", () => ran.Enqueue(captured), TimeSpan.FromMilliseconds(80));
        }
        Assert.True(delayed.IsPending("validate"));

        await WaitUntil(() => !ran.IsEmpty);
        await Task.Delay(250); // give any stale timer a chance to (wrongly) fire
        Assert.SequenceEqual(new[] { 9 }, ran);
        Assert.False(delayed.IsPending("validate"));
    }

    [Test]
    public async Task DelayedActionsCancelAndIndependentNames()
    {
        var context = new InlineSynchronizationContext();
        using var delayed = new DelayedActions(context);
        int a = 0, b = 0;

        delayed.Start("a", () => Interlocked.Increment(ref a), TimeSpan.FromMilliseconds(50));
        delayed.Start("b", () => Interlocked.Increment(ref b), TimeSpan.FromMilliseconds(50));
        Assert.True(delayed.Cancel("a"));
        Assert.False(delayed.Cancel("a"), "already cancelled");
        Assert.False(delayed.Cancel("never started"));

        await WaitUntil(() => Volatile.Read(ref b) == 1);
        await Task.Delay(150);
        Assert.Equal(0, Volatile.Read(ref a));
        Assert.Equal(1, Volatile.Read(ref b));
        Assert.False(delayed.IsPending("a"));
    }

    [Test]
    public async Task DelayedActionsRunOnTheCapturedContext()
    {
        var context = new PumpSynchronizationContext();
        using var delayed = new DelayedActions(context);
        int runs = 0;

        delayed.Start("save", () => runs++, TimeSpan.FromMilliseconds(10));
        await WaitUntil(() => context.Pending == 1);
        Assert.Equal(0, runs, "nothing runs until the context processes the post");
        Assert.True(delayed.IsPending("save"));

        Assert.Equal(1, context.Pump());
        Assert.Equal(1, runs);
        Assert.False(delayed.IsPending("save"));
    }

    [Test]
    public async Task DelayedActionsRestartAfterPostIsIgnoredUntilItsOwnTimer()
    {
        // The timer fired and posted, but the action was restarted before the UI got to it:
        // the stale post must do nothing and only the new entry runs.
        var context = new PumpSynchronizationContext();
        using var delayed = new DelayedActions(context);
        var ran = new List<string>();

        delayed.Start("x", () => ran.Add("first"), TimeSpan.FromMilliseconds(10));
        await WaitUntil(() => context.Pending == 1);
        delayed.Start("x", () => ran.Add("second"), TimeSpan.FromMilliseconds(10));
        await WaitUntil(() => context.Pending == 2);

        context.Pump();
        Assert.SequenceEqual(new[] { "second" }, ran);
    }

    [Test]
    public async Task DisposedDelayedActionsNeverRun()
    {
        var context = new InlineSynchronizationContext();
        var delayed = new DelayedActions(context);
        int runs = 0;
        delayed.Start("x", () => runs++, TimeSpan.FromMilliseconds(30));
        delayed.Dispose();
        delayed.Start("y", () => runs++, TimeSpan.FromMilliseconds(1));
        await Task.Delay(150);
        Assert.Equal(0, runs);
        Assert.False(delayed.IsPending("y"));
    }

    // ----- DocumentSession --------------------------------------------------------

    private static DocumentSession NewSession() => new(new DelayedActions(new InlineSynchronizationContext()));

    [Test]
    public async Task SessionOpenEditSaveRoundTrip()
    {
        var path = _temp.File("session.json");
        await File.WriteAllTextAsync(path, "{\n    \"name\": \"old\",\n    \"n\": 1\n}\n", new UTF8Encoding(true));

        using var session = NewSession();
        int dirtyChanges = 0, replaced = 0, pathChanges = 0;
        session.DirtyChanged += (_, _) => dirtyChanges++;
        session.DocumentReplaced += (_, _) => replaced++;
        session.FilePathChanged += (_, _) => pathChanges++;

        var result = await session.OpenAsync(path, ParseOptions.Default, null, CancellationToken.None);
        Assert.Same(result.Document, session.Document);
        Assert.Equal(Path.GetFullPath(path), session.FilePath);
        Assert.Equal("session.json", session.DisplayName);
        Assert.False(session.IsUntitled);
        Assert.False(session.IsDirty);
        Assert.False(session.IsBusy);
        Assert.Equal(4, session.Format.IndentSize);
        Assert.True(session.Format.HasByteOrderMark);
        Assert.Equal(1, replaced);
        Assert.Equal(1, pathChanges);
        Assert.Same(session.Document, session.Editor.Document);

        Assert.True(session.Editor.SetValue(session.Document.Root.Prop("name"), "new").Success);
        Assert.True(session.IsDirty);
        Assert.Equal(1, dirtyChanges);

        await session.SaveAsync(null, session.Format.ToFormatOptions(FormatOptions.Default), null, CancellationToken.None);
        Assert.False(session.IsDirty);
        Assert.Equal(2, dirtyChanges);
        Assert.Equal(new FileInfo(path).Length, session.FileSize);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3), "the BOM is preserved");
        Assert.Equal("{\n    \"name\": \"new\",\n    \"n\": 1\n}\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "style is preserved");

        session.UndoManager.Undo();
        Assert.True(session.IsDirty, "undo past the save point");
        session.UndoManager.Redo();
        Assert.False(session.IsDirty, "back at the save point");

        session.Editor.Insert(session.Document.Root, InsertPosition.Child, JNodeKind.Null);
        Assert.True(session.IsDirty);
    }

    [Test]
    public async Task SessionSaveAsChangesPath()
    {
        using var session = NewSession();
        session.New(JsonParser.Parse("""{"a":1}"""));
        Assert.True(session.IsUntitled);
        Assert.Equal("Untitled", session.DisplayName);
        Assert.False(session.IsDirty);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveAsync(null, FormatOptions.Default, null, CancellationToken.None));

        session.Editor.Rename(session.Document.Root.Prop("a"), "b");
        Assert.True(session.IsDirty);

        int pathChanges = 0;
        session.FilePathChanged += (_, _) => pathChanges++;
        var target = _temp.File("saved-as.json");
        var utf16 = new FileFormatInfo { Encoding = new UnicodeEncoding(false, true), HasByteOrderMark = true };
        await session.SaveAsync(target, FormatOptions.Compact, utf16, CancellationToken.None);

        Assert.Equal(target, session.FilePath);
        Assert.Equal(1, pathChanges);
        Assert.False(session.IsDirty);
        Assert.Equal(1200, session.Format.Encoding.CodePage);
        Assert.Equal("""{"b":1}""", await File.ReadAllTextAsync(target), "File.ReadAllText detects the UTF-16 BOM");

        // Reopen what we wrote.
        using var second = NewSession();
        await second.OpenAsync(target, ParseOptions.Default, null, CancellationToken.None);
        Assert.Equal("""{"b":1}""", Json.Compact(second.Document));
        Assert.Equal(1200, second.Format.Encoding.CodePage);
    }

    [Test]
    public async Task SessionFailedOpenKeepsCurrentDocument()
    {
        var bad = _temp.File("bad.json");
        await File.WriteAllTextAsync(bad, "{ nope");
        using var session = NewSession();
        session.New(JsonParser.Parse("[1]"));
        session.Editor.SetValue(session.Document.Root[0], "2");
        var before = session.Document;

        await Assert.ThrowsAsync<JsonParseException>(() => session.OpenAsync(bad, ParseOptions.Default, null, CancellationToken.None));
        Assert.Same(before, session.Document);
        Assert.True(session.IsDirty, "the unsaved edit is still there");
        Assert.True(session.IsUntitled);
        Assert.False(session.IsBusy);
    }

    [Test]
    public async Task SessionEditDuringSaveStaysDirty()
    {
        using var session = NewSession();
        session.New(JsonParser.Parse("[0]"));
        session.Editor.SetValue(session.Document.Root[0], "1");
        var path = _temp.File("during.json");

        var save = session.SaveAsync(path, FormatOptions.Compact, null, CancellationToken.None);
        session.Editor.SetValue(session.Document.Root[0], "2"); // after the snapshot was taken
        await save;

        Assert.Equal("[1]", await File.ReadAllTextAsync(path), "the snapshot was written");
        Assert.True(session.IsDirty, "the later edit is not saved");
        session.UndoManager.Undo();
        Assert.False(session.IsDirty, "undoing the later edit returns to the saved state");
    }
}
