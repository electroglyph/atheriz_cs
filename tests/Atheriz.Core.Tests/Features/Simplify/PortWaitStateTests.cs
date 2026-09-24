using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Simplify;

// One bounded quiet poll for a port to reach a state; the free wrapper is
// a thin polarity adapter over the shared core.
[Collection("Ported")]
public class PortWaitStateTests
{
    private static async Task<bool> WaitForPortState(int port, TimeSpan timeout, bool wantUp)
    {
        var m = typeof(RestartHandler).GetMethod("WaitForPortStateAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<bool>)m.Invoke(null, [port, timeout, wantUp])!;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task FreePort_IsFree_NotUp()
    {
        int port = FreePort();
        Assert.True(await WaitForPortState(port, TimeSpan.FromSeconds(1), wantUp: false));
        Assert.False(await WaitForPortState(port, TimeSpan.FromMilliseconds(200), wantUp: true));
    }

    [Fact]
    public async Task FreeWrapper_MatchesCorePolarity()
    {
        int port = FreePort();
        var m = typeof(RestartHandler).GetMethod("WaitForPortFreeAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True(await (Task<bool>)m.Invoke(null, [port, TimeSpan.FromSeconds(1)])!);
    }

    [Fact]
    public void FreeWrapper_IsThinPolarityAdapter()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "RestartHandler.cs");
        Assert.Equal(1, SourceScan.Count(src, "internal static async Task<bool> WaitForPortStateAsync("));
        Assert.Contains("wantUp: false", src);
    }
}
