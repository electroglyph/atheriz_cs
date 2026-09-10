using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Node delete home-move: each child's coord home resolves through the shared
// coord index (with scan fallback), matching MoveTo's resolution exactly.
[Collection("Ported")]
public class NodeHomeMoveTests
{
    [Fact]
    public void NodeDelete_MovesChildrenHomeByCoord()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var coordA = new Coord("limbo", 0, 0, 0);
            var coordB = new Coord("limbo", 5, 5, 0);
            var nodeA = new Node(coordA);
            var nodeB = new Node(coordB);
            ObjectRegistry.AddObject(nodeA);
            ObjectRegistry.AddObject(nodeB);
            var admin = GameObject.Create("admin", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            var kids = new List<GameObject>();
            for (int i = 0; i < 3; i++)
            {
                var kid = GameObject.Create($"kid{i}");
                ObjectRegistry.AddObject(kid);
                kid.Home = new LocationRef.CoordLocation(coordB);
                nodeA.AddObject(kid);
                kids.Add(kid);
            }

            var result = nodeA.Delete(admin, recursive: false);

            Assert.NotNull(result);
            foreach (var kid in kids)
            {
                var loc = Assert.IsType<LocationRef.CoordLocation>(kid.Location);
                Assert.Equal(coordB, loc.Coord);
                Assert.Contains(kid.Id, nodeB.ContentsSnapshot);
            }
            Assert.DoesNotContain(kids[0].Id, nodeA.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
