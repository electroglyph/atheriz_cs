using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Infrastructure;
using System.Net;
using System.Net.Sockets;

namespace Atheriz.Core.Tests.Features.Hosting;

// Port liveness is a single cross-platform socket-table read; per-pid
// attribution stays a small Linux-only /proc read for the stop gate.
// Listener discovery by scan (lsof/ss subprocesses) is gone: a listening
// port without a pid-file owner is reported, never killed.
[Collection("Ported")]
public class PidDiscoveryPriorityTests
{
    [Fact]
    public void IsPortListening_LoopbackListener_ResolvesAndClears()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.True(PidFile.IsPortListening(port));
        }
        finally { listener.Stop(); }
        Assert.False(PidFile.IsPortListening(port));
    }

    [Fact]
    public void PidFile_HasNoListenerScanSurface()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.DoesNotContain("\"lsof\"", src);
        Assert.DoesNotContain("TryFindPidListeningOnPort", src);
        Assert.DoesNotContain("ReadHelperOutput", src);
    }
}
