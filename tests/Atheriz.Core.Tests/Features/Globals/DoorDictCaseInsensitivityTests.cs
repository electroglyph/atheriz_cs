using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// Door direction keys match regardless of letter case: a door filed under a
// mixed-case direction is found by lowercase lookups, so movement honors its
// closed/locked state instead of walking through it.
[Collection("Ported")]
public sealed class DoorDictCaseInsensitivityTests
{
    private static readonly Coord Src = new("casedoor", 0, 0, 0);
    private static readonly Coord Dest = new("casedoor", 0, 2, 0);

    [Fact]
    public void MixedCaseDoorKey_ResolvesFromAnyCasing()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        var door = Door.Create(Src, "North", Dest, "South", closed: true, locked: true);
        nh.AddDoor(door);

        var from = nh.GetDoors(Src);
        Assert.NotNull(from);
        Assert.Same(door, from["north"]);
        Assert.Same(door, from["NORTH"]);
        Assert.Same(door, from["North"]);

        var to = nh.GetDoors(Dest);
        Assert.NotNull(to);
        Assert.Same(door, to["south"]);
        Assert.Same(door, to["SOUTH"]);
    }

    [Fact]
    public void LockedDoor_WithMixedCaseKey_BlocksMovement()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            nh.AddNode(new Node(Src));
            nh.AddNode(new Node(Dest));
            var door = Door.Create(Src, "North", Dest, "South", closed: true, locked: true);
            nh.AddDoor(door);
            var walker = GameObject.Create("walker", isPc: true);
            ObjectRegistry.AddObject(walker);
            walker.Location = new LocationRef.CoordLocation(Src);

            var cmd = new LoggedInExitCommand
            {
                CallerId = walker.Id,
                Location = Src,
                Destination = Dest,
                ExitName = "north",
            };
            cmd.DoMove();

            var loc = walker.ResolveLocationObject() as Node;
            Assert.Equal(Src, loc?.Coord);
            Assert.True(door.Closed);
            Assert.True(door.Locked);
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
