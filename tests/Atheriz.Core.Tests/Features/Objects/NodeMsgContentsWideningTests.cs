// Pins for the widened Node.MsgContents signature (Node.Links.cs,
// ContentUtils.cs): the node and object overloads keep their contracts
// (empty delivery, mapping copy, exclude) while accepting widened
// collection types with no bridging copies in EmitToLocation.
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class NodeMsgContentsWideningTests
{
    private static (Node node, GameObject room, GameObject n1, GameObject n2, GameObject r1) Setup()
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("widen", 0, 0, 0));
        nh.AddNode(node);
        var room = GameObject.Create("room", isContainer: true);
        var n1 = GameObject.Create("node-lis-1");
        var n2 = GameObject.Create("node-lis-2");
        var r1 = GameObject.Create("room-lis-1");
        foreach (var o in new GameObject[] { node, room, n1, n2, r1 })
            if (ObjectRegistry.Get(o.Id).Count == 0)
                ObjectRegistry.AddObject(o);
        Assert.True(n1.MoveTo(node));
        Assert.True(n2.MoveTo(node));
        Assert.True(r1.MoveTo(room));
        foreach (var o in new[] { n1, n2, r1 }) o.ClearMessages();
        return (node, room, n1, n2, r1);
    }

    [Fact]
    public void NodeMsgContents_EmptyText_DeliversEmpty()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (node, _, n1, _, _) = Setup();
            node.MsgContents("");
            Assert.Contains("", n1.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ObjectMsgContents_EmptyText_DeliversEmpty()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (_, room, _, _, r1) = Setup();
            room.MsgContents("");
            Assert.Contains("", r1.PeekMessages());
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void NodeMsgContents_WidenedMapping_NotMutated_AndDelivers()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (node, _, n1, n2, _) = Setup();
            IDictionary<string, object?> mapping = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["k"] = "v" };
            node.MsgContents("Say {k}.", mapping: mapping);
            Assert.False(mapping.ContainsKey("you"));
            Assert.Contains(n1.PeekMessages(), m => m.Contains("Say v."));
            Assert.Contains(n2.PeekMessages(), m => m.Contains("Say v."));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ObjectMsgContents_WidenedMapping_NotMutated_AndDelivers()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (_, room, _, _, r1) = Setup();
            IDictionary<string, object?> mapping = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["k"] = "v" };
            room.MsgContents("Say {k}.", mapping: mapping);
            Assert.False(mapping.ContainsKey("you"));
            Assert.Contains(r1.PeekMessages(), m => m.Contains("Say v."));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void NodeMsgContents_WidenedExclude_Honored()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (node, _, n1, n2, _) = Setup();
            IEnumerable<GameObject> exclude = new HashSet<GameObject> { n1 };
            node.MsgContents("shout", exclude: exclude);
            Assert.DoesNotContain(n1.PeekMessages(), m => m.Contains("shout"));
            Assert.Contains(n2.PeekMessages(), m => m.Contains("shout"));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void EmitToLocation_WidenedCollections_DeliversWithoutMutating()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var (node, room, n1, n2, r1) = Setup();
            IDictionary<string, object?> nodeMapping = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["k"] = "v" };
            IEnumerable<GameObject> nodeExclude = new HashSet<GameObject> { n1 };
            ContentUtils.EmitToLocation(node, "Say {k}.", mapping: nodeMapping, exclude: nodeExclude);
            Assert.False(nodeMapping.ContainsKey("you"));
            Assert.DoesNotContain(n1.PeekMessages(), m => m.Contains("Say v."));
            Assert.Contains(n2.PeekMessages(), m => m.Contains("Say v."));

            IDictionary<string, object?> roomMapping = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["k"] = "v" };
            ContentUtils.EmitToLocation(room, "Say {k}.", mapping: roomMapping);
            Assert.False(roomMapping.ContainsKey("you"));
            Assert.Contains(r1.PeekMessages(), m => m.Contains("Say v."));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
