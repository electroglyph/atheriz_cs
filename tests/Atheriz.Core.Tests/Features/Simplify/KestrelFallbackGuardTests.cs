using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// One fail-closed core for both cert-failure paths: a configured cert that
// cannot serve must never silently serve plaintext holding the admin token.
// Paths keep their own messages and warning prints.
[Collection("Ported")]
public class KestrelFallbackGuardTests
{
    [Fact]
    public void FallbackDecision_LivesInOneHelper()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "KestrelConfig.cs");
        Assert.Equal(1, SourceScan.Count(src, "void ThrowIfNoFallbackOrWarn("));
        Assert.Equal(2, SourceScan.Count(src, "ThrowIfNoFallbackOrWarn(") - 1);
        Assert.Equal(1, SourceScan.Count(src, "void ConfigureEndpoint("));
    }

    [Fact]
    public void BothFailurePaths_StayFailClosed_WithOwnMessages()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "KestrelConfig.cs");
        Assert.Contains("SSL cert configured but not found", src);
        Assert.Contains("SSL cert configured but unloadable", src);
        Assert.Contains("refusing insecure fallback", src);
        Assert.Contains("WARNING: SSL cert file not found", src);
        Assert.Contains("SSL load failed for", src);
    }
}
