using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

// Containment bookkeeping and MoveTo location consistency: add/remove sync and cycle guards.
[Collection("Ported")]
public class ContainmentTests
{
    private static void RegisterAll(params GameObject[] objs)
    {
        foreach (var o in objs)
            if (ObjectRegistry.Get(o.Id).Count == 0)
                ObjectRegistry.AddObject(o);
    }

    // --- Containment bookkeeping ---

    [Fact]
    public void RemoveObject_ClearsMoversLocation()
    {
        // Removing an object drops its id from contents and clears its Location
        // back to null (no stale ObjectLocation may remain).
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
        // Membership in a node implies the node's coord: Node.AddObject assigns
        // CoordLocation, mirroring MoveTo.
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
        // MoveTo into an own descendant is denied: the location chain is severed
        // here so only the container cycle guard can catch it, even for wide
        // containers.
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

    // --- MoveTo hook gaps ---

    [Fact]
    public void MoveTo_NullDestination_VanishesWithoutLeaveVeto()
    {
        // Python parity (base_obj.py:1155-1163: the None path calls
        // loc.remove_object + at_post_move only): vanishing to null skips the
        // leave veto; only the at_pre_move exit-access gate applies.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var item = GameObject.Create("item");
            RegisterAll(room, item);
            Assert.True(item.MoveTo(room));
            room.InstallHook("at_pre_object_leave", (Func<GameObject?, string?, bool>)new VetoHooks().DenyAll);
            Assert.True(item.MoveTo(null));
            Assert.DoesNotContain(item.Id, room.ContentsSnapshot);
            Assert.IsType<LocationRef.NullLocation>(item.Location);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MoveTo_ContainerToContainer_IgnoresReceiveVeto()
    {
        // Python parity (base_obj.py:1238-1259: leave/receive hooks fire only
        // when old/new loc is_node): container moves consult no object hooks,
        // only the at_pre_move gate.
        ObjectRegistry.ClearAll();
        try
        {
            var src = GameObject.Create("src", isContainer: true);
            var dst = GameObject.Create("dst", isContainer: true);
            var item = GameObject.Create("item");
            RegisterAll(src, dst, item);
            Assert.True(item.MoveTo(src));
            dst.InstallHook("at_pre_object_receive", (Func<GameObject?, string?, bool>)new VetoHooks().DenyAll);
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
            src.InstallHook("at_pre_object_leave", (Func<GameObject?, string?, bool>)new VetoHooks().DenyAll);
            Assert.True(item.MoveTo(dst));
            Assert.DoesNotContain(item.Id, src.ContentsSnapshot);
            Assert.Contains(item.Id, dst.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void MoveTo_DeletedMover_IsAllowed_LikePython()
    {
        // Python parity (base_obj.py:1233: only destination.is_deleted is
        // checked): a deleted mover can still move.
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

    // [Replace]-attributed veto hook (replaces the removed At*Override seam;
    // lambdas cannot carry attributes, so a real method provides the marker).
    private sealed class VetoHooks
    {
        [Replace]
        public bool DenyAll(GameObject? a, string? b) => false;
    }
}
