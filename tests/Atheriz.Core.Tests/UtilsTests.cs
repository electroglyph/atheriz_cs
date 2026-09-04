using Atheriz.Core;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests;

public class UtilsTests
{
    [Theory]
    [InlineData("north", 0, 0, 0, 1)]
    [InlineData("south", 0, 1, 0, 0)]
    [InlineData("east", 0, 0, 1, 0)]
    [InlineData("west", 1, 0, 0, 0)]
    [InlineData("northeast", 0, 0, 1, 1)]
    [InlineData("", 0, 0, 0, 0)]
    public void GetDir_Coord(string expected, int ox, int oy, int dx, int dy)
    {
        var o = new Coord("limbo", ox, oy, 0);
        var d = new Coord("limbo", dx, dy, 0);
        Assert.Equal(expected, GameUtils.GetDir(o, d));
    }

    [Fact]
    public void GetDir_DifferentArea_ReturnsEmpty()
    {
        Assert.Equal("", GameUtils.GetDir(new Coord("a", 0, 0, 0), new Coord("b", 1, 1, 0)));
    }

    [Fact]
    public void Dist3d_Coord()
    {
        Assert.Equal(5.0, GameUtils.Dist3d(new Coord("a", 0, 0, 0), new Coord("a", 3, 4, 0)), 5);
    }

    [Fact]
    public void StripAnsi_RemovesCodes()
    {
        Assert.Equal("hi", GameUtils.StripAnsi("\x1b[31mhi\x1b[0m"));
    }

    [Fact]
    public void GetPointsInSphere_Radius0_OnlyCenter()
    {
        var pts = GameUtils.GetPointsInSphere((0, 0, 0), 0);
        Assert.Single(pts);
        Assert.Contains((0, 0, 0), pts);
    }

    [Fact]
    public void GetPointsInSphere_Radius1_7Points()
    {
        var pts = GameUtils.GetPointsInSphere((0, 0, 0), 1);
        // center + 6 axial neighbors = 7 in integer lattice
        Assert.Equal(7, pts.Count);
    }

    [Fact]
    public void GetPointsInSphere_OutOfBounds_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameUtils.GetPointsInSphere((0, 0, 0), 101));
    }

    [Fact]
    public void Clamp_Works()
    {
        Assert.Equal(5, GameUtils.Clamp(0, 5, 10));
        Assert.Equal(0, GameUtils.Clamp(0, -5, 10));
        Assert.Equal(10, GameUtils.Clamp(0, 15, 10));
    }

    [Fact]
    public void IsInGameFolder_Outside_ReturnsFalse()
    {
        // Running from repo root or atheriz-cs should not be a game folder
        Assert.False(GameUtils.IsInGameFolder());
    }
}
