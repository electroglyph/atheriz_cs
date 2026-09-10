using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// One no-cache trio, one first-existing-file helper, one route-table loop for
// the draw aliases (registration order preserved: first-match wins), and one
// merged immutable-cache condition.
[Collection("Ported")]
public class StaticRouteHelperTests
{
    [Fact]
    public void NoCacheTrio_DefinedOnce_UsedThrice()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "StaticFileConfig.cs");
        Assert.Equal(1, SourceScan.Count(src, "private static void SetNoCache("));
        Assert.Equal(4, SourceScan.Count(src, "SetNoCache(")); // def + 3 entry-HTML shapes
        Assert.Contains("no-cache, no-store, must-revalidate", src);
    }

    [Fact]
    public void FileFallbacks_ShareFirstExisting()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "StaticFileConfig.cs");
        Assert.Equal(1, SourceScan.Count(src, "private static string? FirstExisting("));
        Assert.True(SourceScan.Count(src, "FirstExisting(") >= 3);
    }

    [Fact]
    public void DrawAliases_LoopInOrder_ImmutableMerged()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "StaticFileConfig.cs");
        Assert.Contains("foreach (var route in new[] { \"/atheriz_draw\"", src);
        Assert.Contains("||", SourceScan.Region(src, "bool immutable ="));
        Assert.Contains("public, max-age=31536000, immutable", src);
        Assert.Contains("public, max-age=86400", src);
    }
}
