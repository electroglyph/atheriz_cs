using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Hosting;

// The startup banners read the resolved settings instance (never the
// shared Default, which reads must not mutate).
[Collection("Ported")]
public class SpawnBannerDefaultsTests
{
    [Fact]
    public void BannerDefaults_UseResolvedSettings()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "ServerHost.cs");
        Assert.Contains("PrintBanners(settings)", src);
        Assert.DoesNotContain("AtherizSettings.Default", src);
    }

    [Fact]
    public void BannerDefaults_MatchFreshInstance()
    {
        // The banner shows fresh-instance values (no shared singleton reads).
        var lines = Atheriz.Server.Hosting.ServerHost.FormatBannerLines(new AtherizSettings());
        Assert.Contains("Web server listening on http://0.0.0.0:9999", lines);
    }
}
