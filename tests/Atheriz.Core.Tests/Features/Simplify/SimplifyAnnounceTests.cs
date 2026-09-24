using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core;

namespace Atheriz.Core.Tests.Features.Simplify;

// Merged from ContainmentAnnounceContractTests.cs
[Collection("Ported")]
public sealed class ContainmentAnnounceContractTests
{
    private static (Node room, GameObject actor, GameObject watcher) SetupRoom(string area)
    {
        var room = new Node(new Coord(area, 0, 0, 0));
        ObjectRegistry.AddObject(room);
        var actor = GameObject.Create("Actor", isPc: true);
        var watcher = GameObject.Create("Watcher", isPc: true);
        ObjectRegistry.AddObject(actor);
        ObjectRegistry.AddObject(watcher);
        var loc = new LocationRef.CoordLocation(room.Coord);
        actor.Location = loc;
        watcher.Location = loc;
        room.AddObject(actor);
        room.AddObject(watcher);
        actor.ClearMessages();
        watcher.ClearMessages();
        return (room, actor, watcher);
    }

    [Fact]
    public void EmitToContents_EmptyText_DeliversEmpty_ToBothContracts()
    {
        using var env = GlobalTestEnv.Enter();
        var host = GameObject.Create("Host");
        var receiver = GameObject.Create("Recv");
        ObjectRegistry.AddObject(host);
        ObjectRegistry.AddObject(receiver);
        receiver.ClearMessages();
        ContentUtils.EmitToContents([receiver], host, "", null, null, null, null, false, nodeSemantics: true);
        ContentUtils.EmitToContents([receiver], host, "", null, null, null, null, false, nodeSemantics: false);
        Assert.Equal(2, receiver.PeekMessages().Count(m => m == ""));
    }

    [Fact]
    public void EmitToContents_UnknownFunc_NodeFallsBack_BaseRethrowsWhenAsked()
    {
        using var env = GlobalTestEnv.Enter();
        var host = GameObject.Create("Host");
        var receiver = GameObject.Create("Recv");
        ObjectRegistry.AddObject(host);
        ObjectRegistry.AddObject(receiver);
        receiver.ClearMessages();
        // Node delivery never throws — raw text is delivered instead.
        ContentUtils.EmitToContents([receiver], host, "$foo(bar)", null, null, null, null, true, nodeSemantics: true);
        Assert.Contains("$foo(bar)", receiver.PeekMessages());
        // Base delivery rethrows the ParsingError when raiseErrors is set...
        Assert.Throws<FuncParser.ParsingError>(() =>
            ContentUtils.EmitToContents([receiver], host, "$foo(bar)", null, null, null, null, true, nodeSemantics: false));
        // ...and falls back to raw text otherwise.
        ContentUtils.EmitToContents([receiver], host, "$foo(bar)", null, null, null, null, false, nodeSemantics: false);
        Assert.Equal(2, receiver.PeekMessages().Count(m => m == "$foo(bar)"));
    }

    [Fact]
    public void EmitToContents_OverlongText_NodeFallsBack_BaseRethrowsWhenAsked()
    {
        using var env = GlobalTestEnv.Enter();
        var host = GameObject.Create("Host");
        var receiver = GameObject.Create("Recv");
        ObjectRegistry.AddObject(host);
        ObjectRegistry.AddObject(receiver);
        receiver.ClearMessages();
        string huge = new('x', FuncParser.MaxMessageSize + 1);
        ContentUtils.EmitToContents([receiver], host, huge, null, null, null, null, true, nodeSemantics: true);
        Assert.Contains(huge, receiver.PeekMessages());
        Assert.Throws<FuncParser.ParsingError>(() =>
            ContentUtils.EmitToContents([receiver], host, huge, null, null, null, null, true, nodeSemantics: false));
    }

    [Fact]
    public void GetCommand_OnNodeLocation_AnnouncesToRoom_ExcludingActor()
    {
        using var env = GlobalTestEnv.Enter();
        var (room, actor, watcher) = SetupRoom("announce1");
        var item = GameObject.Create("widget", isItem: true);
        ObjectRegistry.AddObject(item);
        item.Location = new LocationRef.CoordLocation(room.Coord);
        room.AddObject(item);
        var cmd = new GetCommand();
        cmd.Run(actor, cmd.Parser!.ParseArgs(["widget"]));
        Assert.Contains(actor.PeekMessages(), m => m.Contains("You picked up"));
        Assert.Contains(watcher.PeekMessages(), m => m.Contains("picked up"));
        Assert.DoesNotContain(actor.PeekMessages(), m => m.Contains("Actor picked up"));
    }
}

