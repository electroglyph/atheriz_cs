using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Infrastructure;
using System.Net;
using System.Net.Sockets;

namespace Atheriz.Core.Tests.Features.Hosting;

// The captured lsof output is split once; two passes over the one array keep
// the verified-servers-first priority over any socket holder. The ss
// fallback keeps its own parse (different backend).
[Collection("Ported")]
public class PidDiscoveryPriorityTests
{
    [Fact]
    public void LsofOutput_SplitOnce_TwoPassesPreservePriority()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        // One lsof split plus the untouched ss-backend split.
        Assert.Equal(1, SourceScan.Count(src, "outp.Split('\\n'"));
        Assert.Equal(2, SourceScan.Count(src, "outp.Split"));
        Assert.Contains("var lines = outp.Split('\\n', StringSplitOptions.RemoveEmptyEntries);", src);
        Assert.Equal(2, SourceScan.Count(src, "foreach (var line in lines)"));
        // Verified-servers pass still runs before the any-holder pass.
        int verifiedAt = src.IndexOf("IsServerProcess(cand) && IsProcessListeningOnPort(cand, port)", StringComparison.Ordinal);
        Assert.True(verifiedAt >= 0);
        int holderAt = src.IndexOf("foreach (var line in lines)", verifiedAt, StringComparison.Ordinal);
        Assert.True(holderAt > verifiedAt);
        Assert.Contains("pid=", src);
    }

    [Fact]
    public void TryFindPidListeningOnPort_LoopbackListener_StillResolves()
    {
        // End-to-end through the hoisted split: a live loopback listener is
        // discovered, and a stopped one is not.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.True(PidFile.TryFindPidListeningOnPort(port, out _));
        }
        finally { listener.Stop(); }
        Assert.False(PidFile.TryFindPidListeningOnPort(port, out _));
    }
}
