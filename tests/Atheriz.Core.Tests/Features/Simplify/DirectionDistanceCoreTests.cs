using Atheriz.Core;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify;

// Unified direction/distance cores: list and Coord overloads agree, and every
// Dist3d overload returns the same Pow-formulation value.
public class DirectionDistanceCoreTests
{
    [Theory]
    [InlineData("north", 0, 0, 0, 1)]
    [InlineData("south", 0, 1, 0, 0)]
    [InlineData("east", 0, 0, 1, 0)]
    [InlineData("west", 1, 0, 0, 0)]
    [InlineData("northeast", 0, 0, 1, 1)]
    [InlineData("southwest", 1, 1, 0, 0)]
    [InlineData("", 0, 0, 0, 0)]
    public void GetDir_ListAndCoordOverloads_Agree(string expected, int ox, int oy, int dx, int dy)
    {
        var listOrigin = new object?[] { ox, oy };
        var listDest = new object?[] { dx, dy };
        Assert.Equal(expected, GameUtils.GetDir(listOrigin, listDest));
        Assert.Equal(expected, GameUtils.GetDir(new Coord("limbo", ox, oy, 0), new Coord("limbo", dx, dy, 0)));
    }

    [Fact]
    public void GetDir_AreaPrefixedLists_ComposeNsEwInOrder()
    {
        Assert.Equal("northeast", GameUtils.GetDir(
            new object?[] { "limbo", 0, 0, 0 }, new object?[] { "limbo", 1, 1, 0 }));
    }

    [Fact]
    public void Dist3d_AllOverloads_AgreeOnKnownValue()
    {
        var coordOrigin = new Coord("a", 0, 0, 0);
        var coordDest = new Coord("a", 3, 4, 0);
        Assert.Equal(5.0, GameUtils.Dist3d(coordOrigin, coordDest), 9);
        Assert.Equal(5.0, GameUtils.Dist3d((0, 0, 0), (3, 4, 0)), 9);
        Assert.Equal(5.0, GameUtils.Dist3d(
            new object?[] { 0, 0, 0 }, new object?[] { 3, 4, 0 }), 9);
        Assert.Equal(5.0, GameUtils.Dist3d(
            new object?[] { "a", 0, 0, 0 }, new object?[] { "a", 3, 4, 0 }), 9);
    }
}
