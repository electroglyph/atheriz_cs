// Port of atheriz/tests/test_move.py:1
// Port of atheriz/tests/test_move_transaction.py:1
// Port of atheriz/tests/test_move_hooks.py:1
// Port of atheriz/tests/test_location_lock.py:1
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMoveTests
{
    private static (Node n1, Node n2, string area) MakeTwoNodes(string? areaName = null)
    {
        var area = areaName ?? $"test_area_{Guid.NewGuid():N}";
        var n1 = new Node(new Coord(area, 0, 0, 0), desc: "Source");
        var n2 = new Node(new Coord(area, 0, 1, 0), desc: "Dest");
        // Add reciprocal links for reverse lookup
        n1.AddLink(new NodeLink("north", new Coord(area, 0, 1, 0), new List<string>{"n"}));
        n2.AddLink(new NodeLink("south", new Coord(area, 0, 0, 0), new List<string>{"s"}));
        // Explicit registration: the constructor does not publish.
        ObjectRegistry.AddObject(n1); ObjectRegistry.AddObject(n2);
        return (n1, n2, area);
    }

    [Fact]
    public void NpcMoveAnnouncements()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2, _) = MakeTwoNodes();
        var mover = GameObject.Create("MoverNPC", isNpc: true);
        ObjectRegistry.AddObject(mover);
        mover.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord);
        n1.AddObject(mover);

        var observer1 = GameObject.Create("Observer1", isPc: true);
        ObjectRegistry.AddObject(observer1);
        observer1.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord);
        n1.AddObject(observer1);

        var observer2 = GameObject.Create("Observer2", isPc: true);
        ObjectRegistry.AddObject(observer2);
        observer2.Location = new Persistence.Dto.LocationRef.CoordLocation(n2.Coord);
        n2.AddObject(observer2);

        observer1.ClearMessages();
        observer2.ClearMessages();

        var ok = mover.MoveTo(n2, toExit: "north");
        Assert.True(ok);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)mover.Location).Coord);

        var msg1 = string.Join(" ", observer1.PeekMessages());
        Assert.Contains("MoverNPC", msg1);
        Assert.True(msg1.Contains("walks") || msg1.Contains("leaves"), $"msg1 was {msg1}");
        Assert.Contains("north", msg1);
        Assert.DoesNotContain("away", msg1);

        var msg2 = string.Join(" ", observer2.PeekMessages());
        Assert.Contains("MoverNPC", msg2);
        Assert.True(msg2.Contains("walks") || msg2.Contains("arrives"), $"msg2 was {msg2}");
        Assert.Contains("south", msg2);
    }

    [Fact]
    public void MoveIntoContainer()
    {
        using var env = GlobalTestEnv.Enter();
        var container = GameObject.Create("Backpack", isItem: true, isContainer: true);
        ObjectRegistry.AddObject(container);
        var item = GameObject.Create("Apple", isItem: true);
        ObjectRegistry.AddObject(item);
        var ok = item.MoveTo(container);
        Assert.True(ok);
        Assert.Equal(container.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item.Location).ObjectId);
        Assert.Contains(item.Id, container.ContentsSnapshot);
        Assert.Contains(item, ObjectRegistry.Get(container.ContentsSnapshot.ToList()));
    }

    [Fact]
    public void MoveBetweenContainers()
    {
        using var env = GlobalTestEnv.Enter();
        var c1 = GameObject.Create("Backpack", isItem: true, isContainer: true); ObjectRegistry.AddObject(c1);
        var c2 = GameObject.Create("Chest", isItem: true, isContainer: true); ObjectRegistry.AddObject(c2);
        var item = GameObject.Create("Apple", isItem: true); ObjectRegistry.AddObject(item);
        item.MoveTo(c1);
        Assert.Equal(c1.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item.Location).ObjectId);
        var ok = item.MoveTo(c2);
        Assert.True(ok);
        Assert.Equal(c2.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item.Location).ObjectId);
        Assert.DoesNotContain(item.Id, c1.ContentsSnapshot);
        Assert.Contains(item.Id, c2.ContentsSnapshot);
    }

    [Fact]
    public void MoveIntoObjectWithLock()
    {
        using var env = GlobalTestEnv.Enter();
        var container = GameObject.Create("LockedBox", isItem: true, isContainer: true); ObjectRegistry.AddObject(container);
        var item = GameObject.Create("Gold", isItem: true); ObjectRegistry.AddObject(item);
        container.AddLock("enter", _ => false);
        var ok = item.MoveTo(container);
        Assert.False(ok);
        Assert.IsType<Persistence.Dto.LocationRef.NullLocation>(item.Location);
    }

    [Fact]
    public void MoveHooks()
    {
        using var env = GlobalTestEnv.Enter();
        var container = GameObject.Create("MagicBox", isItem: true, isContainer: true); ObjectRegistry.AddObject(container);
        var item = GameObject.Create("Wand", isItem: true); ObjectRegistry.AddObject(item);
        var moveRec = new MoveRecorder();
        item.InstallHook("at_pre_move", (Func<GameObject?, string?, bool>)moveRec.RecordPre);
        item.InstallHook("at_post_move", (Action<GameObject?, string?>)moveRec.RecordPost);
        var ok = item.MoveTo(container);
        Assert.True(ok);
        Assert.Equal(1, moveRec.PreCalls);
        Assert.Equal(container, moveRec.LastPre.d);
        Assert.Null(moveRec.LastPre.e);
        // Second with blocking pre_move
        var item2 = GameObject.Create("CursedSword", isItem: true); ObjectRegistry.AddObject(item2);
        item2.InstallHook("at_pre_move", (Func<GameObject?, string?, bool>)new VetoHooks().DenyAll);
        var ok2 = item2.MoveTo(container);
        Assert.False(ok2);
        Assert.IsType<Persistence.Dto.LocationRef.NullLocation>(item2.Location);
    }

    [Fact]
    public void NestedContainers()
    {
        using var env = GlobalTestEnv.Enter();
        var outer = GameObject.Create("LargeChest", isItem: true, isContainer: true); ObjectRegistry.AddObject(outer);
        var inner = GameObject.Create("SmallBox", isItem: true, isContainer: true); ObjectRegistry.AddObject(inner);
        var item = GameObject.Create("Gem", isItem: true); ObjectRegistry.AddObject(item);
        item.MoveTo(inner);
        var ok = inner.MoveTo(outer);
        Assert.True(ok);
        Assert.Equal(outer.Id, ((Persistence.Dto.LocationRef.ObjectLocation)inner.Location).ObjectId);
        Assert.Equal(inner.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item.Location).ObjectId);
        Assert.Contains(inner.Id, outer.ContentsSnapshot);
        Assert.Contains(item.Id, inner.ContentsSnapshot);
    }

    // ----- Transaction tests (test_move_transaction.py) -----

    private static (Node n1, Node n2) MakeTwoSimpleNodes()
    {
        var area = $"test_area_{Guid.NewGuid():N}";
        var n1 = new Node(new Coord(area, 0, 0, 0));
        var n2 = new Node(new Coord(area, 0, 1, 0));
        // Explicit registration: the constructor does not publish.
        ObjectRegistry.AddObject(n1); ObjectRegistry.AddObject(n2);
        // ensure empty
        return (n1, n2);
    }

    [Fact]
    public void Transaction_NodeDestPreFailsDoesNotTriggerLeave()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeTwoSimpleNodes();
        var rec = new OrderRecorder();
        n1.InstallHook("at_pre_object_leave", (Func<GameObject?, string?, bool>)rec.PreLeave);
        n1.InstallHook("at_object_leave", (Action<GameObject?, string?>)rec.Leave);
        n2.InstallHook("at_pre_object_receive", (Func<GameObject?, string?, bool>)rec.VetoReceive);
        n2.InstallHook("at_object_receive", (Action<GameObject?, string?>)rec.Receive);
        var mover = GameObject.Create("Mover", isPc: true); ObjectRegistry.AddObject(mover);
        mover.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord);
        n1.AddObject(mover);
        var ok = mover.MoveTo(n2, toExit: "north", announce: false);
        Assert.False(ok);
        Assert.Equal(new[] {"pre_leave","pre_receive"}, rec.Calls);
        Assert.Equal(n1.Coord, ((Persistence.Dto.LocationRef.CoordLocation)mover.Location).Coord);
        Assert.Contains(mover.Id, n1.ContentsSnapshot);
        Assert.DoesNotContain(mover.Id, n2.ContentsSnapshot);
    }

    [Fact]
    public void Transaction_NodeSuccessOrderIsTransactional()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeTwoSimpleNodes();
        var rec = new OrderRecorder();
        n1.InstallHook("at_pre_object_leave", (Func<GameObject?, string?, bool>)rec.PreLeave);
        n1.InstallHook("at_object_leave", (Action<GameObject?, string?>)rec.Leave);
        n2.InstallHook("at_pre_object_receive", (Func<GameObject?, string?, bool>)rec.PreReceive);
        n2.InstallHook("at_object_receive", (Action<GameObject?, string?>)rec.Receive);
        var mover = GameObject.Create("Mover2", isPc: true); ObjectRegistry.AddObject(mover);
        mover.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord); n1.AddObject(mover);
        var ok = mover.MoveTo(n2, toExit: "north", announce: false);
        Assert.True(ok);
        Assert.Equal(new[] {"pre_leave","pre_receive","leave","receive"}, rec.Calls);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)mover.Location).Coord);
        Assert.DoesNotContain(mover.Id, n1.ContentsSnapshot);
        Assert.Contains(mover.Id, n2.ContentsSnapshot);
    }

    [Fact]
    public void Transaction_NodeSourcePreFailsNoDestPre()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeTwoSimpleNodes();
        var rec = new OrderRecorder();
        n1.InstallHook("at_pre_object_leave", (Func<GameObject?, string?, bool>)rec.VetoLeave);
        n1.InstallHook("at_object_leave", (Action<GameObject?, string?>)rec.Leave);
        n2.InstallHook("at_pre_object_receive", (Func<GameObject?, string?, bool>)rec.PreReceive);
        n2.InstallHook("at_object_receive", (Action<GameObject?, string?>)rec.Receive);
        var mover = GameObject.Create("Mover3", isPc: true); ObjectRegistry.AddObject(mover);
        mover.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord); n1.AddObject(mover);
        var ok = mover.MoveTo(n2, toExit: "north", announce: false);
        Assert.False(ok);
        Assert.Equal(new[] {"pre_leave"}, rec.Calls);
        Assert.Equal(n1.Coord, ((Persistence.Dto.LocationRef.CoordLocation)mover.Location).Coord);
        Assert.Contains(mover.Id, n1.ContentsSnapshot);
    }

    [Fact]
    public void Transaction_NodeSuccessContentsSwap()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeTwoSimpleNodes();
        var mover = GameObject.Create("Mover4", isPc: true); ObjectRegistry.AddObject(mover);
        mover.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord); n1.AddObject(mover);
        Assert.Contains(mover.Id, n1.ContentsSnapshot);
        var ok = mover.MoveTo(n2, announce: false);
        Assert.True(ok);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)mover.Location).Coord);
        Assert.DoesNotContain(mover.Id, n1.ContentsSnapshot);
        Assert.Contains(mover.Id, n2.ContentsSnapshot);
    }

    [Fact]
    public void Transaction_ItemNodeDestPreFails()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeTwoSimpleNodes();
        var rec = new OrderRecorder();
        n1.InstallHook("at_pre_object_leave", (Func<GameObject?, string?, bool>)rec.PreLeave);
        n1.InstallHook("at_object_leave", (Action<GameObject?, string?>)rec.Leave);
        n2.InstallHook("at_pre_object_receive", (Func<GameObject?, string?, bool>)rec.VetoReceive);
        n2.InstallHook("at_object_receive", (Action<GameObject?, string?>)rec.Receive);
        var item = GameObject.Create("Apple", isItem: true); ObjectRegistry.AddObject(item);
        item.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord); n1.AddObject(item);
        var ok = item.MoveTo(n2, announce: false);
        Assert.False(ok);
        Assert.Equal(new[] {"pre_leave","pre_receive"}, rec.Calls);
        Assert.Equal(n1.Coord, ((Persistence.Dto.LocationRef.CoordLocation)item.Location).Coord);
        Assert.Contains(item.Id, n1.ContentsSnapshot);
        Assert.DoesNotContain(item.Id, n2.ContentsSnapshot);
    }

    [Fact]
    public void Transaction_ItemNodeSuccessOrder()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeTwoSimpleNodes();
        var rec = new OrderRecorder();
        n1.InstallHook("at_pre_object_leave", (Func<GameObject?, string?, bool>)rec.PreLeave);
        n1.InstallHook("at_object_leave", (Action<GameObject?, string?>)rec.Leave);
        n2.InstallHook("at_pre_object_receive", (Func<GameObject?, string?, bool>)rec.PreReceive);
        n2.InstallHook("at_object_receive", (Action<GameObject?, string?>)rec.Receive);
        var item = GameObject.Create("Gem", isItem: true); ObjectRegistry.AddObject(item);
        item.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord); n1.AddObject(item);
        var ok = item.MoveTo(n2, announce: false);
        Assert.True(ok);
        Assert.Equal(new[] {"pre_leave","pre_receive","leave","receive"}, rec.Calls);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)item.Location).Coord);
        Assert.DoesNotContain(item.Id, n1.ContentsSnapshot);
        Assert.Contains(item.Id, n2.ContentsSnapshot);
    }

    [Fact]
    public void Transaction_ItemContainerDestPreFailsSimilar()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeTwoSimpleNodes();
        var container = GameObject.Create("Chest", isItem: true, isContainer: true); ObjectRegistry.AddObject(container);
        var rec = new OrderRecorder();
        container.InstallHook("at_pre_object_leave", (Func<GameObject?, string?, bool>)rec.PreLeave);
        container.InstallHook("at_object_leave", (Action<GameObject?, string?>)rec.Leave);
        var item2 = GameObject.Create("Coin", isItem: true); ObjectRegistry.AddObject(item2);
        item2.Location = new Persistence.Dto.LocationRef.ObjectLocation(container.Id);
        container.AddObject(item2);
        // n2 pre_receive fails
        n2.InstallHook("at_pre_object_receive", (Func<GameObject?, string?, bool>)rec.VetoReceive);
        n2.InstallHook("at_object_receive", (Action<GameObject?, string?>)rec.Receive);
        var ok = item2.MoveTo(n2, announce: false);
        Assert.False(ok);
        Assert.Equal(container.Id, ((Persistence.Dto.LocationRef.ObjectLocation)item2.Location).ObjectId);
        Assert.Contains(item2.Id, container.ContentsSnapshot);
        Assert.DoesNotContain(item2.Id, n2.ContentsSnapshot);
        Assert.DoesNotContain("receive", rec.Calls);
    }

    [Fact]
    public void Transaction_ItemContainerSuccessOrderSimilar()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeTwoSimpleNodes();
        var rec = new OrderRecorder();
        n1.InstallHook("at_pre_object_leave", (Func<GameObject?, string?, bool>)rec.PreLeave);
        n1.InstallHook("at_object_leave", (Action<GameObject?, string?>)rec.Leave);
        n2.InstallHook("at_pre_object_receive", (Func<GameObject?, string?, bool>)rec.PreReceive);
        n2.InstallHook("at_object_receive", (Action<GameObject?, string?>)rec.Receive);
        var item = GameObject.Create("Potion", isItem: true, isContainer: true); ObjectRegistry.AddObject(item);
        item.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord); n1.AddObject(item);
        var ok = item.MoveTo(n2, announce: false);
        Assert.True(ok);
        Assert.Equal(new[] {"pre_leave","pre_receive","leave","receive"}, rec.Calls);
        Assert.Equal(n2.Coord, ((Persistence.Dto.LocationRef.CoordLocation)item.Location).Coord);
    }

    // ----- Hook tests (test_move_hooks.py) -----

    [Fact]
    public void NodeToNodeMoveFiresEnterLeaveHooks()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeTwoSimpleNodes();
        var rec = new OrderRecorder();
        n1.InstallHook("at_pre_object_leave", (Func<GameObject?, string?, bool>)rec.PreLeave);
        n1.InstallHook("at_object_leave", (Action<GameObject?, string?>)rec.Leave);
        n2.InstallHook("at_pre_object_receive", (Func<GameObject?, string?, bool>)rec.PreReceive);
        n2.InstallHook("at_object_receive", (Action<GameObject?, string?>)rec.Receive);
        var obj = GameObject.Create("wanderer", isPc: true); ObjectRegistry.AddObject(obj);
        obj.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord); n1.AddObject(obj);
        var ok = obj.MoveTo(n2, announce: false);
        Assert.True(ok);
        Assert.Equal(new[] {"pre_leave","pre_receive","leave","receive"}, rec.Calls);
    }

    // Recording hook host (replaces the removed At*Override seam): [Before]
    // hooks record advisory pres, [After] hooks record posts, [Replace] hooks
    // record + veto. Lambdas cannot carry attributes, hence real methods.
    private sealed class OrderRecorder
    {
        public readonly List<string> Calls = new();
        [Before] public bool PreLeave(GameObject? d, string? e) { Calls.Add("pre_leave"); return true; }
        [After] public void Leave(GameObject? d, string? e) => Calls.Add("leave");
        [Before] public bool PreReceive(GameObject? s, string? e) { Calls.Add("pre_receive"); return true; }
        [After] public void Receive(GameObject? s, string? e) => Calls.Add("receive");
        [Replace] public bool VetoLeave(GameObject? d, string? e) { Calls.Add("pre_leave"); return false; }
        [Replace] public bool VetoReceive(GameObject? s, string? e) { Calls.Add("pre_receive"); return false; }
    }

    private sealed class MoveRecorder
    {
        public int PreCalls;
        public (GameObject? d, string? e) LastPre = (null, null);
        public int PostCalls;
        [Before] public bool RecordPre(GameObject? d, string? e) { PreCalls++; LastPre = (d, e); return true; }
        [After] public void RecordPost(GameObject? d, string? e) => PostCalls++;
    }

    // [Replace]-attributed veto hooks (replaces the removed At*Override seam;
    // lambdas cannot carry attributes, so real methods provide the marker).
    private sealed class VetoHooks
    {
        [Replace]
        public bool DenyAll(GameObject? a, string? b) => false;
    }

    [Fact]
    public void PreLeaveFalseAbortsNodeMove()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeTwoSimpleNodes();
        n1.InstallHook("at_pre_object_leave", (Func<GameObject?, string?, bool>)new VetoHooks().DenyAll);
        var obj = GameObject.Create("stuck", isPc: true); ObjectRegistry.AddObject(obj);
        obj.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord); n1.AddObject(obj);
        var ok = obj.MoveTo(n2, announce: false);
        Assert.False(ok);
        Assert.Equal(n1.Coord, ((Persistence.Dto.LocationRef.CoordLocation)obj.Location).Coord);
    }

    [Fact]
    public void MoveToHonorsContainerMoveRefusal()
    {
        using var env = GlobalTestEnv.Enter();
        var (n1, n2) = MakeTwoSimpleNodes();
        var pack = GameObject.Create("pack", isContainer: true); ObjectRegistry.AddObject(pack);
        pack.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord); n1.AddObject(pack);
        var item = GameObject.Create("ball", isItem: true); ObjectRegistry.AddObject(item);
        item.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord); n1.AddObject(item);
        n1.InstallHook("at_pre_object_leave", (Func<GameObject?, string?, bool>)new VetoHooks().DenyAll);
        var ok = item.MoveTo(pack);
        Assert.False(ok);
        Assert.Equal(n1.Coord, ((Persistence.Dto.LocationRef.CoordLocation)item.Location).Coord);
    }

    // ----- Location lock test (real path: a worker MoveTo blocks while this
    // thread holds the mover's write lock, proving MoveTo acquires it) -----
    [Fact]
    public void RoomMoveLocksLocationWhenTracking()
    {
        using var env = GlobalTestEnv.Enter();
        var area = $"test_area_{Guid.NewGuid():N}";
        var source = new Node(new Coord(area, 0,0,0), desc:"Source");
        var dest = new Node(new Coord(area, 1,0,0), desc:"Destination");
        var obj = GameObject.Create("Mover");
        ObjectRegistry.AddObject(obj);
        // initial move to source without tracking
        obj.Location = new Persistence.Dto.LocationRef.CoordLocation(source.Coord);
        source.AddObject(obj);
        _ = obj.MoveTo(dest, announce: false); // ensure obj is at source (tautology removed — move may succeed)
        // Reset to source
        obj.Location = new Persistence.Dto.LocationRef.CoordLocation(source.Coord);
        source.AddObject(obj);
        obj.SyncRoot.EnterWriteLock();
        var task = Task.Run(() => obj.MoveTo(dest, announce: false));
        try
        {
            Assert.False(task.Wait(TimeSpan.FromMilliseconds(300)), "MoveTo completed without acquiring the mover's lock");
        }
        finally { obj.SyncRoot.ExitWriteLock(); }
        Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "MoveTo did not finish after the lock was released");
        Assert.True(task.Result);
    }
}
