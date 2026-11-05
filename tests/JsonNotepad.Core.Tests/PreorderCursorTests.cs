using System.Diagnostics;
using System.Text;
using JsonNotepad.Core.IO;
using JsonNotepad.Core.Model;
using JsonNotepad.Core.Tests.Framework;

namespace JsonNotepad.Core.Tests;

public sealed class PreorderCursorTests
{
    private static readonly JNode Root = JsonParser.Parse("""
        {"a":1,"b":{"c":[2,3,{"d":[]}],"e":{}},"f":[[[4]]],"g":null}
        """);

    private static List<JNode> Forward(PreorderCursor cursor)
    {
        var list = new List<JNode> { cursor.Current };
        while (cursor.MoveNext())
        {
            list.Add(cursor.Current);
        }
        return list;
    }

    private static List<JNode> Backward(PreorderCursor cursor)
    {
        var list = new List<JNode> { cursor.Current };
        while (cursor.MovePrevious())
        {
            list.Add(cursor.Current);
        }
        return list;
    }

    [Test]
    public void ForwardTraversalMatchesDescendantsAndSelf()
    {
        var expected = Root.DescendantsAndSelf().ToList();
        Assert.Equal(14, expected.Count);
        var actual = Forward(new PreorderCursor(Root));
        Assert.SequenceEqual(expected, actual);
    }

    [Test]
    public void BackwardTraversalIsTheReverse()
    {
        var cursor = new PreorderCursor(Root);
        cursor.MoveToLast();
        Assert.Same(Root.Prop("g"), cursor.Current);
        var expected = Root.DescendantsAndSelf().Reverse().ToList();
        Assert.SequenceEqual(expected, Backward(cursor));
    }

    [Test]
    public void LastNodeIsDeepestLastDescendant()
    {
        var root = JsonParser.Parse("[1,[2,[3,[4]]]]");
        var cursor = new PreorderCursor(root);
        cursor.MoveToLast();
        Assert.Equal("4", cursor.Current.Value);
        cursor.MoveToFirst();
        Assert.Same(root, cursor.Current);
    }

    [Test]
    public void StartingMidTree()
    {
        var all = Root.DescendantsAndSelf().ToList();
        for (int i = 0; i < all.Count; i++)
        {
            Assert.SequenceEqual(all.Skip(i), Forward(new PreorderCursor(Root, all[i])), "forward from #" + i);
            Assert.SequenceEqual(all.Take(i + 1).Reverse(), Backward(new PreorderCursor(Root, all[i])), "backward from #" + i);
        }
    }

    [Test]
    public void MoveToRepositions()
    {
        var cursor = new PreorderCursor(Root);
        var d = Root.Prop("b").Prop("c")[2].Prop("d");
        cursor.MoveTo(d);
        Assert.True(cursor.MoveNext());
        Assert.Same(Root.Prop("b").Prop("e"), cursor.Current);
        Assert.True(cursor.MovePrevious());
        Assert.Same(d, cursor.Current);
        Assert.True(cursor.MovePrevious());
        Assert.Same(Root.Prop("b").Prop("c")[2], cursor.Current);
    }

    [Test]
    public void SingleNodeTree()
    {
        var single = JNode.CreateNumber("1");
        var cursor = new PreorderCursor(single);
        Assert.False(cursor.MoveNext());
        Assert.False(cursor.MovePrevious());
        Assert.Same(single, cursor.Current);
        cursor.MoveToLast();
        Assert.Same(single, cursor.Current);
    }

    [Test]
    public void FailedMoveNextLeavesCursorUsable()
    {
        // Running off the end must not corrupt the cursor: it stays on the last node
        // and can walk back from there.
        var cursor = new PreorderCursor(Root);
        cursor.MoveToLast();
        Assert.False(cursor.MoveNext());
        Assert.Same(Root.Prop("g"), cursor.Current);
        Assert.True(cursor.MovePrevious(), "the cursor still knows its position");
        Assert.Same(Root.Prop("f")[0][0][0], cursor.Current);

        var forward = new PreorderCursor(Root);
        while (forward.MoveNext())
        {
        }
        Assert.False(forward.MoveNext(), "stays at the end");
        Assert.SequenceEqual(Root.DescendantsAndSelf().Reverse(), Backward(forward));
    }

    [Test]
    public void FailedMovePreviousLeavesCursorAtRoot()
    {
        var cursor = new PreorderCursor(Root);
        Assert.False(cursor.MovePrevious());
        Assert.Same(Root, cursor.Current);
        Assert.True(cursor.MoveNext());
        Assert.Same(Root.Prop("a"), cursor.Current);
    }

    [Test]
    public void IndexOfAndNodeAt()
    {
        var all = Root.DescendantsAndSelf().ToList();
        for (int i = 0; i < all.Count; i++)
        {
            Assert.Equal(i, PreorderCursor.IndexOf(Root, all[i]));
            Assert.Same(all[i], PreorderCursor.NodeAt(Root, i));
        }
        Assert.Null(PreorderCursor.NodeAt(Root, -1));
        Assert.Null(PreorderCursor.NodeAt(Root, all.Count));
        Assert.Equal(-1, PreorderCursor.IndexOf(Root, JNode.CreateNull()));
    }

    [Test]
    public void LargeFlatArrayIsLinear()
    {
        var sb = new StringBuilder("[");
        const int n = 300_000;
        for (int i = 0; i < n; i++)
        {
            sb.Append(i == 0 ? "0" : ",0");
        }
        var root = JsonParser.Parse(sb.Append(']').ToString());

        var sw = Stopwatch.StartNew();
        var cursor = new PreorderCursor(root);
        int forward = 0;
        while (cursor.MoveNext())
        {
            forward++;
        }
        int backward = 0;
        while (cursor.MovePrevious())
        {
            backward++;
        }
        sw.Stop();
        Assert.Equal(n, forward);
        Assert.Equal(n, backward);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"{2 * n} steps took {sw.ElapsedMilliseconds} ms (quadratic?)");
    }
}
