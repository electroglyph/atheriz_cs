using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;
using Atheriz.Server.Hosting;

namespace Atheriz.Core.Tests.Features.Hosting;

// Background launching: the host gate, the shared banner text, and the
// child-neutralization shape. The real detach is never invoked here —
// setsid in the test host would detach the test runner itself.
[Collection("Ported")]
public sealed class DaemonSpawnerTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("::1", true)]
    [InlineData("bad host!", false)]
    [InlineData("", false)]
    [InlineData("localhost!", false)]
    public void IsSafeHost_MatchesForegroundGate(string? host, bool expected)
    {
        Assert.Equal(expected, DaemonSpawner.IsSafeHost(host));
    }

    [Fact]
    public async Task IsSafeHost_AgreesWithForegroundRejection()
    {
        // Same rule and same fail-fast exit code as the top of
        // ServerHost.RunForegroundAsync (which delegates to the helper).
        Assert.False(DaemonSpawner.IsSafeHost("bad host!"));
        Assert.Equal(2, await ServerHost.RunForegroundAsync(null, "bad host!", null));
    }

    [Fact]
    public void BannerFormatter_ListsListeningLines()
    {
        var lines = ServerHost.FormatBannerLines(new AtherizSettings
        {
            WebserverInterface = "0.0.0.0",
            WebserverPort = 9999,
            SslCertFile = null,
            SslKeyFile = null,
        });
        Assert.Contains("Web server listening on http://0.0.0.0:9999", lines);
        Assert.Contains("WebSocket server available at ws://0.0.0.0:9999/ws", lines);
        Assert.Contains("SSL is disabled (set SSL_CERTFILE to enable)", lines);
    }

    [Fact]
    public void BannerFormatter_OmitsWebSocketWhenDisabled()
    {
        var lines = ServerHost.FormatBannerLines(new AtherizSettings
        {
            WebsocketEnabled = false,
            SslCertFile = null,
            SslKeyFile = null,
        });
        Assert.DoesNotContain(lines, l => l.Contains("/ws"));
    }

    [Fact]
    public void BannerFormatter_BracketsIpv6Host()
    {
        var lines = ServerHost.FormatBannerLines(new AtherizSettings
        {
            WebserverInterface = "::1",
            SslCertFile = null,
            SslKeyFile = null,
        });
        Assert.Contains(lines, l => l.Contains("[::1]"));
    }

    [Fact]
    public void ChildNeutralization_StaysSilentInDaemon()
    {
        // The detached child prints nothing itself: banners gated, MEL
        // console providers cleared, detach attempted before the builder.
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "ServerHost.cs");
        Assert.Contains("if (!daemon) PrintBanners(settings);", src);
        Assert.Contains("builder.Logging.ClearProviders();", src);
        Assert.Contains("DaemonDetach.TryDetachThisProcess();", src);
    }

    [Fact]
    public void DetachFailure_IsLoggedNeverFatal()
    {
        // Neuter setsid and the server still boots attached: every step is
        // guarded with a recorded fallback, never a throw.
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "DaemonDetach.cs");
        Assert.Contains("continuing attached", src);
        Assert.Contains("TryDetachThisProcess", src);
    }

    [Fact]
    public void RivalRefusal_NoClaimant_ReturnsNull()
    {
        Assert.Null(DaemonSpawner.RivalRefusal(null, parentPid: 100, childPid: 200));
    }

    [Fact]
    public void RivalRefusal_OwnChild_ReturnsNull()
    {
        // Our own still-booting child holds a live claim while slow-booting:
        // not ready, never a rival starter.
        Assert.Null(DaemonSpawner.RivalRefusal(200, parentPid: 100, childPid: 200));
    }

    [Fact]
    public void RivalRefusal_OwnParent_ReturnsNull()
    {
        Assert.Null(DaemonSpawner.RivalRefusal(100, parentPid: 100, childPid: 200));
    }

    [Fact]
    public void RivalRefusal_GenuineRival_NamesPid()
    {
        var refusal = DaemonSpawner.RivalRefusal(300, parentPid: 100, childPid: 200);
        Assert.NotNull(refusal);
        Assert.Contains("300", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForReady_SlowChild_EventuallyReady()
    {
        // The incident shape with a mocked clock: the child's claim and port
        // arrive late (slower than any fast boot) but inside the timeout —
        // the spawner waits instead of refusing.
        int polls = 0;
        var (ready, died) = await DaemonSpawner.WaitForReadyAsync(
            readClaim: () => ++polls >= 3 ? 200 : (int?)null,
            childPid: 200,
            portOpen: () => polls >= 5,
            childAlive: () => true,
            timeout: TimeSpan.FromSeconds(5),
            cadence: TimeSpan.FromMilliseconds(1),
            CancellationToken.None);
        Assert.True(ready);
        Assert.False(died);
    }

    [Fact]
    public async Task WaitForReady_OwnChildNeverReady_ReturnsNotReadyNotDied()
    {
        // Own child holds a live claim but the port never opens: not ready
        // (the caller prints the log tail), and RivalRefusal stays silent
        // for it — never a rival report.
        var (ready, died) = await DaemonSpawner.WaitForReadyAsync(
            readClaim: () => 200,
            childPid: 200,
            portOpen: () => false,
            childAlive: () => true,
            timeout: TimeSpan.FromMilliseconds(50),
            cadence: TimeSpan.FromMilliseconds(1),
            CancellationToken.None);
        Assert.False(ready);
        Assert.False(died);
        Assert.Null(DaemonSpawner.RivalRefusal(200, parentPid: 100, childPid: 200));
    }

    [Fact]
    public async Task WaitForReady_DeadChild_ReturnsDied()
    {
        var (ready, died) = await DaemonSpawner.WaitForReadyAsync(
            readClaim: () => null,
            childPid: 200,
            portOpen: () => false,
            childAlive: () => false,
            timeout: TimeSpan.FromSeconds(5),
            cadence: TimeSpan.FromMilliseconds(1),
            CancellationToken.None);
        Assert.False(ready);
        Assert.True(died);
    }

    [Fact]
    public async Task WaitForReady_RivalClaim_NeverMatches()
    {
        // A rival starter's claim (different pid) with an open port must not
        // satisfy the poll; the timeout expires and RivalRefusal names it.
        var (ready, died) = await DaemonSpawner.WaitForReadyAsync(
            readClaim: () => 300,
            childPid: 200,
            portOpen: () => true,
            childAlive: () => true,
            timeout: TimeSpan.FromMilliseconds(50),
            cadence: TimeSpan.FromMilliseconds(1),
            CancellationToken.None);
        Assert.False(ready);
        Assert.False(died);
        Assert.NotNull(DaemonSpawner.RivalRefusal(300, parentPid: 100, childPid: 200));
    }
}
