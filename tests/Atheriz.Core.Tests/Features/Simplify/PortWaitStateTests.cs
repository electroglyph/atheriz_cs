using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Simplify;

// One bounded tenth-second poll for a port to reach a listening state; the
// free/up pair differs only in polarity (same probe count, cadence, bounds).
[Collection("Ported")]
public class PortWaitStateTests
{
    private static async Task<bool> WaitForPortState(int port, int tenths, bool wantUp)
    {
        var m = typeof(RestartHandler).GetMethod("WaitForPortStateAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<bool>)m.Invoke(null, [port, tenths, wantUp])!;
    }

    private static async Task<bool> Wrapper(string name, int port, int tenths)
    {
        var m = typeof(RestartHandler).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<bool>)m.Invoke(null, [port, tenths])!;
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
        Assert.True(await WaitForPortState(port, 3, wantUp: false));
        Assert.False(await WaitForPortState(port, 2, wantUp: true));
    }

    [Fact]
    public async Task Wrappers_MatchCorePolarity()
    {
        int port = FreePort();
        Assert.True(await Wrapper("WaitForPortFreeAsync", port, 3));
        Assert.False(await Wrapper("WaitForPortUpAsync", port, 2));
    }

    [Fact]
    public void Wrappers_AreThinPolarityAdapters()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "RestartHandler.cs");
        Assert.Equal(1, SourceScan.Count(src, "internal static async Task<bool> WaitForPortStateAsync("));
        Assert.Contains("wantUp: false", src);
        Assert.Contains("wantUp: true", src);
    }
}
