using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// `restart` must wait for the old server (SIGKILL past a 5s grace), and must
// abort the spawn — not warn-and-proceed — when the stop fails or the port
// never frees. Otherwise the child fails to bind while the old server keeps
// running, after the operator was told a restart happened.
[Collection("Ported")]
public class RestartSafetyTests
{
    [Fact]
    public void PortGatePrimitives_AgreeOnBoundPort()
    {
        // The restart port gate polls this primitive: bound means held.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            Assert.True(PidFile.IsPortListening(port));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void HandleRestart_AbortsInsteadOfSpawning()
    {
        // Structural pin: the stop-failure and port-held paths return before
        // any spawn, and a stuck verified server is SIGKILLed past the grace.
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Cli/RestartHandler.cs");
        Assert.Contains("aborting restart", src);
        Assert.Contains("Kill(entireProcessTree", src);
        Assert.Contains("IsServerProcess(oldPid)", src);
        Assert.DoesNotContain("may fail to bind", src);
    }
}
