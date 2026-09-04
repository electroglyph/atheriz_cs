// Port of atheriz/tests/test_utils_coords.py:1
using Atheriz.Core;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedUtilsCoordsTests
{
    [Fact] public void GetDir_Cardinals()
    {
        using var env = GlobalTestEnv.Enter();
        var origin = new Coord("limbo",0,0,0);
        Assert.Equal("north", GameUtils.GetDir(origin, new Coord("limbo",0,1,0)));
        Assert.Equal("south", GameUtils.GetDir(origin, new Coord("limbo",0,-1,0)));
        Assert.Equal("east", GameUtils.GetDir(origin, new Coord("limbo",1,0,0)));
        Assert.Equal("west", GameUtils.GetDir(origin, new Coord("limbo",-1,0,0)));
    }
    [Fact] public void GetDir_Diagonals()
    {
        using var env = GlobalTestEnv.Enter();
        var o = new Coord("limbo",0,0,0);
        Assert.Equal("northeast", GameUtils.GetDir(o, new Coord("limbo",1,1,0)));
        Assert.Equal("northwest", GameUtils.GetDir(o, new Coord("limbo",-1,1,0)));
        Assert.Equal("southeast", GameUtils.GetDir(o, new Coord("limbo",1,-1,0)));
        Assert.Equal("southwest", GameUtils.GetDir(o, new Coord("limbo",-1,-1,0)));
    }
    [Fact] public void GetDir_FarAway()
    {
        using var env = GlobalTestEnv.Enter();
        var o = new Coord("limbo",0,0,0);
        Assert.Equal("northeast", GameUtils.GetDir(o, new Coord("limbo",10,5,0)));
        Assert.Equal("southwest", GameUtils.GetDir(o, new Coord("limbo",-10,-5,0)));
    }
    [Fact] public void GetDir_SameSpot()
    {
        using var env = GlobalTestEnv.Enter();
        var o = new Coord("limbo",0,0,0);
        Assert.Equal("", GameUtils.GetDir(o,o));
    }
    [Fact] public void GetDir_VerticalIgnored()
    {
        using var env = GlobalTestEnv.Enter();
        var o = new Coord("limbo",0,0,0);
        Assert.Equal("north", GameUtils.GetDir(o, new Coord("limbo",0,1,10)));
        Assert.Equal("", GameUtils.GetDir(o, new Coord("limbo",0,0,10)));
    }
    [Fact] public void Dist3d_Axes()
    {
        using var env = GlobalTestEnv.Enter();
        var o = new Coord("limbo",0,0,0);
        Assert.Equal(3.0, GameUtils.Dist3d(o, new Coord("limbo",3,0,0)));
        Assert.Equal(4.0, GameUtils.Dist3d(o, new Coord("limbo",0,4,0)));
        Assert.Equal(5.0, GameUtils.Dist3d(o, new Coord("limbo",0,0,5)));
    }
    [Fact] public void Dist3d_Diagonal2D()
    {
        using var env = GlobalTestEnv.Enter();
        var o = new Coord("limbo",0,0,0);
        Assert.Equal(5.0, GameUtils.Dist3d(o, new Coord("limbo",3,4,0)));
    }
    [Fact] public void Dist3d_Diagonal3D()
    {
        using var env = GlobalTestEnv.Enter();
        var o = new Coord("limbo",0,0,0);
        Assert.True(Math.Abs(GameUtils.Dist3d(o, new Coord("limbo",1,1,1)) - Math.Sqrt(3)) < 1e-9);
    }
    [Fact] public void Dist3d_Tuple()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Equal(5.0, GameUtils.Dist3d((0,0,0),(3,4,0)));
    }
    [Fact] public void Dist3d_MixedCoordTuple()
    {
        using var env = GlobalTestEnv.Enter();
        var o = new Coord("limbo",0,0,0);
        Assert.Equal(5.0, GameUtils.Dist3d(o, new object[]{"limbo",0,3,4,0}));
        Assert.Equal(5.0, GameUtils.Dist3d(new object[]{"limbo",0,0,0,0}, new object[]{"limbo",0,3,4,0}));
        Assert.Equal(5.0, GameUtils.Dist3d(new object[]{0,0,0}, new object[]{3,4,0}));
    }
}
