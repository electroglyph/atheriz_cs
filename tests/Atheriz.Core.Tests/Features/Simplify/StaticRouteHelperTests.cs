using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// One no-cache trio, one first-existing-file helper, single draw/webclient
// alias registrations (duplicate slash/no-slash patterns threw
// AmbiguousMatchException), default documents for directory URLs, and one
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
    public void EntryFiles_SingleRoot_NoTemplateFallback()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "StaticFileConfig.cs");
        Assert.DoesNotContain("templatesCandidate", src);
        Assert.DoesNotContain("FirstExisting(", src);
        Assert.Contains("staticCandidate", src);
    }

    [Fact]
    public void DrawAliases_SingleRegistrations_ImmutableMerged()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "StaticFileConfig.cs");
        // No slash/no-slash duplicate route pairs (AmbiguousMatchException);
        // the served spellings are pinned behaviorally by ServerHostingDirectTests.
        Assert.DoesNotContain("foreach (var route in", src);
        Assert.Contains("app.MapGet(\"/atheriz_draw\", ServeDraw);", src);
        Assert.Contains("app.MapGet(\"/atheriz_draw/index.html\", ServeDraw);", src);
        Assert.Contains("app.MapGet(\"/webclient\", () => Results.Redirect(\"/webclient/index.html\"));", src);
        Assert.Contains("app.UseDefaultFiles(", src);
        Assert.Contains("||", SourceScan.Region(src, "bool immutable ="));
        Assert.Contains("public, max-age=31536000, immutable", src);
        Assert.Contains("public, max-age=86400", src);
    }
}
