using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Infrastructure;
using System.Net;
using System.Net.Sockets;

namespace Atheriz.Core.Tests.Features.Hosting;

// Port liveness is a single cross-platform socket-table read; per-pid
// attribution stays a small Linux-only /proc read for the stop gate.
// Listener discovery by scan exists only for the explicit -f/--force
// path: without force, a listening port without a pid-file owner is
// reported, never killed — and the verified kill never scans.
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
    public void PidFile_ListenerScanSurface_ForcePathOnly()
    {
        // The scan surface exists for -f/--force, but the verified kill
        // must never use it: exactly one reference in StopHandler, inside
        // the force-only KillPortListenersAsync.
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.Contains("FindPidsListeningOnPort", src);
        var stop = SourceScan.Read("src", "Atheriz.Server", "Cli", "StopHandler.cs");
        Assert.Equal(1, SourceScan.Count(stop, "FindPidsListeningOnPort"));
        Assert.Contains("KillPortListenersAsync", stop);
    }
}
