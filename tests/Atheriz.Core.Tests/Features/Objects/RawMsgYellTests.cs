using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// rawMsg passthrough (talk.md Batch 2 step 5) + YellCommand area broadcast
// (step 6). Mock-free: real NodeHandler/ObjectRegistry paths.
[Collection("Ported")]
public sealed class RawMsgYellTests
{
    private sealed class CapturingObject : GameObject
    {
        public List<(string Text, string? RawMsg, string? MsgType)> Deliveries { get; } = [];
        public override void Msg(string text, GameObject? fromObj, IDictionary<string, object?>? mapping, bool raiseErrors = false, string? msgType = null, string? rawMsg = null)
        {
            Deliveries.Add((text, rawMsg, msgType));
            base.Msg(text, fromObj, mapping, raiseErrors, msgType, rawMsg);
        }
    }

    private static (Node node, CapturingObject speaker, CapturingObject hearer, CapturingObject bystander) Setup(string area = "rawmsgarea")
    {
        ObjectRegistry.ClearAll();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var node = new Node(new Coord(area, 0, 0, 0));
        nh.AddNode(node);
        CapturingObject mk(string name)
        {
            var o = new CapturingObject { Name = name, IsPc = true };
            o.Id = IdGenerator.GetUniqueId();
            ObjectRegistry.AddObject(o);
            o.IsConnected = true;
            Assert.True(o.MoveTo(node));
            o.ClearMessages();
            o.Deliveries.Clear();
            return o;
        }
        return (node, mk("speaker"), mk("hearer"), mk("bystander"));
    }

    [Fact]
    public void EmitToContents_ForwardsSpeech_AsRawMsg()
    {
        try
        {
            var (_, speaker, _, bystander) = Setup();
            speaker.AtSayFull("hello", msgSelf: true);
            var bystanderDelivery = Assert.Single(bystander.Deliveries);
            Assert.Equal("hello", bystanderDelivery.RawMsg);
            Assert.Equal("say", bystanderDelivery.MsgType);
            Assert.Contains("says", bystanderDelivery.Text);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Msg_DefaultRawMsg_Null_PreservesBehavior()
    {
        try
        {
            ObjectRegistry.ClearAll();
            NodeHandler.SetCurrent(null);
            var receiver = new CapturingObject { Name = "R", IsPc = true };
            receiver.Id = IdGenerator.GetUniqueId();
            ObjectRegistry.AddObject(receiver);
            receiver.Msg("plain line");
            var delivery = Assert.Single(receiver.Deliveries);
            Assert.Null(delivery.RawMsg);
            Assert.Null(delivery.MsgType);
            Assert.Equal("plain line", delivery.Text);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtSayFull_SelfDelivery_CarriesBareRawMsg()
    {
        try
        {
            var (_, speaker, _, _) = Setup();
            speaker.AtSayFull("hello", msgSelf: true);
            var selfDelivery = Assert.Single(speaker.Deliveries, d => d.MsgType == "say");
            Assert.Equal("hello", selfDelivery.RawMsg);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtSayFull_WhisperReceiver_GetsBareRawMsg()
    {
        try
        {
            var (_, speaker, hearer, bystander) = Setup();
            speaker.AtSayFull("secret", msgSelf: true, receivers: [hearer], whisper: true);
            var hearerDelivery = Assert.Single(hearer.Deliveries, d => d.MsgType == "whisper");
            Assert.Equal("secret", hearerDelivery.RawMsg);
            Assert.Contains("whispers", hearerDelivery.Text);
            Assert.Empty(bystander.Deliveries);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Yell_AreaReach_BystanderHears()
    {
        try
        {
            var (_, emitter, _, bystander) = Setup();
            Atheriz.Core.Commands.LoggedIn.YellCommand.YellToArea(emitter, "help me");
            Assert.Contains(emitter.PeekMessages(), m => m.Contains("You yell 'help me'"));
            var bystanderDelivery = Assert.Single(bystander.Deliveries);
            Assert.Equal("help me", bystanderDelivery.RawMsg);
            Assert.Equal("yell", bystanderDelivery.MsgType);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Yell_UnregisteredArea_FallsBackToRoom()
    {
        try
        {
            ObjectRegistry.ClearAll();
            NodeHandler.SetCurrent(null);
            var room = new GameObject { Name = "room", IsPc = false };
            room.Id = IdGenerator.GetUniqueId();
            ObjectRegistry.AddObject(room);
            CapturingObject mk(string name, bool pc)
            {
                var o = new CapturingObject { Name = name, IsPc = pc, IsNpc = !pc };
                o.Id = IdGenerator.GetUniqueId();
                ObjectRegistry.AddObject(o);
                Assert.True(o.MoveTo(room));
                o.ClearMessages();
                o.Deliveries.Clear();
                return o;
            }
            var emitter = mk("Emitter", true);
            var buddy = mk("Buddy", true);
            Atheriz.Core.Commands.LoggedIn.YellCommand.YellToArea(emitter, "fallback yell");
            Assert.Contains(buddy.PeekMessages(), m => m.Contains("yells 'fallback yell'"));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
