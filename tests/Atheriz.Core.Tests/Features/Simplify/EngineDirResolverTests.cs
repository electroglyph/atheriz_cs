using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Simplify;

// One assembly-dir probe for the engine resolvers (sub-path + optional
// fallback differ per caller). The ResolveTemplates post-check re-probe is
// gone: the table already yields the engine dir, so a null result means it
// was absent at probe time.
[Collection("Ported")]
public class EngineDirResolverTests
{
    [Fact]
    public void EngineProbe_DefinedOnce_DelegatedTwice()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "AssetPathResolver.cs");
        Assert.Equal(1, SourceScan.Count(src, "private static string? ResolveEngineDir("));
        Assert.Contains("ResolveEngineDir(\"wwwroot\"", src);
        Assert.Contains("ResolveEngineDir(Path.Combine(\"web\", \"templates\")", src);
    }

    [Fact]
    public void ResolveTemplates_HasNoDeadPostCheck()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "AssetPathResolver.cs");
        var region = SourceScan.Region(src, "public static string? ResolveTemplates(");
        Assert.DoesNotContain("Directory.Exists(engineTemplates)", region);
        Assert.Contains("ResolveCandidates(", region);
    }

    [Fact]
    public void ResolveCandidates_FirstExistingWins_SkipsBlanks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_assetdir_" + Guid.NewGuid().ToString("N"));
        var want = Path.Combine(dir, "b");
        Directory.CreateDirectory(want);
        try
        {
            Assert.Equal(want, AssetPathResolver.ResolveCandidates([null, "", Path.Combine(dir, "missing"), want]));
            Assert.Null(AssetPathResolver.ResolveCandidates([null, "", Path.Combine(dir, "missing")]));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
