// Pins for the NodeHandler primary-lock scope conversion: AddArea, GetArea,
// GetAreas, and the AddNode tail flag write use the handler WriteScope /
// ReadScope around the same lock with the same boundaries, so area install,
// lookup, enumeration, node insert, save/load round-trip, and the untouched
// door value-removal path all behave as before.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B25aNodeScopeTests
{
    [Fact]
    public void GetArea_AfterAddArea_ReturnsInstalledArea()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            var area = new NodeArea("b25a-scope");
            nh.AddArea(area);
            Assert.Same(area, nh.GetArea("b25a-scope"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void GetAreas_AfterMultipleAdds_ContainsAllAreas()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            nh.AddArea(new NodeArea("b25a-one"));
            nh.AddArea(new NodeArea("b25a-two"));
            var names = nh.GetAreas().Select(a => a.Name).ToHashSet();
            Assert.Contains("b25a-one", names);
            Assert.Contains("b25a-two", names);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void GetNode_AfterAddNode_ResolvesThroughAreaGrid()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            var coord = new Coord("b25a-node", 3, 4, 0);
            var node = new Node(coord);
            nh.AddNode(node);
            Assert.Same(node, nh.GetNode(coord));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Save_Load_RoundTrip_PreservesAreas()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            nh.AddArea(new NodeArea("b25a-saved"));
            using (var db = new AtherizDbContext(env.TempPath))
            {
                db.Database.EnsureCreated();
                nh.Save(db, force: true);
            }
            var reloaded = new NodeHandler(autoLoad: false);
            using (var db = new AtherizDbContext(env.TempPath))
            {
                reloaded.Load(db);
            }
            Assert.NotNull(reloaded.GetArea("b25a-saved"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void RemoveDoor_ByValue_ClearsBothEndpointRows()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            var from = new Coord("b25a-door", 0, 0, 0);
            var to = new Coord("b25a-door", 1, 0, 0);
            var door = new Door(from, to, "east", "west", (0, 0), "X", "O", true, false);
            nh.AddDoor(door);
            Assert.NotNull(nh.GetDoors(from));
            nh.RemoveDoor(door);
            var fromRow = nh.GetDoors(from);
            var toRow = nh.GetDoors(to);
            Assert.True(fromRow is null || !fromRow.ContainsKey("east"));
            Assert.True(toRow is null || !toRow.ContainsKey("west"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
