// Pins for U8 (MapInfo.GetDirs HashSet core): the HashSet callers produce the
// same direction tuples the removed List overload forwarded to.
using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Features.Globals;

[Collection("Ported")]
public sealed class GetDirsTests
{
    [Fact]
    public void GetDirs_NoNeighbors_ReturnsAllFalse()
    {
        var (n, s, e, w) = MapInfo.GetDirs(new Dictionary<(int, int), string>(), (0, 0), new HashSet<string> { "#" });
        Assert.Equal((false, false, false, false), (n, s, e, w));
    }

    [Fact]
    public void GetDirs_NorthNeighbor_ReturnsNorthOnly()
    {
        var grid = new Dictionary<(int, int), string> { [(0, 1)] = "#" };
        var (n, s, e, w) = MapInfo.GetDirs(grid, (0, 0), new HashSet<string> { "#" });
        Assert.True(n);
        Assert.False(s);
        Assert.False(e);
        Assert.False(w);
    }

    [Fact]
    public void GetDirs_AllNeighbors_ReturnsAllTrue()
    {
        var grid = new Dictionary<(int, int), string> { [(0, 1)] = "#", [(0, -1)] = "#", [(1, 0)] = "#", [(-1, 0)] = "#" };
        var (n, s, e, w) = MapInfo.GetDirs(grid, (0, 0), new HashSet<string> { "#" });
        Assert.Equal((true, true, true, true), (n, s, e, w));
    }

    [Fact]
    public void GetDirs_NonMatchingChar_ReturnsFalse()
    {
        var grid = new Dictionary<(int, int), string> { [(0, 1)] = "X" };
        var (n, _, _, _) = MapInfo.GetDirs(grid, (0, 0), new HashSet<string> { "#" });
        Assert.False(n);
    }
}
