using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Converters;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Single construction point for coord-backed locations: the factory builds
// the identical record (same values, same coordinate order, same JSON) as
// the inline constructor it replaces at every call site.
[Collection("Ported")]
public sealed class CoordLocationFactoryTests
{
    [Fact]
    public void FromCoord_MatchesDirectConstruction_ValueAndOrder()
    {
        var coord = new Coord("limbo", 1, 2, 3);
        Assert.Equal(new LocationRef.CoordLocation(coord), LocationRef.FromCoord(coord));
        var viaFactory = LocationRef.FromCoord(coord);
        Assert.Equal("limbo", viaFactory.Coord.Area);
        Assert.Equal(1, viaFactory.Coord.X);
        Assert.Equal(2, viaFactory.Coord.Y);
        Assert.Equal(3, viaFactory.Coord.Z);
    }

    [Fact]
    public void FromCoord_SerializesIdentically()
    {
        var coord = new Coord("limbo", 4, 4, 4);
        Assert.Equal(
            JsonSerializer.Serialize(new LocationRef.CoordLocation(coord)),
            JsonSerializer.Serialize(LocationRef.FromCoord(coord)));
    }

    [Fact]
    public void BuildDto_ForNode_UsesFactoryValue()
    {
        using var env = GlobalTestEnv.Enter();
        var coord = new Coord("factorydto", 5, 6, 7);
        var node = new Node(coord);
        ObjectRegistry.AddObject(node);
        var dto = GameObjectDtoConverter.BuildDto(node);
        Assert.Equal(LocationRef.FromCoord(coord), dto.Location);
    }

    [Fact]
    public void NodeAddObject_SetsFactoryLocation()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea("factoryadd");
        var grid = new NodeGrid("factoryadd", 0);
        var room = new Node(new Coord("factoryadd", 0, 0, 0));
        grid.AddNode(room);
        areaObj.AddGrid(grid);
        nh.AddArea(areaObj);
        ObjectRegistry.AddObject(room);
        var obj = GameObject.Create("factoryobj");
        ObjectRegistry.AddObject(obj);
        room.AddObject(obj);
        Assert.Equal(LocationRef.FromCoord(room.Coord), obj.Location);
    }

    [Fact]
    public void NodeAddObjects_SetsFactoryLocation()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea("factoryadds");
        var grid = new NodeGrid("factoryadds", 0);
        var room = new Node(new Coord("factoryadds", 0, 0, 0));
        grid.AddNode(room);
        areaObj.AddGrid(grid);
        nh.AddArea(areaObj);
        ObjectRegistry.AddObject(room);
        var obj = GameObject.Create("factoryobjs");
        ObjectRegistry.AddObject(obj);
        room.AddObjects([obj]);
        Assert.Equal(LocationRef.FromCoord(room.Coord), obj.Location);
    }
}
