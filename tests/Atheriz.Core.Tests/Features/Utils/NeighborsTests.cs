using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Utils;

// Neighbor listing must agree with door-aware pathing: a link sealed by a
// closed door the caller cannot open is not a usable move (Pathfind.cs:259-267
// walks links without consulting doors, while AStar filters by caller/doors at
// Pathfind.cs:164-166 via GetLinkNodesCaller at Pathfind.cs:74-110).
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

            // Neighbor listing must refuse it too.
            Assert.DoesNotContain(coordB, Pathfind.GetNeighbors(coordA, nh));
            Assert.DoesNotContain(coordA, Pathfind.GetNeighbors(coordB, nh));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
