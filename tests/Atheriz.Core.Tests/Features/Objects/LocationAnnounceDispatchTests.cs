using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Location announce keeps dynamic dispatch through one helper: node
// locations run the live-contents overload, other locations the
// registry-snapshot overload — and both keep their empty-message contract.
[Collection("Ported")]
public class LocationAnnounceDispatchTests
{
    private static (Node node, GameObject speaker, GameObject hearer, GameObject bystander) Setup()
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("announcearea", 0, 0, 0));
        nh.AddNode(node);
        GameObject Mk(string name)
        {
            var o = GameObject.Create(name, isPc: true);
            ObjectRegistry.AddObject(o);
            o.IsConnected = true;
            Assert.True(o.MoveTo(node));
            o.ClearMessages();
            return o;
        }
        return (node, Mk("speaker"), Mk("hearer"), Mk("bystander"));
    }

    private static void RegisterAll(params GameObject[] objs)
    {
        foreach (var o in objs)
            if (ObjectRegistry.Get(o.Id).Count == 0)
                ObjectRegistry.AddObject(o);
    }

    [Fact]
    public void Say_ToNodeLocation_ReachesBystander()
    {
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var (_, speaker, _, bystander) = Setup();
            speaker.AtSayFull("hello there", msgSelf: true);
            Assert.Contains(bystander.PeekMessages(), m => m.Contains("says"));
            Assert.Contains(bystander.PeekMessages(), m => m.Contains("hello there"));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void NodeEmptyMessage_DeliversEmpty()
    {
        // Node contract: empty text still delivers ("" via the bare send).
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("emptyarea", 0, 0, 0));
            var hearer = GameObject.Create("hearer", isPc: true);
            RegisterAll(node, hearer);
            Assert.True(hearer.MoveTo(node));
            hearer.ClearMessages();
            node.MsgContents("");
            Assert.Contains("", hearer.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ObjectEmptyMessage_DeliversEmpty()
    {
        // Object contract: empty text parses to "" and still delivers.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var hearer = GameObject.Create("hearer", isPc: true);
            RegisterAll(room, hearer);
            Assert.True(hearer.MoveTo(room));
            hearer.ClearMessages();
            room.MsgContents("");
            Assert.Contains("", hearer.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void EmitToLocation_NodeUsesNodeOverload()
    {
        // The helper dispatches like the inlined branch did: node delivery
        // excludes the listed objects and still arrives for the rest.
        ObjectRegistry.ClearAll();
        NodeHandler.SetCurrent(null);
        try
        {
            var (node, speaker, _, bystander) = Setup();
            ContentUtils.EmitToLocation(node, "a shout", fromObj: speaker,
                mapping: new Dictionary<string, object?>(StringComparer.Ordinal),
                exclude: [speaker], msgType: "say");
            Assert.Contains(bystander.PeekMessages(), m => m.Contains("a shout"));
            Assert.DoesNotContain(speaker.PeekMessages(), m => m.Contains("a shout"));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
