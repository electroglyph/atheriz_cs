using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

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
