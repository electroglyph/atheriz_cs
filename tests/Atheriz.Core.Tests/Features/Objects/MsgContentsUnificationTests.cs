using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Node and object MsgContents share one broadcast core while keeping their
// contracts: identical delivery for plain text, empty delivery on both, and
// the preserved error split (object rethrows ParsingError under raiseErrors,
// node never throws).
[Collection("Ported")]
public class MsgContentsUnificationTests
{
    private static void RegisterAll(params GameObject[] objs)
    {
        foreach (var o in objs)
            if (ObjectRegistry.Get(o.Id).Count == 0)
                ObjectRegistry.AddObject(o);
    }

    private static (Node node, GameObject room, GameObject n1, GameObject n2, GameObject r1, GameObject r2) Setup()
    {
        // Handler-backed node like the say tests: MoveTo(node) resolves
        // through it.
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("unify", 0, 0, 0));
        nh.AddNode(node);
        var room = GameObject.Create("room", isContainer: true);
        var n1 = GameObject.Create("node-lis-1");
        var n2 = GameObject.Create("node-lis-2");
        var r1 = GameObject.Create("room-lis-1");
        var r2 = GameObject.Create("room-lis-2");
        RegisterAll(node, room, n1, n2, r1, r2);
        Assert.True(n1.MoveTo(node));
        Assert.True(n2.MoveTo(node));
        Assert.True(r1.MoveTo(room));
        Assert.True(r2.MoveTo(room));
        foreach (var o in new[] { n1, n2, r1, r2 }) o.ClearMessages();
        return (node, room, n1, n2, r1, r2);
    }

    [Fact]
    public void PlainText_DeliversOnBothPaths()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (node, room, n1, n2, r1, r2) = Setup();
            node.MsgContents("node says hi");
            room.MsgContents("room says hi");
            Assert.Contains(n1.PeekMessages(), m => m.Contains("node says hi"));
            Assert.Contains(n2.PeekMessages(), m => m.Contains("node says hi"));
            Assert.Contains(r1.PeekMessages(), m => m.Contains("room says hi"));
            Assert.Contains(r2.PeekMessages(), m => m.Contains("room says hi"));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void EmptyText_DeliversEmptyOnBothPaths()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (node, room, n1, _, r1, _) = Setup();
            node.MsgContents("");
            room.MsgContents("");
            Assert.Contains("", n1.PeekMessages());
            Assert.Contains("", r1.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Exclude_HonoredOnBothPaths()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (node, room, n1, n2, r1, r2) = Setup();
            node.MsgContents("shout", exclude: [n1]);
            room.MsgContents("shout", exclude: [r1]);
            Assert.DoesNotContain(n1.PeekMessages(), m => m.Contains("shout"));
            Assert.Contains(n2.PeekMessages(), m => m.Contains("shout"));
            Assert.DoesNotContain(r1.PeekMessages(), m => m.Contains("shout"));
            Assert.Contains(r2.PeekMessages(), m => m.Contains("shout"));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void RaiseErrors_ObjectRethrows_NodeFallsBack()
    {
        // Overlong input raises ParsingError inside the parser: the object
        // contract rethrows it under raiseErrors, the node contract falls
        // back to raw text and never throws.
        ObjectRegistry.ClearAll();
        try
        {
            var (node, room, n1, _, r1, _) = Setup();
            string big = new('x', FuncParser.MaxMessageSize + 1);
            Assert.Throws<FuncParser.ParsingError>(() => room.MsgContents(big, raiseErrors: true));
            var ex = Record.Exception(() => node.MsgContents(big, raiseErrors: true));
            Assert.Null(ex);
            var ex2 = Record.Exception(() => room.MsgContents(big));
            Assert.Null(ex2);
            Assert.Contains(r1.PeekMessages(), m => m.Length > FuncParser.MaxMessageSize);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
