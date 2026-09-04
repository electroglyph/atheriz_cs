// Port of atheriz/tests/test_give.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedGiveTests
{
    private static (GameObject giver, GameObject receiver, Node node) SetupGiveScenario(string area = "testarea")
    {
        var nh = new NodeHandler();
        var node = new Node(new Coord(area, 0, 0, 0));
        nh.AddNode(node);
        var giver = GameObject.Create("giver", isPc: true);
        ObjectRegistry.AddObject(giver);
        giver.IsConnected = true;
        giver.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(giver);
        var receiver = GameObject.Create("receiver", isPc: true);
        ObjectRegistry.AddObject(receiver);
        receiver.IsConnected = true;
        receiver.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(receiver);
        giver.ClearMessages(); receiver.ClearMessages();
        return (giver, receiver, node);
    }

    [Fact]
    public void GiveItem()
    {
        using var env = GlobalTestEnv.Enter();
        var (giver, receiver, _) = SetupGiveScenario("give1");
        var item = GameObject.Create("apple", isItem: true);
        ObjectRegistry.AddObject(item);
        Assert.True(item.MoveTo(giver));
        var cmd = new GiveCommand();
        var args = cmd.Parser!.ParseArgs(new[] { "apple", "receiver" });
        cmd.Run(giver, args);
        Assert.Equal(receiver.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item.Location).ObjectId);
        Assert.Contains(item.Id, receiver.ContentsSnapshot);
        Assert.DoesNotContain(item.Id, giver.ContentsSnapshot);
        Assert.Contains(giver.PeekMessages(), m => m.Contains("give apple to receiver", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(receiver.PeekMessages(), m => m.Contains("giver gives you apple", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GiveAll()
    {
        using var env = GlobalTestEnv.Enter();
        var (giver, receiver, _) = SetupGiveScenario("give2");
        var i1 = GameObject.Create("apple", isItem: true); ObjectRegistry.AddObject(i1); i1.MoveTo(giver);
        var i2 = GameObject.Create("orange", isItem: true); ObjectRegistry.AddObject(i2); i2.MoveTo(giver);
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "all", "receiver" }));
        Assert.Equal(receiver.Id, ((Persistence.Dto.LocationRef.ObjectLocation)i1.Location).ObjectId);
        Assert.Equal(receiver.Id, ((Persistence.Dto.LocationRef.ObjectLocation)i2.Location).ObjectId);
        Assert.Empty(giver.ContentsSnapshot);
    }

    [Fact]
    public void GiveMultipleSameName()
    {
        using var env = GlobalTestEnv.Enter();
        var (giver, receiver, _) = SetupGiveScenario("give3");
        var s1 = GameObject.Create("sword", isItem: true); ObjectRegistry.AddObject(s1); s1.MoveTo(giver);
        var s2 = GameObject.Create("sword", isItem: true); ObjectRegistry.AddObject(s2); s2.MoveTo(giver);
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "swords", "receiver" }));
        Assert.Equal(receiver.Id, ((Persistence.Dto.LocationRef.ObjectLocation)s1.Location).ObjectId);
        Assert.Equal(receiver.Id, ((Persistence.Dto.LocationRef.ObjectLocation)s2.Location).ObjectId);
    }

    private sealed class HookGiveObj : GameObject
    {
        public bool AtPreGiveCalled = false;
        public bool AtGiveCalled = false;
        public (GameObject giver, GameObject receiver) AtPreGiveArgs;
        public (GameObject giver, GameObject receiver) AtGiveArgs;
        public bool AtPreGiveResult = true;
        public HookGiveObj(string name, bool isItem=false){ Id=IdGenerator.GetUniqueId(); Name=name; IsItem=isItem; }
        public override bool AtPreGive(GameObject giver, GameObject receiver){ AtPreGiveCalled=true; AtPreGiveArgs=(giver,receiver); return AtPreGiveResult; }
        public override void AtGive(GameObject giver, GameObject receiver){ AtGiveCalled=true; AtGiveArgs=(giver,receiver); base.AtGive(giver,receiver); }
    }
    [Fact]
    public void GiveHooks()
    {
        using var env = GlobalTestEnv.Enter();
        var (giver, receiver, _) = SetupGiveScenario("give4");
        var item = new HookGiveObj("wand", isItem:true); ObjectRegistry.AddObject(item); item.MoveTo(giver);
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "wand", "receiver" }));
        Assert.Equal(receiver.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item.Location).ObjectId);
        Assert.True(item.AtPreGiveCalled);
        Assert.Equal(giver, item.AtPreGiveArgs.giver);
        Assert.Equal(receiver, item.AtPreGiveArgs.receiver);
        Assert.True(item.AtGiveCalled);
        Assert.Equal(giver, item.AtGiveArgs.giver);
        Assert.Equal(receiver, item.AtGiveArgs.receiver);
    }

    [Fact]
    public void GivePreGiveBlocked()
    {
        using var env = GlobalTestEnv.Enter();
        var (giver, receiver, _) = SetupGiveScenario("give5");
        var item = new HookGiveObj("ring", isItem:true); item.AtPreGiveResult=false; ObjectRegistry.AddObject(item); item.MoveTo(giver);
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "ring", "receiver" }));
        Assert.Equal(giver.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item.Location).ObjectId);
        Assert.Contains(item.Id, giver.ContentsSnapshot);
        Assert.True(item.AtPreGiveCalled);
        Assert.Equal(giver, item.AtPreGiveArgs.giver);
        Assert.Equal(receiver, item.AtPreGiveArgs.receiver);
        Assert.False(item.AtGiveCalled);
    }

    [Fact] public void GiveToSelf()
    {
        using var env = GlobalTestEnv.Enter();
        var (giver, _, _) = SetupGiveScenario("give6");
        var item = GameObject.Create("apple", isItem: true); ObjectRegistry.AddObject(item); item.MoveTo(giver);
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "apple", "giver" }));
        Assert.Equal(giver.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item.Location).ObjectId);
        Assert.Contains(giver.PeekMessages(), m => m.ToLowerInvariant().Contains("already have"));
    }

    [Fact] public void GiveItemNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var (giver, _, _) = SetupGiveScenario("give7");
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "sword", "receiver" }));
        Assert.Contains(giver.PeekMessages(), m => m.ToLowerInvariant().Contains("don't have"));
    }

    [Fact] public void GiveTargetNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var (giver, _, _) = SetupGiveScenario("give8");
        var item = GameObject.Create("apple", isItem: true); ObjectRegistry.AddObject(item); item.MoveTo(giver);
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "apple", "nonexistent" }));
        Assert.Contains(giver.PeekMessages(), m => m.ToLowerInvariant().Contains("could not find"));
        Assert.Equal(giver.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item.Location).ObjectId);
    }

    [Fact] public void GiveMultipleMatches()
    {
        using var env = GlobalTestEnv.Enter();
        var (giver, receiver, node) = SetupGiveScenario("give9");
        var item = GameObject.Create("apple", isItem: true); ObjectRegistry.AddObject(item); item.MoveTo(giver);
        var twin = GameObject.Create("receiver", isPc: true); ObjectRegistry.AddObject(twin); twin.Location = new Persistence.Dto.LocationRef.CoordLocation(node.Coord); node.AddObject(twin); twin.IsConnected = true;
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "apple", "all", "receiver" }));
        Assert.Contains(giver.PeekMessages(), m => m.ToLowerInvariant().Contains("multiple matches"));
        Assert.Equal(giver.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item.Location).ObjectId);
    }

    [Fact] public void GiveWithToPreposition()
    {
        using var env = GlobalTestEnv.Enter();
        var (giver, receiver, _) = SetupGiveScenario("give10");
        var item = GameObject.Create("apple", isItem: true); ObjectRegistry.AddObject(item); item.MoveTo(giver);
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "apple", "to", "receiver" }));
        Assert.Equal(receiver.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item.Location).ObjectId);
    }

    [Fact] public void GiveToSelfFails()
    {
        using var env = GlobalTestEnv.Enter();
        var (giver, _, _) = SetupGiveScenario("give11");
        var item = GameObject.Create("apple", isItem: true); ObjectRegistry.AddObject(item); item.MoveTo(giver);
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "apple", "giver" }));
        Assert.Contains(giver.PeekMessages(), m => m.ToLowerInvariant().Contains("already have"));
        Assert.Equal(giver.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item.Location).ObjectId);
    }

    [Fact] public void GiveToOfflineCharFails()
    {
        using var env = GlobalTestEnv.Enter();
        var (giver, receiver, _) = SetupGiveScenario("give12");
        receiver.IsConnected = false;
        var item = GameObject.Create("apple", isItem: true); ObjectRegistry.AddObject(item); item.MoveTo(giver);
        var cmd = new GiveCommand();
        cmd.Run(giver, cmd.Parser!.ParseArgs(new[] { "apple", "receiver" }));
        var msg = string.Join(" ", giver.PeekMessages()).ToLowerInvariant();
        Assert.True(msg.Contains("could not find") || msg.Contains("offline"), $"expected could not find/offline message, got: {msg}");
        var locId = (item.Location as Persistence.Dto.LocationRef.ObjectLocation)?.ObjectId;
        Assert.Equal(giver.Id, locId);
    }
}
