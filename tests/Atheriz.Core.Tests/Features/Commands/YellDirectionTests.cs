using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class YellDirectionTests
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

    private static (Node src, Node dst, GameObject emitter, CapturingObject receiver) SetupPair(
        Coord srcCoord, Coord dstCoord)
    {
        ObjectRegistry.ClearAll();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var src = new Node(srcCoord);
        var dst = new Node(dstCoord);
        nh.AddNode(src);
        nh.AddNode(dst);
        var emitter = new GameObject { Name = "Yeller", IsPc = true };
        emitter.Id = IdGenerator.GetUniqueId();
        ObjectRegistry.AddObject(emitter);
        emitter.IsConnected = true;
        Assert.True(emitter.MoveTo(src));
        var receiver = new CapturingObject { Name = "Hearer", IsPc = true };
        receiver.Id = IdGenerator.GetUniqueId();
        ObjectRegistry.AddObject(receiver);
        receiver.IsConnected = true;
        Assert.True(receiver.MoveTo(dst));
        emitter.ClearMessages();
        receiver.ClearMessages();
        receiver.Deliveries.Clear();
        return (src, dst, emitter, receiver);
    }

    [Fact]
    public void Yell_SameRoom_KeepsBareLine()
    {
        try
        {
            ObjectRegistry.ClearAll();
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            var node = new Node(new Coord("yelldir", 0, 0, 0));
            nh.AddNode(node);
            var emitter = new GameObject { Name = "Yeller", IsPc = true };
            emitter.Id = IdGenerator.GetUniqueId();
            ObjectRegistry.AddObject(emitter);
            Assert.True(emitter.MoveTo(node));
            var receiver = new CapturingObject { Name = "Hearer", IsPc = true };
            receiver.Id = IdGenerator.GetUniqueId();
            ObjectRegistry.AddObject(receiver);
            Assert.True(receiver.MoveTo(node));
            emitter.ClearMessages();
            receiver.ClearMessages();
            receiver.Deliveries.Clear();
            Atheriz.Core.Commands.LoggedIn.YellCommand.YellToArea(emitter, "help me");
            var heard = Assert.Single(receiver.Deliveries);
            Assert.Contains("yells 'help me'", heard.Text);
            Assert.DoesNotContain("from above", heard.Text);
            Assert.DoesNotContain("from below", heard.Text);
            Assert.DoesNotContain("to the", heard.Text);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Yell_RoomToNorth_NamesDirection()
    {
        try
        {
            var (_, _, emitter, receiver) = SetupPair(
                new Coord("yelldir", 0, 0, 0), new Coord("yelldir", 0, -2, 0));
            Atheriz.Core.Commands.LoggedIn.YellCommand.YellToArea(emitter, "help me");
            var heard = Assert.Single(receiver.Deliveries);
            Assert.Contains("yells to the north: 'help me'", heard.Text);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Yell_RoomAboveSouthwest_MatchesHearOrder()
    {
        try
        {
            var (_, _, emitter, receiver) = SetupPair(
                new Coord("yelldir", -2, -2, 1), new Coord("yelldir", 0, 0, 0));
            Atheriz.Core.Commands.LoggedIn.YellCommand.YellToArea(emitter, "to arms");
            var heard = Assert.Single(receiver.Deliveries);
            Assert.Contains("yells from above you to the southwest: 'to arms'", heard.Text);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Yell_RoomBelow_NamesBelow()
    {
        try
        {
            var (_, _, emitter, receiver) = SetupPair(
                new Coord("yelldir", 0, 0, -1), new Coord("yelldir", 0, 0, 0));
            Atheriz.Core.Commands.LoggedIn.YellCommand.YellToArea(emitter, "help me");
            var heard = Assert.Single(receiver.Deliveries);
            Assert.Contains("yells from below you: 'help me'", heard.Text);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Yell_RemoteDelivery_KeepsYellTypeAndRawLine()
    {
        try
        {
            var (_, _, emitter, receiver) = SetupPair(
                new Coord("yelldir", 5, 0, 0), new Coord("yelldir", 0, 0, 0));
            Atheriz.Core.Commands.LoggedIn.YellCommand.YellToArea(emitter, "help me");
            var delivery = Assert.Single(receiver.Deliveries);
            Assert.Equal("help me", delivery.RawMsg);
            Assert.Equal("yell", delivery.MsgType);
            Assert.Contains("to the east", delivery.Text);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Yell_RemoteLine_StaysVerbatim()
    {
        try
        {
            var (_, _, emitter, receiver) = SetupPair(
                new Coord("yelldir", 5, 0, 0), new Coord("yelldir", 0, 0, 0));
            string line = "one two three four five six seven eight nine ten";
            Atheriz.Core.Commands.LoggedIn.YellCommand.YellToArea(emitter, line);
            var heard = Assert.Single(receiver.Deliveries);
            Assert.Contains(line, heard.Text);
            Assert.DoesNotContain("...", heard.Text);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Yell_RoomToSouth_NamesDirection()
    {
        try
        {
            var (_, _, emitter, receiver) = SetupPair(
                new Coord("yelldir", 0, -2, 0), new Coord("yelldir", 0, 0, 0));
            Atheriz.Core.Commands.LoggedIn.YellCommand.YellToArea(emitter, "help me");
            var heard = Assert.Single(receiver.Deliveries);
            Assert.Contains("yells to the south: 'help me'", heard.Text);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Yell_RoomToWest_NamesDirection()
    {
        try
        {
            var (_, _, emitter, receiver) = SetupPair(
                new Coord("yelldir", -2, 0, 0), new Coord("yelldir", 0, 0, 0));
            Atheriz.Core.Commands.LoggedIn.YellCommand.YellToArea(emitter, "help me");
            var heard = Assert.Single(receiver.Deliveries);
            Assert.Contains("yells to the west: 'help me'", heard.Text);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Yell_RoomAboveSameXY_NamesAboveOnly()
    {
        try
        {
            var (_, _, emitter, receiver) = SetupPair(
                new Coord("yelldir", 0, 0, 1), new Coord("yelldir", 0, 0, 0));
            Atheriz.Core.Commands.LoggedIn.YellCommand.YellToArea(emitter, "help me");
            var heard = Assert.Single(receiver.Deliveries);
            Assert.Contains("yells from above you: 'help me'", heard.Text);
            Assert.DoesNotContain("to the", heard.Text);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void HearDirection_DifferentAreas_ReturnsEmpty()
    {
        var a = new Node(new Coord("areaA", 0, 0, 1));
        var b = new Node(new Coord("areaB", 5, 5, 0));
        var (zStr, dirStr) = GameObject.HearDirection(b, a);
        Assert.Equal("", zStr);
        Assert.Equal("", dirStr);
    }

    [Fact]
    public void HearDirection_NonNodeLocations_ReturnsEmpty()
    {
        var roomA = new GameObject { Name = "roomA" };
        var roomB = new GameObject { Name = "roomB" };
        var (zStr, dirStr) = GameObject.HearDirection(roomA, roomB);
        Assert.Equal("", zStr);
        Assert.Equal("", dirStr);
    }

    [Fact]
    public void HearDirection_SameCoord_ReturnsEmpty()
    {
        var a = new Node(new Coord("yelldir", 3, 3, 0));
        var b = new Node(new Coord("yelldir", 3, 3, 0));
        var (zStr, dirStr) = GameObject.HearDirection(b, a);
        Assert.Equal("", zStr);
        Assert.Equal("", dirStr);
    }

    [Fact]
    public void HearDirection_MatchesHearRendering()
    {
        try
        {
            var (_, _, emitter, receiver) = SetupPair(
                new Coord("yelldir", -2, -2, 1), new Coord("yelldir", 0, 0, 0));
            var emitterLoc = emitter.ResolveLocationObject();
            var receiverLoc = receiver.ResolveLocationObject();
            var (zStr, dirStr) = GameObject.HearDirection(receiverLoc, emitterLoc);
            Assert.Equal(" from above you", zStr);
            Assert.Equal(" to the southwest", dirStr);
            receiver.CanHear = true;
            receiver.ClearMessages();
            receiver.Deliveries.Clear();
            receiver.AtHear(emitter, "Someone says,", " \"hello\"", 55.0, true);
            var heard = Assert.Single(receiver.PeekMessages());
            Assert.Contains($"{zStr}{dirStr}: ", heard);
            Assert.DoesNotContain("loud", heard);
            Assert.DoesNotContain("faint", heard);
            Assert.DoesNotContain("inaudible", heard);
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
