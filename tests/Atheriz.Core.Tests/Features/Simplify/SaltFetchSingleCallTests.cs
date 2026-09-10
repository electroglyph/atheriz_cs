using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// One salt fetch: the catch-retry invoked the identical read with no backoff,
// no state change and no logging between attempts, so failure throws
// identically either way. Setup behavior is covered by the setup suites.
[Collection("Ported")]
public class SaltFetchSingleCallTests
{
    [Fact]
    public void SaltFetch_HasNoCatchRetry()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "InitialSetup.cs");
        var region = SourceScan.Region(src, "// Account + character");
        Assert.Contains("string saltVal = SaltProvider.GetSalt(absSecret);", region);
        Assert.DoesNotContain("catch { saltVal", region);
        Assert.Equal(1, SourceScan.Count(region, "GetSalt(absSecret)"));
    }
}
