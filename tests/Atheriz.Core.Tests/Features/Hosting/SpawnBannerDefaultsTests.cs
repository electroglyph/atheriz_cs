using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;
using System.Text.RegularExpressions;

namespace Atheriz.Core.Tests.Features.Hosting;

// The post-spawn banner reads its display defaults from the shared
// AtherizSettings.Default instance instead of allocating one per spawn.
[Collection("Ported")]
public class SpawnBannerDefaultsTests
{
    [Fact]
    public void BannerDefaults_UseSharedDefaultInstance()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "DaemonSpawner.cs");
        Assert.Contains("AtherizSettings.Default", src);
        Assert.DoesNotContain("new AtherizSettings()", src);
        // Reads only: no assignment through the shared instance (which would
        // poison every later borrower). Mirrors SettingsDefault_NeverMutated.
        var rx = new Regex(@"shippedDefaults\.[A-Za-z_][A-Za-z0-9_]*\s*=(?![=>])", RegexOptions.None);
        Assert.DoesNotMatch(rx, src);
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