// Merged from DoorAnnounceTargetTests.cs
// Door announces: every site builds its own mapping instance carrying the
// caller under the shared key, so the parser substitutes the actor's name.
[Collection("Ported")]
public class DoorAnnounceTargetTests
{
    [Fact]
    public void TryLock_Announce_SubstitutesCallerName()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var caller = GameObject.Create("caller");
            ObjectRegistry.AddObject(caller);
            var receiver = GameObject.Create("recv");
            ObjectRegistry.AddObject(receiver);
            room.AddObject(caller);
            room.AddObject(receiver);
            var door = new Door(new Coord("limbo", 0, 0, 0), new Coord("limbo", 0, 1, 0), "north", "south");

            Assert.True(door.TryLock(caller));

            var heard = string.Join("\n", receiver.PeekMessages());
            Assert.Contains("caller", heard);
            Assert.Contains("lock", heard);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // already_open announces loc-only like already_closed: the far room hears
    // neither idempotent announce.
    [Fact]
    public void AlreadyOpen_Announces_LocOnly()
    {
        ObjectRegistry.ClearAll();
        var priorCurrent = NodeHandler.GetCurrent();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh);
            const string area = "DoorLocPin";
            var areaObj = new NodeArea(area);
            var grid = new NodeGrid(area, 0);
            var n1 = new Node(new Coord(area, 0, 0, 0));
            var n2 = new Node(new Coord(area, 0, 2, 0));
            grid.AddNode(n1);
            grid.AddNode(n2);
            areaObj.AddGrid(grid);
            nh.AddArea(areaObj);
            var pa = GameObject.Create("pa");
            ObjectRegistry.AddObject(pa);
            var pb = GameObject.Create("pb");
            ObjectRegistry.AddObject(pb);
            n1.AddObject(pa);
            n2.AddObject(pb);
            var caller = GameObject.Create("caller");
            ObjectRegistry.AddObject(caller);
            n1.AddObject(caller);
            var door = new Door(new Coord(area, 0, 0, 0), new Coord(area, 0, 2, 0), "north", "south");
            pa.ClearMessages(); pb.ClearMessages(); caller.ClearMessages();

            // Control: already_closed on a shut door reaches only the caller's room.
            Assert.True(door.TryClose(caller));
            Assert.Contains("already closed", string.Join("\n", pa.PeekMessages()));
            Assert.Empty(pb.PeekMessages());
            pa.ClearMessages(); pb.ClearMessages();

            // Fixed path: already_open on an open door stays in the caller's room too.
            door.ForceOpen();
            Assert.True(door.TryOpen(caller));
            Assert.Contains("already open", string.Join("\n", pa.PeekMessages()));
            Assert.Empty(pb.PeekMessages());
        }
        finally
        {
            try { NodeHandler.SetCurrent(priorCurrent); } catch { }
            ObjectRegistry.ClearAll();
        }
    }
}

// Merged from SoundPreHookTests.cs
// Shared pre-hook core: AtPreHear and AtPreEmitSound pass the same tuple
// through the same gate, so a vetoed receiver hears nothing while others do.
[Collection("Ported")]
public class SoundPreHookTests
{
    private sealed class VetoHearer : GameObject
    {
        public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreHear(
            GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
            => (false, emitter, soundDesc, soundMsg, loudness, isSay);
    }

    [Fact]
    public void AtEmitSound_VetoedReceiver_HearsNothing()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var emitter = GameObject.Create("em");
            ObjectRegistry.AddObject(emitter);
            var veto = new VetoHearer();
            veto.Id = GameObject.GetNextId();
            veto.Name = "veto";
            veto.IsPc = true;
            ObjectRegistry.AddObject(veto);
            var control = GameObject.Create("ctl", isPc: true);
            ObjectRegistry.AddObject(control);
            emitter.MoveTo(room, force: true, announce: false);
            veto.MoveTo(room, force: true, announce: false);
            control.MoveTo(room, force: true, announce: false);

            emitter.AtEmitSound("boom", "bang", 50.0, false);

