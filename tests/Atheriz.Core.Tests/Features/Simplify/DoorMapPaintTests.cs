using Atheriz.Core;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Door map paint: MapClose/MapOpen share one parameterized body differing
// only in preset symbol + closed flag; the gate (map disabled, missing symbol
// coord, default endpoints) returns before touching any handler.
[Collection("Ported")]
public class DoorMapPaintTests
{
    [Fact]
    public void MapClose_MapOpen_AreNoOps_WithoutSymbolCoord()
    {
        var door = new Door(new Coord("limbo", 0, 0, 0), new Coord("limbo", 0, 1, 0), "north", "south");

        door.MapClose();
        door.MapOpen();

        Assert.True(door.Closed);
        Assert.False(door.Locked);
    }

    [Fact]
    public void MapPaint_ReturnsEarly_WhenMapDisabled()
    {
        bool prev = AtherizSettings.Global.MapEnabled;
        AtherizSettings.Global.MapEnabled = false;
        try
        {
            var door = new Door(new Coord("limbo", 0, 0, 0), new Coord("limbo", 0, 1, 0), "north", "south", (1, 2));

            door.MapClose();
            door.MapOpen();

            Assert.True(door.Closed);
        }
        finally { AtherizSettings.Global.MapEnabled = prev; }
    }
}
