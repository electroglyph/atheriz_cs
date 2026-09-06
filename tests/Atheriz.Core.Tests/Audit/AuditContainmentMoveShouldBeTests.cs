using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Audit;

// Should-be tests for audit §1 A2/A4 and §2 B3/B11-B13/B15/B16/B18/B19 (+B1 Put).
// Each test asserts the behavior the engine SHOULD have; it FAILS while the
// audited bug is present and PASSES once fixed. Production code is untouched.
[Collection("Ported")]
public class AuditContainmentMoveShouldBeTests
{
    private static void SetId(GameObject o, int id)
    {
        var f = typeof(GameObject).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f);
        f!.SetValue(o, id);
    }

    private static int MsgLogCount(GameObject o)
    {
        var f = typeof(GameObject).GetField("_msgLog", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f);
        var list = (System.Collections.Generic.IReadOnlyCollection<string>)f!.GetValue(o)!;
        return list.Count;
    }

    private static void RegisterAll(params GameObject[] objs)
    {
        foreach (var o in objs)
            if (ObjectRegistry.Get(o.Id).Count == 0)
                ObjectRegistry.AddObject(o);
    }

    // --- A2: containment bookkeeping ---

    [Fact]
    public void RemoveObject_ClearsMoversLocation()
    {
        // audit A2: RemoveObject only drops the id from _contents; obj.Location
        // keeps pointing at the old container (stale ObjectLocation leak).
        ObjectRegistry.ClearAll();
        try
        {
            var bag = GameObject.Create("bag", isContainer: true);
            var coin = GameObject.Create("coin");
            RegisterAll(bag, coin);
            bag.AddObject(coin);
            Assert.IsType<LocationRef.ObjectLocation>(coin.Location);
            bag.RemoveObject(coin);
            Assert.DoesNotContain(coin.Id, bag.ContentsSnapshot);
            Assert.IsType<LocationRef.NullLocation>(coin.Location);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Node_AddObject_SetsCoordLocation()
    {
        // audit A2: Node.AddObject/AddObjects never assigned obj.Location (only
        // MoveTo wrote CoordLocation). Membership in a node implies the node's
        // coord, mirroring MoveTo.
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("limbo", 5, 5, 0));
            var coin = GameObject.Create("coin");
            RegisterAll(node, coin);
            node.AddObject(coin);
            Assert.Contains(coin.Id, node.ContentsSnapshot);
            var loc = Assert.IsType<LocationRef.CoordLocation>(coin.Location);
            Assert.Equal(node.Coord, loc.Coord);
            node.RemoveObject(coin);
            Assert.DoesNotContain(coin.Id, node.ContentsSnapshot);
            Assert.IsType<LocationRef.NullLocation>(coin.Location);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MoveTo_IntoOwnDescendant_WideContainer_IsDenied()
    {
        // audit A4: the IsContainer BFS cycle guard is capped at 100 iterations
        // over nondeterministic HashSet order, so wide containers can pass a
        // cycle. The location chain is severed here so only the BFS can catch it.
        ObjectRegistry.ClearAll();
        try
        {
            var mover = GameObject.Create("mover", isContainer: true);
            ObjectRegistry.AddObject(mover);
            GameObject? first = null;
            for (int i = 0; i < 150; i++)
            {
                var kid = GameObject.Create($"kid{i}");
                ObjectRegistry.AddObject(kid);
                Assert.True(kid.MoveTo(mover));
                first ??= kid;
            }
            Assert.NotNull(first);
            first!.Location = LocationRef.NullLocation.Instance;
            Assert.False(mover.MoveTo(first));
            Assert.IsType<LocationRef.NullLocation>(mover.Location);
            Assert.DoesNotContain(mover.Id, first.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MoveTo_Success_KeepsContentsAndLocationConsistent()
    {
        // Pin: after a successful move, contents and Location agree.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var coin = GameObject.Create("coin");
            RegisterAll(room, coin);
            Assert.True(coin.MoveTo(room));
            Assert.Contains(coin.Id, room.ContentsSnapshot);
            var loc = Assert.IsType<LocationRef.ObjectLocation>(coin.Location);
            Assert.Equal(room.Id, loc.ObjectId);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- A4: MoveTo hook gaps ---

    [Fact]
    public void MoveTo_NullDestination_VanishesWithoutLeaveVeto()
    {
        // audit A4, corrected for Python parity (base_obj.py:1155-1163: the
        // None path calls loc.remove_object + at_post_move only; there is no
        // leave veto on vanish, only the at_pre_move exit-access gate).
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var item = GameObject.Create("item");
            RegisterAll(room, item);
            Assert.True(item.MoveTo(room));
            room.AtPreObjectLeaveOverride = (dest, exit) => false;
            Assert.True(item.MoveTo(null));
            Assert.DoesNotContain(item.Id, room.ContentsSnapshot);
            Assert.IsType<LocationRef.NullLocation>(item.Location);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MoveTo_ContainerToContainer_IgnoresReceiveVeto()
    {
        // audit A4, corrected for Python parity (base_obj.py:1238-1259: leave/
        // receive hooks fire only when old/new loc is_node). Container moves
        // consult no object hooks, only the at_pre_move gate.
        ObjectRegistry.ClearAll();
        try
        {
            var src = GameObject.Create("src", isContainer: true);
            var dst = GameObject.Create("dst", isContainer: true);
            var item = GameObject.Create("item");
            RegisterAll(src, dst, item);
            Assert.True(item.MoveTo(src));
            dst.AtPreObjectReceiveOverride = (s, e) => false;
            Assert.True(item.MoveTo(dst));
            Assert.DoesNotContain(item.Id, src.ContentsSnapshot);
            Assert.Contains(item.Id, dst.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MoveTo_ContainerToContainer_IgnoresLeaveVeto()
    {
        // Same Python parity as above (base_obj.py:1238-1259).
        ObjectRegistry.ClearAll();
        try
        {
            var src = GameObject.Create("src", isContainer: true);
            var dst = GameObject.Create("dst", isContainer: true);
            var item = GameObject.Create("item");
            RegisterAll(src, dst, item);
            Assert.True(item.MoveTo(src));
            src.AtPreObjectLeaveOverride = (d, e) => false;
            Assert.True(item.MoveTo(dst));
            Assert.DoesNotContain(item.Id, src.ContentsSnapshot);
            Assert.Contains(item.Id, dst.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MoveTo_DeletedMover_IsAllowed_LikePython()
    {
        // audit A4, corrected for Python parity (base_obj.py:1233: only
        // destination.is_deleted is checked). A deleted mover can move.
        ObjectRegistry.ClearAll();
        try
        {
            var dst = GameObject.Create("dst", isContainer: true);
            var item = GameObject.Create("item");
            RegisterAll(dst, item);
            Assert.NotNull(item.Delete(null, recursive: false));
            Assert.True(item.IsDeleted);
            Assert.True(item.MoveTo(dst));
            Assert.Contains(item.Id, dst.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- B1: PutCommand live path ---

    [Fact]
    public void Put_RealDispatch_PutsObjectInContainer()
    {
        // audit B1: PutCommand.Run only reflects over Args/Object/Destination
        // props that ParsedArgs lacks, so live `put x in y` always prints help.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var puppet = GameObject.Create("hero");
            var bag = GameObject.Create("bag", isContainer: true);
            var coin = GameObject.Create("coin");
            RegisterAll(room, puppet, bag, coin);
            Assert.True(puppet.MoveTo(room));
            Assert.True(bag.MoveTo(room));
            Assert.True(coin.MoveTo(puppet));
            puppet.ClearMessages();
            var cmd = new PutCommand();
            var (func, caller, args) = cmd.Execute(puppet, "coin in bag");
            Assert.NotNull(func);
            func!(caller!, args);
            Assert.Contains(coin.Id, bag.ContentsSnapshot);
            Assert.DoesNotContain(coin.Id, puppet.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- B3: GiveCommand inventory bypass ---

    [Fact]
    public void Give_RoomObject_MovesToTarget()
    {
        // audit B3, corrected for Python parity (give.py:162
        // objs_to_give = caller.search(obj_name), and search covers the room
        // the caller stands in): giving a room object works, like Python.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var giver = GameObject.Create("giver", isPc: true);
            var box = GameObject.Create("box", isContainer: true);
            var coin = GameObject.Create("coin");
            RegisterAll(room, giver, box, coin);
            Assert.True(giver.MoveTo(room));
            Assert.True(box.MoveTo(room));
            Assert.True(coin.MoveTo(room));
            giver.ClearMessages();
            var cmd = new GiveCommand();
            var (func, caller, args) = cmd.Execute(giver, "coin to box");
            Assert.NotNull(func);
            func!(caller!, args);
            Assert.Contains("You give coin to box.", giver.PeekMessages());
            Assert.Contains(coin.Id, box.ContentsSnapshot);
            Assert.DoesNotContain(coin.Id, room.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- B11: hear veto/addressing/desc-only ---

    [Fact]
    public void EmitSound_DescOnly_ReachesNobody()
    {
        // audit B11, corrected for Python parity (base_obj.py:1893-1894: early
        // return on empty soundMsg). Desc-only sounds never propagate.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var emitter = GameObject.Create("emitter");
            var hearer = GameObject.Create("hearer", isPc: true);
            RegisterAll(room, emitter, hearer);
            Assert.True(emitter.MoveTo(room));
            Assert.True(hearer.MoveTo(room));
            hearer.ClearMessages();
            emitter.AtEmitSound("a loud crash", "", 60.0, false);
            Assert.Empty(hearer.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Hear_SameRoom_IsDirect_NotDirectional()
    {
        // Pin: same-room hearing has no direction suffix.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var emitter = GameObject.Create("emitter");
            var hearer = GameObject.Create("hearer", isPc: true);
            RegisterAll(room, emitter, hearer);
            Assert.True(emitter.MoveTo(room));
            Assert.True(hearer.MoveTo(room));
            hearer.ClearMessages();
            hearer.AtHear(emitter, "says", "hello", 70.0, true);
            var all = string.Join("\n", hearer.PeekMessages());
            Assert.Contains("sayshello", all);
            Assert.DoesNotContain("to the", all);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    private sealed class VetoNode : Node
    {
        public VetoNode(Coord c) : base(c) { }
        public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreHear(
            GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
            => (false, emitter, soundDesc, soundMsg, loudness, isSay);
    }

    [Fact]
    public void Node_AtHear_Vetoed_ReturnsAttenuated_WithoutLocalDelivery()
    {
        // audit B11, corrected for Python parity (nodes.py:325-326,335): a
        // vetoed node returns loudness-attenuation (still positive, so BFS may
        // forward) but delivers to nothing locally.
        ObjectRegistry.ClearAll();
        try
        {
            var node = new VetoNode(new Coord("limbo", 1, 0, 0));
            var emitter = GameObject.Create("emitter");
            var hearer = GameObject.Create("hearer", isPc: true);
            RegisterAll(node, emitter, hearer);
            Assert.True(hearer.MoveTo(node));
            hearer.ClearMessages();
            double ret = node.AtHear(emitter, "boom", "loud!", 60.0, false);
            Assert.Equal(50.0, ret);
            Assert.Empty(hearer.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- B12: Node.MsgContents double-parse ---

    [Fact]
    public void MsgContents_NodeAndContainerPaths_AgreeOnSingleParse()
    {
        // audit B12: Node.MsgContents parses then receiver.Msg parses AGAIN,
        // while GameObject.MsgContents appends directly. Both paths must deliver
        // the identical single-parse result for identical input.
        ObjectRegistry.ClearAll();
        try
        {
            var actor = GameObject.Create("Ann");
            var cRoom = GameObject.Create("croom", isContainer: true);
            var bob1 = GameObject.Create("Bob");
            var node = new Node(new Coord("limbo", 2, 0, 0));
            var bob2 = GameObject.Create("Bob");
            RegisterAll(actor, cRoom, bob1, node, bob2);
            Assert.True(bob1.MoveTo(cRoom));
            Assert.True(bob2.MoveTo(node));
            var mapping1 = new Dictionary<string, object?> { ["k"] = "$you" };
            var mapping2 = new Dictionary<string, object?> { ["k"] = "$you" };
            cRoom.MsgContents("Say {k}.", actor, mapping1);
            node.MsgContents("Say {k}.", fromObj: actor, mapping: mapping2);
            var m1 = Assert.Single(bob1.PeekMessages());
            var m2 = Assert.Single(bob2.PeekMessages());
            Assert.Equal(m1, m2);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- B13: door dirty asymmetry ---

    [Fact]
    public void Door_ForcePaths_MarkDoorsDirty_SameAsTryPaths()
    {
        // audit B13: ForceOpen/ForceClose never call MarkNodeDoorsModified, so an
        // incremental save (force:false) silently drops the forced state.
        using var env = GlobalTestEnv.Enter();
        var coordA = new Coord("limbo", 0, 0, 0);
        var coordB = new Coord("limbo", 2, 0, 0);
        try
        {
            // Control: TryOpen marks dirty -> incremental save persists.
            var nh1 = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh1);
            var opener = GameObject.Create("opener");
            var d1 = Door.Create(coordA, "east", coordB, "west", closed: true);
            nh1.AddDoor(d1);
            nh1.Save(force: true);
            Assert.True(d1.TryOpen(opener));
            nh1.Save(force: false);
            using (var db = new AtherizDbContext(env.TempPath))
            {
                var probe = new NodeHandler(autoLoad: false);
                probe.Load(db);
                var doors = probe.GetDoors(coordA);
                Assert.NotNull(doors);
                Assert.Contains(doors!.Values, d => !d.Closed);
            }

            // Experiment: ForceOpen must behave the same.
            NodeHandler.SetCurrent(null);
            var nh2 = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh2);
            var d2 = Door.Create(coordA, "east", coordB, "west", closed: true);
            nh2.AddDoor(d2);
            nh2.Save(force: true);
            Assert.True(d2.ForceOpen());
            Assert.False(d2.Closed);
            nh2.Save(force: false);
            using (var db = new AtherizDbContext(env.TempPath))
            {
                var probe = new NodeHandler(autoLoad: false);
                probe.Load(db);
                var doors = probe.GetDoors(coordA);
                Assert.NotNull(doors);
                Assert.Contains(doors!.Values, d => !d.Closed);
            }
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void Door_TryOpenClose_Roundtrip_State()
    {
        // Pin: documents the Python-faithful already_open->true /
        // already_closed->false Try convention (base_door.py:125-138,181-188).
        var d = Door.Create(new Coord("limbo", 0, 0, 0), "east", new Coord("limbo", 2, 0, 0), "west", closed: true);
        var caller = GameObject.Create("opener");
        Assert.True(d.TryOpen(caller));
        Assert.False(d.Closed);
        Assert.True(d.TryOpen(caller));
        Assert.True(d.TryClose(caller));
        Assert.True(d.Closed);
        Assert.False(d.TryClose(caller));
    }

    // --- B15: session/puppet identity ---

    [Fact]
    public void Session_AccountSwap_UpdatesAccountId()
    {
        // audit B15: AccountId is set only in the ctor; later Account swaps
        // leave it stale.
        var a1 = new Account();
        SetId(a1, 501);
        var a2 = new Account();
        SetId(a2, 502);
        var s = new Session(connection: null, account: a1);
        Assert.Equal(501, s.AccountId);
        s.Account = a2;
        Assert.Equal(502, s.AccountId);
    }

    [Fact]
    public void Puppet_SameIdDistinctInstance_RefusedAsSelf()
    {
        // audit B15: the self-puppet check uses reference ==, so a distinct
        // instance with the same Id (e.g. after a reload rewire) bypasses it.
        ObjectRegistry.ClearAll();
        try
        {
            var caller = GameObject.Create("hero", isPc: true, privilege: Privilege.Admin);
            var twin = GameObject.Create("hero-twin");
            SetId(twin, caller.Id);
            RegisterAll(caller, twin);
            var session = new Session();
            Assert.False(caller.Puppet(session, twin));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- B16: Delete counts ---

    [Fact]
    public void Delete_Recursive_ReturnsTrueCount()
    {
        // audit B16: recursive Delete always returns (1, ops) regardless of
        // how many objects were collected.
        ObjectRegistry.ClearAll();
        try
        {
            var parent = GameObject.Create("parent", isContainer: true);
            var k1 = GameObject.Create("kid1");
            var k2 = GameObject.Create("kid2");
            RegisterAll(parent, k1, k2);
            Assert.True(k1.MoveTo(parent));
            Assert.True(k2.MoveTo(parent));
            var res = parent.Delete(null, recursive: true);
            Assert.NotNull(res);
            Assert.Equal(3, res!.Value.count);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Node_Delete_Recursive_ReturnsTrueCount()
    {
        // audit B16: same miscount in Node.Delete.
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("limbo", 6, 0, 0));
            var item = GameObject.Create("item");
            RegisterAll(node, item);
            Assert.True(item.MoveTo(node));
            var res = node.Delete(null, recursive: true);
            Assert.NotNull(res);
            Assert.Equal(2, res!.Value.count);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Delete_NonRecursive_CountsRecursivelyDeletedChildren()
    {
        // audit B16: the non-recursive path returns (1, ops) always, even when
        // un-movable children are recursively deleted and appended to ops.
        ObjectRegistry.ClearAll();
        try
        {
            var parent = GameObject.Create("parent", isContainer: true);
            var stuck = GameObject.Create("stuck");
            RegisterAll(parent, stuck);
            Assert.True(stuck.MoveTo(parent));
            stuck.AtPreMoveOverride = (d, e) => false;
            var res = parent.Delete(null, recursive: false);
            Assert.NotNull(res);
            Assert.Equal(2, res!.Value.count);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- B18: Node.Name no-op ---

    [Fact]
    public void Node_Name_IsCoordString_SetterIgnored()
    {
        // audit B18, corrected for Python parity (nodes.py:616-619: name is a
        // read-only property returning str(coord); rooms are coord-identified,
        // get_display_name is "" for non-builders). The C# no-op setter mirrors
        // the read-only property for the base-class contract.
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("limbo", 3, 0, 0));
            if (ObjectRegistry.Get(node.Id).Count == 0) ObjectRegistry.AddObject(node);
            Assert.Equal(node.Coord.ToString(), node.Name);
            node.Name = "Tavern";
            Assert.Equal(node.Coord.ToString(), node.Name);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- B19: channel history / msglog ---

    [Fact]
    public void Channel_History_ContainsSenderAndTimestamp()
    {
        // audit B19, corrected for Python parity (base_channel.py): history
        // keeps (timestamp, sender, message) entries; History projects the raw
        // messages while GetHistory re-formats on replay, so replay matches
        // what live listeners received.
        ObjectRegistry.ClearAll();
        try
        {
            var ch = new Channel();
            var sender = GameObject.Create("Alice");
            RegisterAll(sender);
            ch.Msg("hello", sender);
            Assert.Contains("hello", ch.History);
            var h = ch.GetHistory(10);
            Assert.Contains("hello", h);
            Assert.Contains("Alice", h);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void GameObject_MsgLog_IsBounded()
    {
        // audit B19: _msgLog grows without bound (Channel caps history at 50);
        // long-lived NPCs leak memory. Bound is 200 (exam dumps ~60 lines, so
        // whole multi-screen outputs must survive in PeekMessages).
        var o = GameObject.Create("npc");
        try
        {
            for (int i = 0; i < 300; i++) o.Msg($"m{i}");
            Assert.True(MsgLogCount(o) <= 200, $"msg log should be bounded, has {MsgLogCount(o)} entries");
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