            Assert.Empty(veto.PeekMessages());
            Assert.NotEmpty(control.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtHear_HearingNonPc_ReceivesSound()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = new Node(new Coord("f5room", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            var npc = GameObject.Create("f5listener", isNpc: true);
            var emitter = GameObject.Create("f5emitter", isNpc: true);
            ObjectRegistry.AddObject(npc);
            ObjectRegistry.AddObject(emitter);
            Assert.True(npc.MoveTo(room));
            Assert.True(emitter.MoveTo(room));
            Assert.False(npc.IsPc);
            Assert.True(npc.CanHear);
            npc.ClearMessages();

            npc.AtHear(emitter, "a clang", "", 50.0, false);

            Assert.Contains("You hear something", string.Join("\n", npc.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtHear_DeafNonPc_HearsNothing()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = new Node(new Coord("f5droom", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            var npc = GameObject.Create("f5deaf", isNpc: true);
            var emitter = GameObject.Create("f5demitter", isNpc: true);
            ObjectRegistry.AddObject(npc);
            ObjectRegistry.AddObject(emitter);
            Assert.True(npc.MoveTo(room));
            Assert.True(emitter.MoveTo(room));
            npc.CanHear = false;
            npc.ClearMessages();

            npc.AtHear(emitter, "a clang", "", 50.0, false);

            Assert.Empty(npc.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}

// Merged from ChannelHistoryTailTests.cs
// Channel formatting: the shared prefix shape and TakeLast tail selection
// (same tail as Skip(count-n), no off-by-one on the history limit).
[Collection("Ported")]
public class ChannelHistoryTailTests
{
    [Fact]
    public void FormatMessage_SharesPrefixShape()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            ch.Name = "chan";
            var named = ch.FormatMessage(0, "Bob", "hi");
            Assert.StartsWith("(chan) [", named);
            Assert.Contains("Bob: hi", named);
            var anon = ch.FormatMessage(0, "", "hi");
            Assert.EndsWith("] hi", anon);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void GetHistory_ReturnsExactTail()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel(historyLimit: 3);
            ch.Name = "chan";
            var sender = GameObject.Create("Bob");
            ch.Msg("m1", sender);
            ch.Msg("m2", sender);
            ch.Msg("m3", sender);
            ch.Msg("m4", sender);
            ch.Msg("m5", sender);

            Assert.Equal(3, ch.History.Count);
            var tail = ch.GetHistory(2).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, tail.Length);
            Assert.Contains("m4", tail[0]);
            Assert.Contains("m5", tail[1]);
            Assert.Equal("", ch.GetHistory(0));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}

// Merged from ShowLocationGateTests.cs
[Collection("Ported")]
public sealed class ShowLocationGateTests
{
    private static GameObject MakePuppet(Node? room, string desc = "")
    {
        var p = GameObject.Create("Seer", isPc: true, desc: desc);
        ObjectRegistry.AddObject(p);
        if (room is not null)
        {
            p.Location = new LocationRef.CoordLocation(room.Coord);
            room.AddObject(p);
        }
        else
        {
            p.Location = LocationRef.NullLocation.Instance;
        }
        p.ClearMessages();
        return p;
    }

    [Fact]
    public void Look_NullLocationWithDesc_ReportsNowhere()
    {
        using var env = GlobalTestEnv.Enter();
        var p = MakePuppet(null, "A drifter.");
        new LookCommand().Run(p, new LookCommand().Parser!.ParseArgs([]));
        Assert.Contains(p.PeekMessages(), m => m == "You are nowhere.");
        Assert.DoesNotContain(p.PeekMessages(), m => m == "A drifter.");
    }

    [Fact]
    public void Look_NullLocationWithoutDesc_ReportsNowhere()
    {
        using var env = GlobalTestEnv.Enter();
        var p = MakePuppet(null);
        new LookCommand().Run(p, new LookCommand().Parser!.ParseArgs([]));
        Assert.Contains(p.PeekMessages(), m => m == "You are nowhere.");
    }

    [Fact]
    public void Look_ContainerWithoutView_Refuses_BothTails()
    {
        using var env = GlobalTestEnv.Enter();
        var box = GameObject.Create("box", isContainer: true);
        ObjectRegistry.AddObject(box);
        box.AddLock("view", _ => false);
        var p = GameObject.Create("Seer", isPc: true);
        ObjectRegistry.AddObject(p);
        Assert.True(p.MoveTo(box, force: true, announce: false));
        p.ClearMessages();
        new LookCommand().Run(p, new LookCommand().Parser!.ParseArgs([]));
        Assert.Contains(p.PeekMessages(), m => m == "You can't see anything.");
    }

    [Fact]
    public void Look_NounFallback_Resolves_AfterGate()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord("lookgate1", 0, 0, 0));
        ObjectRegistry.AddObject(room);
        room.AddNoun("plaque", "A brass plaque.");
        var p = MakePuppet(room);
        // Direct target move pins the fallback outside SearchWithFallback.
        Assert.Equal("A brass plaque.", room.TryResolveLookTarget("plaque", p));
        Assert.Null(room.TryResolveLookTarget("nope", p));
        new LookCommand().Run(p, new LookCommand().Parser!.ParseArgs(["plaque"]));
        Assert.Contains(p.PeekMessages(), m => m == "A brass plaque.");
    }

    [Fact]
    public void Look_NounFallback_DeniedView_NeverResolves()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord("lookgate2", 0, 0, 0));
        ObjectRegistry.AddObject(room);
        room.AddNoun("plaque", "A brass plaque.");
        room.AddLock("view", _ => false);
        var p = MakePuppet(room);
        new LookCommand().Run(p, new LookCommand().Parser!.ParseArgs(["plaque"]));
        Assert.Contains(p.PeekMessages(), m => m == "No match found for 'plaque'.");
        Assert.DoesNotContain(p.PeekMessages(), m => m == "A brass plaque.");
    }
}

// Merged from SetCommandGateReprTests.cs
[Collection("Ported")]
public sealed class SetCommandGateReprTests
{
    private static GameObject MakeSuperuser(string name = "Root")
    {
        var c = GameObject.Create(name, privilege: Atheriz.Core.Privilege.Admin);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    private static GameObject MakeBuilder(string name = "Bob")
    {
        var c = GameObject.Create(name, privilege: Atheriz.Core.Privilege.Builder);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    [Fact]
    public void Set_LowercaseLocation_AsSuperuser_HitsMoveGate()
    {
        using var env = GlobalTestEnv.Enter();
        var root = MakeSuperuser();
        var target = MakeBuilder("Target");
        // #id refs (established SetTargetById pattern): name search needs a
        // shared connected location, which would only add view-gate noise to
        // these gate/repr pins.
        new SetCommand().Run(root, new SetCommand().Parser!.ParseArgs(["#" + target.Id, "location", "x"]));
        Assert.Contains(root.PeekMessages(), m => m == "'location' cannot be set directly; use move/teleport instead.");
    }

    [Fact]
    public void Set_UppercaseLocation_AsSuperuser_BypassesMoveGate()
    {
        using var env = GlobalTestEnv.Enter();
        var root = MakeSuperuser();
        var target = MakeBuilder("Target");
        // The gate is Ordinal: "LOCATION" is not gated (and not a known
        // property either), so it lands in extras like any junk attribute.
        new SetCommand().Run(root, new SetCommand().Parser!.ParseArgs(["#" + target.Id, "LOCATION", "x"]));
        Assert.Contains(root.PeekMessages(), m => m.Contains("Warning: 'LOCATION' is a new attribute"));
        Assert.True(target.HasExtra("LOCATION"));
    }

    [Fact]
    public void Set_Location_AsBuilder_HitsProtectedGateFirst()
    {
        using var env = GlobalTestEnv.Enter();
        var bob = MakeBuilder();
        var target = MakeBuilder("Target");
        target.PrivilegeLevel = Atheriz.Core.Privilege.Player;
        new SetCommand().Run(bob, new SetCommand().Parser!.ParseArgs(["#" + target.Id, "location", "x"]));
        Assert.Contains(bob.PeekMessages(), m => m == "'location' is protected and cannot be set.");
    }

    [Fact]
    public void Set_ReprCoversNullStringBool()
    {
        using var env = GlobalTestEnv.Enter();
        var root = MakeSuperuser();
        var target = MakeBuilder("Target");
        var cmd = new SetCommand();
        var idRef = "#" + target.Id;
        cmd.Run(root, cmd.Parser!.ParseArgs([idRef, "desc", "None"]));
        Assert.Contains(root.PeekMessages(), m => m == "Set Target.desc = None");
        root.ClearMessages();
        cmd.Run(root, cmd.Parser!.ParseArgs([idRef, "desc", "hello"]));
        Assert.Contains(root.PeekMessages(), m => m == "Set Target.desc = 'hello'");
        root.ClearMessages();
        cmd.Run(root, cmd.Parser!.ParseArgs([idRef, "desc", "True"]));
        Assert.Contains(root.PeekMessages(), m => m == "Set Target.desc = True");
    }

    [Fact]
    public void IsProtected_CaseInsensitive_WhileGateStaysSensitive()
    {
        Assert.True(SetHelper.IsProtected("is_pc"));
        Assert.True(SetHelper.IsProtected("IS_PC"));
        Assert.True(SetHelper.IsProtected("Is_Pc"));
        Assert.False(SetHelper.IsProtected("desc"));
    }
}

// Merged from QuietCloseOrderTests.cs
[Collection("Ported")]
public sealed class QuietCloseOrderTests
{
    private sealed class OtherCaller : IMessageTarget
    {
        public void Msg(string text) { }
    }

    [Fact]
    public void Quit_GameObjectCaller_GoodbyeFirst_ThenClosesConnection()
    {
        using var env = GlobalTestEnv.Enter();
        var puppet = GameObject.Create("Quitter", isPc: true);
        ObjectRegistry.AddObject(puppet);
        var conn = new Atheriz.Core.Tests.TestConnection("quit-conn");
        var sess = new Session(conn);
        puppet.Session = sess;
        sess.Puppet = puppet;
        puppet.ClearMessages();
        new QuitCommand().Run(puppet, null);
        Assert.Contains(puppet.PeekMessages(), m => m == "Goodbye!");
        Assert.True(conn.Closed);
    }

    [Fact]
    public void Quit_RawConnectionCaller_ClosesIt()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new Atheriz.Core.Tests.TestConnection("quit-raw");
        new QuitCommand().Run(conn, null);
        Assert.True(conn.Closed);
    }

    [Fact]
    public void CloseQuietly_UnknownCallerShape_IsSilentNoop()
    {
        using var env = GlobalTestEnv.Enter();
        var ex = Record.Exception(() => ConnectionHelper.CloseQuietly(new OtherCaller()));
        Assert.Null(ex);
    }

    [Fact]
    public void CloseQuietly_SessionShapes_CloseExpectedConnection()
    {
        using var env = GlobalTestEnv.Enter();
        var puppet = GameObject.Create("QuietQuitter", isPc: true);
        ObjectRegistry.AddObject(puppet);
        var conn = new TestConnection("quiet-quit");
        var sess = new Session(conn);
        puppet.Session = sess;
        sess.Puppet = puppet;
        ConnectionHelper.CloseQuietly(puppet);
        Assert.True(conn.Closed);

        var raw = new TestConnection("quiet-raw");
        ConnectionHelper.CloseQuietly(raw);
        Assert.True(raw.Closed);
    }
}

// Merged from AtPostPuppetMapGateTests.cs
// Map gates in AtPostPuppet: the global flag is read once per gate (two
// observations, never hoisted), so disabling either gate suppresses the
// map_enable send while the rest of the login still runs.
[Collection("Ported")]
public class AtPostPuppetMapGateTests
{
    private static TestConnection RunPostPuppet(GameObject obj)
    {
        var conn = new TestConnection("mapgate");
        obj.Session = new Session(conn);
        obj.AtPostPuppet();
        return conn;
    }

    [Fact]
    public void AtPostPuppet_GlobalMapDisabled_SendsNoMapEnable()
    {
        bool prev = AtherizSettings.Global.MapEnabled;
        AtherizSettings.Global.MapEnabled = false;
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var obj = GameObject.Create("pc");
            ObjectRegistry.AddObject(obj);
            room.AddObject(obj);

            var conn = RunPostPuppet(obj);

            Assert.Contains(conn.Sent, s => s.Cmd == "logged_in");
            Assert.DoesNotContain(conn.Sent, s => s.Cmd == "map_enable");
        }
        finally
        {
            AtherizSettings.Global.MapEnabled = prev;
            ObjectRegistry.ClearAll();
        }
    }

    [Fact]
    public void AtPostPuppet_SelfMapDisabled_SendsNoMapEnable()
    {
        // The listener gate still resolves a handler (kept side-effect free
        // with an unloadable instance), but the second gate fails first.
        using var env = GlobalTestEnv.Enter();
        GlobalServices.SetMapHandler(new MapHandler(new AtherizSettings(), autoLoad: false));
        bool prev = AtherizSettings.Global.MapEnabled;
        AtherizSettings.Global.MapEnabled = true;
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var obj = GameObject.Create("pc");
            ObjectRegistry.AddObject(obj);
            obj.MapEnabled = false;
            room.AddObject(obj);

            var conn = RunPostPuppet(obj);

            Assert.Contains(conn.Sent, s => s.Cmd == "logged_in");
            Assert.DoesNotContain(conn.Sent, s => s.Cmd == "map_enable");
        }
        finally
        {
            AtherizSettings.Global.MapEnabled = prev;
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }
}
