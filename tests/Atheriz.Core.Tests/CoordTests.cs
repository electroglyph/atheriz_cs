using Atheriz.Core;

namespace Atheriz.Core.Tests;

public class CoordTests
{
    [Fact]
    public void Coord_Equality_ByValue()
    {
        var a = new Coord("limbo", 4, 4, 4);
        var b = new Coord("limbo", 4, 4, 4);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Coord_DifferentArea_NotEqual()
    {
        Assert.NotEqual(new Coord("limbo", 0, 0, 0), new Coord("void", 0, 0, 0));
    }
}
