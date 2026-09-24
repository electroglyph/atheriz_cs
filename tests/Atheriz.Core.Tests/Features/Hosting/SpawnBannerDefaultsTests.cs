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
        // The banner shows the same values through Default as through a
        // fresh instance, so the shared read changes nothing displayed.
        var fresh = new AtherizSettings();
        Assert.Equal(fresh.WebserverPort, AtherizSettings.Default.WebserverPort);
        Assert.Equal(fresh.WebserverInterface, AtherizSettings.Default.WebserverInterface);
        Assert.Equal(fresh.WebsocketEnabled, AtherizSettings.Default.WebsocketEnabled);
    }
}
