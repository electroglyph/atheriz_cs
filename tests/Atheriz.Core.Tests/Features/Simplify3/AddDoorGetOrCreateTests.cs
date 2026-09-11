using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// AddDoor routes both endpoints through one fetch-or-create core: a door is
// resolvable from either coord, and a second door sharing a coord extends the
// per-coord row instead of replacing it.
[Collection("Ported")]
public sealed class AddDoorGetOrCreateTests
{
    private static readonly Coord C1 = new("doors", 0, 0, 0);
    private static readonly Coord C2 = new("doors", 1, 0, 0);
    private static readonly Coord C3 = new("doors", 2, 0, 0);

    [Fact]
    public void AddDoor_TwoEndedDoor_ResolvableFromBothCoords()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        var door = new Door(C1, C2, "east", "west", (0, 0), "X", "O", true, false);
        nh.AddDoor(door);

        var from = nh.GetDoors(C1);
        Assert.NotNull(from);
        Assert.True(from.ContainsKey("east"));
        Assert.Same(door, from["east"]);

        var to = nh.GetDoors(C2);
        Assert.NotNull(to);
        Assert.True(to.ContainsKey("west"));
        Assert.Same(door, to["west"]);
    }

    [Fact]
    public void AddDoor_SecondDoorSameCoord_KeepsBothRows()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        nh.AddDoor(new Door(C1, C2, "east", "west", (0, 0), "X", "O", true, false));
        nh.AddDoor(new Door(C1, C3, "north", "south", (0, 0), "X", "O", true, false));

        var from = nh.GetDoors(C1);
        Assert.NotNull(from);
        Assert.True(from.ContainsKey("east"));
        Assert.True(from.ContainsKey("north"));

        var far = nh.GetDoors(C3);
        Assert.NotNull(far);
        Assert.True(far.ContainsKey("south"));
    }
}
