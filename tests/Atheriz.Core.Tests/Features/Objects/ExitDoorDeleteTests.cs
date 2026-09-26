// Pins for audit10 findings 18-22: installed exits enforce doors and name the
// direction, node arrivals install exits, PC deletion frees the account slot,
// and a throwing at_delete on a child is a veto (fail-closed) on the
// non-recursive delete path too.
using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class ExitDoorDeleteTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    private static (Node RoomA, Node RoomB) TwoRooms(string area)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var coordA = new Coord(area, 0, 0, 0);
        var coordB = new Coord(area, 0, 1, 0);
        var roomA = new Node(coordA);
        var roomB = new Node(coordB);
        nh.AddNode(roomA);
        nh.AddNode(roomB);
        ObjectRegistry.AddObject(roomA);
        ObjectRegistry.AddObject(roomB);
        roomA.AddLink(new NodeLink("north", coordB));
        roomB.AddLink(new NodeLink("south", coordA));
        return (roomA, roomB);
    }

    // Finding 18: the installed exit command must not walk through a closed
    // and locked door. Neuter (drop the door probe in ExitCommand.Run) moves
    // the PC into room B with the door still Closed+Locked.
    [Fact]
    public void ClosedLockedDoor_BlocksInstalledExitMove()
    {
        using var env = GlobalTestEnv.Enter();
        var (roomA, roomB) = TwoRooms("pin-door");
        var door = Door.Create(roomA.Coord, "north", roomB.Coord, "south", closed: true, locked: true);
        NodeHandler.GetCurrent()?.AddDoor(door);
        var pc = GameObject.Create("pin-door-pc", isPc: true, privilege: Privilege.Admin);
        ObjectRegistry.AddObject(pc);
        Assert.True(pc.MoveTo(roomA, force: true, announce: false));

        RunJob(CommandDispatcher.DispatchLoggedIn(pc, "north", immediate: true));

        Assert.Equal(roomA.Coord, Assert.IsType<LocationRef.CoordLocation>(pc.Location).Coord);
        Assert.True(door.Closed);
        Assert.True(door.Locked);
    }

    // Finding 19: the installed exit passes its direction to MoveTo, so the
    // source room hears "to the north" instead of a bare "walks away.". The
    // destination room hears "from the south" — that half comes from the
    // reverse-link lookup and is geographically correct for a northbound
    // arrival, so the pin asserts it stays. Neuter (MoveTo without toExit)
    // loses "to the north" from the departure.
    [Fact]
    public void InstalledExit_AnnouncesDirectionUsed()
    {
        using var env = GlobalTestEnv.Enter();
        var (roomA, roomB) = TwoRooms("pin-dir");
        var mover = GameObject.Create("pin-dir-mover", isPc: true, privilege: Privilege.Admin);
        var watcherA = GameObject.Create("pin-dir-watcher-a", isNpc: true);
        var watcherB = GameObject.Create("pin-dir-watcher-b", isNpc: true);
        ObjectRegistry.AddObject(mover);
        ObjectRegistry.AddObject(watcherA);
        ObjectRegistry.AddObject(watcherB);
        Assert.True(mover.MoveTo(roomA, force: true, announce: false));
        Assert.True(watcherA.MoveTo(roomA, force: true, announce: false));
        Assert.True(watcherB.MoveTo(roomB, force: true, announce: false));
        mover.ClearMessages(); watcherA.ClearMessages(); watcherB.ClearMessages();

        RunJob(CommandDispatcher.DispatchLoggedIn(mover, "north", immediate: true));

        Assert.Equal(roomB.Coord, Assert.IsType<LocationRef.CoordLocation>(mover.Location).Coord);
        Assert.Contains(watcherA.PeekMessages(), m => m.Contains("to the north", StringComparison.Ordinal));
        Assert.Contains(watcherB.PeekMessages(), m => m.Contains("from the south", StringComparison.Ordinal));
    }

    // Finding 20: arriving in a node from nowhere must install exits. Neuter
    // (restore the oldLoc guard) leaves the arrival with zero exit commands.
    [Fact]
    public void MoveFromNowhere_InstallsExits()
    {
        using var env = GlobalTestEnv.Enter();
        var coord = new Coord("pin-nowhere", 0, 0, 0);
        var node = new Node(coord);
        ObjectRegistry.AddObject(node);
        node.AddLink(new NodeLink("north", new Coord("pin-nowhere", 0, 1, 0)));
        var pc = GameObject.Create("pin-nowhere-pc", isPc: true);
        ObjectRegistry.AddObject(pc);

        Assert.True(pc.MoveTo(node, force: true, announce: false));

        Assert.Contains(pc.InternalCmdSet!.GetAll(), c => c.Key == "north" && c.Tag == "exits");
    }

    // Finding 21: deleting a PC frees its account character slot via the
    // session block of TeardownDeleted. Neuter (drop the RemoveCharacter
    // line) keeps the deleted id listed and the cap rejects the next child.
    [Fact]
    public void DeletePc_FreesAccountCharacterSlot()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = Account.Create("pin-del-acc", "pw-pin-del-xyz");
        ObjectRegistry.AddObject(acc);
        var pc1 = GameObject.Create("pin-del-1", isPc: true);
        var pc2 = GameObject.Create("pin-del-2", isPc: true);
        ObjectRegistry.AddObject(pc1);
        ObjectRegistry.AddObject(pc2);
        Assert.True(acc.TryAddCharacter(pc1, 2));
        Assert.True(acc.TryAddCharacter(pc2, 2));
        pc1.Session = new Session(account: acc);
        var builder = GameObject.Create("pin-del-builder", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(builder);

        Assert.NotNull(pc1.Delete(builder, recursive: false));

        Assert.DoesNotContain(pc1.Id, acc.Characters);
        var pc3 = GameObject.Create("pin-del-3", isPc: true);
        ObjectRegistry.AddObject(pc3);
        Assert.True(acc.TryAddCharacter(pc3, 2));
    }

    // Lambdas cannot carry hook markers and an unmarked delegate never runs
    // as a veto, so the hooks below are real [Replace] methods.
    private sealed class CursedHooks
    {
        [Replace]
        public bool DenyMove(GameObject? d, string? e) => false;
        [Replace]
        public bool ThrowDelete(GameObject? c) => throw new InvalidOperationException("cursed");
    }

    // Finding 22: on the non-recursive path a throwing at_delete is a veto,
    // not an escape: the box is still destroyed and the unmovable child
    // survives at nowhere instead of aborting the parent delete mid-detach.
    // Neuter (drop the pre-detach probe) lets the throw escape with the box
    // still alive.
    [Fact]
    public void NonRecursiveDelete_ThrowingAtDeleteChild_SurvivesAtNowhere()
    {
        using var env = GlobalTestEnv.Enter();
        var box = GameObject.Create("pin-box", isItem: true, isContainer: true);
        var gem = GameObject.Create("pin-gem", isItem: true);
        ObjectRegistry.AddObject(box);
        ObjectRegistry.AddObject(gem);
        var builder = GameObject.Create("pin-box-builder", privilege: Privilege.Admin);
        ObjectRegistry.AddObject(builder);
        Assert.True(gem.MoveTo(box, force: true, announce: false));
        var hooks = new CursedHooks();
        gem.InstallHook("at_pre_move", (Func<GameObject?, string?, bool>)hooks.DenyMove);
        gem.InstallHook("at_delete", (Func<GameObject?, bool>)hooks.ThrowDelete);

        // Controls: the preconditions the finding needs, asserted first so a
        // repro that misses either still goes green while testing nothing.
        Assert.False(gem.MoveTo(new Node(new Coord("pin-box-far", 9, 9, 0)), force: false, announce: false));
        Assert.Throws<InvalidOperationException>(() => gem.AtDelete(builder));

        var ex = Record.Exception(() => box.Delete(builder, recursive: false));

        Assert.Null(ex);
        Assert.True(box.IsDeleted);
        Assert.False(gem.IsDeleted);
        Assert.DoesNotContain(gem.Id, box.ContentsSnapshot);
        Assert.IsType<LocationRef.NullLocation>(gem.Location);
    }
}
