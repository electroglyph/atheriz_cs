// Port of atheriz/tests/test_door_remove.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedDoorRemoveTests
{
    private static (NodeHandler nh, Node n1, Node n2, Door door) SetupDoor(string area="RemoveDoorArea")
    {
        var nh = new NodeHandler(); NodeHandler.SetCurrent(nh);
        var areaObj = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var n1 = new Node(new Coord(area, 0, 0, 0));
        var n2 = new Node(new Coord(area, 0, 2, 0));
        n1.AddLink(new NodeLink("north", new Coord(area, 0, 2, 0), new List<string>{"n"}));
        n2.AddLink(new NodeLink("south", new Coord(area, 0, 0, 0), new List<string>{"s"}));
        grid.AddNode(n1); grid.AddNode(n2); areaObj.AddGrid(grid); nh.AddArea(areaObj);
        var door = new Door(new Coord(area, 0, 0, 0), new Coord(area, 0, 2, 0), "north","south", (0,1), "X","O", true,false);
        nh.AddDoor(door);
        return (nh, n1, n2, door);
    }

    [Fact]
    public void RemoveDoorRemoveDoorDeletesLinks()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,n1,n2,door) = SetupDoor();
        Assert.NotNull(n1.GetLinkByName("north"));
        Assert.NotNull(n2.GetLinkByName("south"));
        Assert.NotNull(nh.GetDoors(n1.Coord)); Assert.Contains("north", nh.GetDoors(n1.Coord)!.Keys);
        nh.RemoveDoor(door);
        Assert.Null(n1.GetLinkByName("north"));
        Assert.Null(n2.GetLinkByName("south"));
        Assert.False(n1.HasLinkName("north"));
        Assert.False(n2.HasLinkName("south"));
        Assert.Null(n1.GetLinkByName("n"));
    }

    [Fact]
    public void RemoveDoorRemoveDoorDeletesDoorsDict()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,n1,n2,door) = SetupDoor("RemoveDoorArea2");
        Assert.NotNull(nh.GetDoors(n1.Coord));
        Assert.NotNull(nh.GetDoors(n2.Coord));
        nh.RemoveDoor(door);
        var d1 = nh.GetDoors(n1.Coord);
        var d2 = nh.GetDoors(n2.Coord);
        Assert.True(d1 == null || !d1.ContainsKey("north"));
        Assert.True(d2 == null || !d2.ContainsKey("south"));
    }

    [Fact]
    public void RemoveDoorRemoveDoorDisplayExitsNoLongerShows()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,n1,n2,door) = SetupDoor("RemoveDoorArea3");
        Assert.Contains("north", n1.GetDisplayExits());
        Assert.Contains("south", n2.GetDisplayExits());
        nh.RemoveDoor(door);
        Assert.DoesNotContain("north", n1.GetDisplayExits());
        Assert.DoesNotContain("south", n2.GetDisplayExits());
    }

    [Fact]
    public void RemoveDoorRemoveDoorPlayerCannotUseExit()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,n1,n2,door) = SetupDoor("RemoveDoorArea4");
        var player = GameObject.Create("Adventurer", isPc:true); ObjectRegistry.AddObject(player);
        player.Location = new Persistence.Dto.LocationRef.CoordLocation(n1.Coord); n1.AddObject(player);
        Assert.NotNull(n1.GetLinkByName("north"));
        nh.RemoveDoor(door);
        Assert.Null(n1.GetLinkByName("north"));
        var (found, _, _) = Pathfind.AStar(n1, n2, player, nh);
        Assert.False(found);
        Assert.Equal(n1, nh.GetNode(n1.Coord));
        Assert.Null(n1.GetLinkByName("north"));
    }

    [Fact]
    public void RemoveDoorRemoveDoorBothCoordsCleaned()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,n1,n2,door) = SetupDoor("RemoveDoorArea5");
        Assert.Equal(door, nh.GetDoors(n1.Coord)!["north"]);
        Assert.Equal(door, nh.GetDoors(n2.Coord)!["south"]);
        nh.RemoveDoor(door);
        var d1 = nh.GetDoors(n1.Coord);
        var d2 = nh.GetDoors(n2.Coord);
        Assert.True(d1 == null || !d1.ContainsKey("north"));
        Assert.True(d2 == null || !d2.ContainsKey("south"));
        Assert.Null(n1.GetLinkByName("north"));
        Assert.Null(n2.GetLinkByName("south"));
    }

    [Fact]
    public void RemoveDoorRemoveDoorViaDoorCreateAndAddDoorThenRemove()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(); NodeHandler.SetCurrent(nh);
        var area = new NodeArea("RemoveDoorArea6");
        var grid = new NodeGrid("RemoveDoorArea6", 0);
        var n1 = new Node(new Coord("RemoveDoorArea6", 0, 0, 0));
        var n2 = new Node(new Coord("RemoveDoorArea6", 0, 2, 0));
        n1.AddLink(new NodeLink("north", new Coord("RemoveDoorArea6", 0, 2, 0)));
        n2.AddLink(new NodeLink("south", new Coord("RemoveDoorArea6", 0, 0, 0)));
        grid.AddNode(n1); grid.AddNode(n2); area.AddGrid(grid); nh.AddArea(area);
        var door = new Door(new Coord("RemoveDoorArea6", 0, 0, 0), new Coord("RemoveDoorArea6", 0, 2, 0), "north","south", (0,1), "X","O", true,false);
        nh.AddDoor(door);
        Assert.NotNull(n1.GetLinkByName("north"));
        Assert.NotNull(n2.GetLinkByName("south"));
        nh.RemoveDoor(door);
        Assert.Null(n1.GetLinkByName("north"));
        Assert.Null(n2.GetLinkByName("south"));
        Assert.True(string.IsNullOrEmpty(n1.GetDisplayExits()) || !n1.GetDisplayExits().Contains("north"));
        Assert.True(string.IsNullOrEmpty(n2.GetDisplayExits()) || !n2.GetDisplayExits().Contains("south"));
    }

    [Fact]
    public void RemoveDoorRemoveDoorTwiceIdempotent()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh,n1,n2,door) = SetupDoor("RemoveDoorArea7");
        nh.RemoveDoor(door);
        nh.RemoveDoor(door);
        Assert.Null(n1.GetLinkByName("north"));
        Assert.Null(n2.GetLinkByName("south"));
    }
}
