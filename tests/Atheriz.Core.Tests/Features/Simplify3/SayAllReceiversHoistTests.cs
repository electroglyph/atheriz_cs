// Pins for the hoisted all-receivers join (GameObjectMessaging.cs): the
// per-receiver mapping still carries the receiver's own name plus the full
// join, byte-identical across receivers.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class SayAllReceiversHoistTests
{
    private static GameObject Mk(Node node, string name)
    {
        var o = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(o);
        o.IsConnected = true;
        Assert.True(o.MoveTo(node));
        o.ClearMessages();
        return o;
    }

    [Fact]
    public void AtSayFull_MultiReceiver_PerReceiverPayloadsMatchExpected()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("sayhoist", 0, 0, 0));
        nh.AddNode(node);
        var speaker = Mk(node, "speaker");
        var r1 = Mk(node, "r1");
        var r2 = Mk(node, "r2");

        speaker.ClearMessages(); r1.ClearMessages(); r2.ClearMessages();
        speaker.AtSayFull("hello", msgSelf: false, receivers: [r1, r2], msgReceivers: "{receiver}|{all_receivers}|{speech}");

        Assert.Equal("r1|r1, r2|hello", Assert.Single(r1.PeekMessages()));
        Assert.Equal("r2|r1, r2|hello", Assert.Single(r2.PeekMessages()));
    }

    [Fact]
    public void AtSayFull_ThreeReceivers_SharedJoinIdentical()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord("sayhoist3", 0, 0, 0));
        nh.AddNode(node);
        var speaker = Mk(node, "speaker");
        var a = Mk(node, "a1");
        var b = Mk(node, "b2");
        var c = Mk(node, "c3");

        speaker.ClearMessages(); a.ClearMessages(); b.ClearMessages(); c.ClearMessages();
        speaker.AtSayFull("hi", msgSelf: false, receivers: [a, b, c], msgReceivers: "{all_receivers}");

        Assert.Equal("a1, b2, c3", Assert.Single(a.PeekMessages()));
        Assert.Equal("a1, b2, c3", Assert.Single(b.PeekMessages()));
        Assert.Equal("a1, b2, c3", Assert.Single(c.PeekMessages()));
    }
}
