using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Utils;

// Behavior pins for pathfinding overloads on an open grid: the plain and
// door-aware overloads agree, and neighbor lookup includes the target.
[Collection("Ported")]
public class PathfindingTests
{
    [Fact]
    public void Pathfind_Overloads_Agree_OnOpenGrid()
    {
        // Door-aware overload agrees with the plain overload on an open grid.
        ObjectRegistry.ClearAll();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var coordA = new Coord("limbo", 0, 0, 0);
            var coordB = new Coord("limbo", 2, 0, 0);
            var a = new Node(coordA);
            var b = new Node(coordB);
            if (ObjectRegistry.Get(a.Id).Count == 0) ObjectRegistry.AddObject(a);
            if (ObjectRegistry.Get(b.Id).Count == 0) ObjectRegistry.AddObject(b);
            a.AddLink(new NodeLink("east", coordB, new List<string> { "e" }));
            b.AddLink(new NodeLink("west", coordA, new List<string> { "w" }));
            nh.AddNode(a);
            nh.AddNode(b);
            var plain = Pathfind.FindPath(coordA, coordB, nh);
            var caller = GameObject.Create("walker");
            ObjectRegistry.AddObject(caller);
            var doorAware = Pathfind.FindPath(coordA, coordB, nh, caller);
            Assert.NotNull(plain);
            Assert.NotNull(doorAware);
            Assert.Equal(plain!, doorAware!);
            Assert.Contains(coordB, Pathfind.GetNeighbors(coordA, nh));
        }
        finally { NodeHandler.SetCurrent(null); ObjectRegistry.ClearAll(); }
    }
}
