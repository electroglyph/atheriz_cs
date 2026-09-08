using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Utils;

// Neighbor listing shares AStar's caller dispatch (pathfind.py:122-126):
// door-blind for caller=None, door-aware otherwise. A link sealed by a
// closed door the caller cannot open is not a usable move for that caller,
// while the null-caller view ignores doors like upstream.
[Collection("Ported")]
public sealed class NeighborsTests
{
    [Fact]
    public void GetNeighbors_ClosedDeniedDoor_ExcludesLinkedNode()
    {
        ObjectRegistry.ClearAll();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var coordA = new Coord("NeighborDoorArea", 0, 0, 0);
            var coordB = new Coord("NeighborDoorArea", 1, 0, 0);
            var a = new Node(coordA);
            var b = new Node(coordB);
            if (ObjectRegistry.Get(a.Id).Count == 0) ObjectRegistry.AddObject(a);
            if (ObjectRegistry.Get(b.Id).Count == 0) ObjectRegistry.AddObject(b);
            a.AddLink(new NodeLink("east", coordB, new List<string> { "e" }));
            b.AddLink(new NodeLink("west", coordA, new List<string> { "w" }));
            nh.AddNode(a);
            nh.AddNode(b);
            var door = new Door(coordA, coordB, "east", "west", null, "", "", closed: true, locked: false);
            door.AddLock("open", _ => false);
            nh.AddDoor(door);

            var caller = GameObject.Create("walker");
            ObjectRegistry.AddObject(caller);

            // Control: caller-aware pathing already refuses the sealed link,
            // proving the fixture door actually blocks this caller.
            var (found, _, _) = Pathfind.AStar(a, b, caller, nh);
            Assert.False(found);

            // Caller-aware neighbor listing refuses it too.
            Assert.DoesNotContain(coordB, Pathfind.GetNeighbors(coordA, nh, caller));
            Assert.DoesNotContain(coordA, Pathfind.GetNeighbors(coordB, nh, caller));

            // Null-caller view is door-blind on both sides (upstream parity).
            var (foundBlind, _, _) = Pathfind.AStar(a, b, null, nh);
            Assert.True(foundBlind);
            Assert.Contains(coordB, Pathfind.GetNeighbors(coordA, nh));
            Assert.Contains(coordA, Pathfind.GetNeighbors(coordB, nh));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
