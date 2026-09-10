using Atheriz.Core.Globals;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Globals;

// PreRender without the working grid copy: placeholders still resolve against
// the pre-resolution neighbor set (two-pass publish order), per-instance
// settings still drive the output, and non-placeholders pass through.
[Collection("Ported")]
public class PreRenderInPlaceTests
{
    [Fact]
    public void PreRender_CustomPlaceholders_ResolvePerInstanceSettings()
    {
        // The style table is per-Settings-instance: "#" is a wall only for
        // the custom instance, inert for the default one.
        var custom = new AtherizSettings { SingleWallPlaceholder = "#" };
        var mi = new MapInfo("customrender",
            preGrid: new Dictionary<(int, int), string> { [(0, 0)] = "#" },
            settings: custom);
        mi.PreRender();
        // Isolated single wall with no neighbors resolves to a horizontal stub.
        Assert.Equal("─", mi.PostGrid[(0, 0)]);

        var plain = new MapInfo("plainrender",
            preGrid: new Dictionary<(int, int), string> { [(0, 0)] = "#" });
        plain.PreRender();
        Assert.Equal("#", plain.PostGrid[(0, 0)]);
    }

    [Fact]
    public void PreRender_RoomPlaceholder_ClearsAndPreservesOthers()
    {
        var settings = new AtherizSettings();
        var mi = new MapInfo("roomrender",
            preGrid: new Dictionary<(int, int), string>
            {
                [(0, 0)] = settings.RoomPlaceholder,
                [(1, 0)] = "X",
            },
            settings: settings);
        mi.PreRender();
        Assert.Equal(" ", mi.PostGrid[(0, 0)]);
        Assert.Equal("X", mi.PostGrid[(1, 0)]);
        Assert.Equal(2, mi.PostGrid.Count);
    }

    [Fact]
    public void PreRender_TwoByTwoBlock_ResolvesAgainstUnresolvedNeighbors()
    {
        // A 2x2 single-wall block must see placeholder neighbors, not
        // in-progress glyphs: each corner resolves against the original set.
        var settings = new AtherizSettings();
        string w = settings.SingleWallPlaceholder;
        var mi = new MapInfo("blockrender",
            preGrid: new Dictionary<(int, int), string>
            {
                [(0, 0)] = w, [(1, 0)] = w,
                [(0, 1)] = w, [(1, 1)] = w,
            },
            settings: settings);
        mi.PreRender();
        Assert.Equal("└", mi.PostGrid[(0, 0)]);
        Assert.Equal("┘", mi.PostGrid[(1, 0)]);
        Assert.Equal("┌", mi.PostGrid[(0, 1)]);
        Assert.Equal("┐", mi.PostGrid[(1, 1)]);
    }
}
