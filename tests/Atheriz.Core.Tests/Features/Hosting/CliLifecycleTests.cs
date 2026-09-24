using Atheriz.Server.Cli;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace Atheriz.Core.Tests.Features.Hosting;

// thin CLI files get direct behavior pins — ProcessHelper terminate/escalate
// waits and RestartHandler port-wait polling (TcpListener-driven, no daemons).
[Collection("Ported")]
public class CliLifecycleTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task RequestTerminate_LiveProcess_ExitsPromptly()
    {
        if (OperatingSystem.IsWindows()) return; // SIGTERM path is POSIX-only.
        using var proc = Process.Start(new ProcessStartInfo("sleep", "60") { RedirectStandardOutput = true });
        Assert.NotNull(proc);
        try
        {
            ProcessHelper.RequestTerminate(proc);
            Assert.True(await Task.Run(() => proc.WaitForExit(5000)), "sleep survived SIGTERM");
        }
        finally { try { if (!proc.HasExited) proc.Kill(); } catch { } }
    }

    [Fact]
    public void RequestTerminate_ExitedProcess_DoesNotThrow()
    {
        using var proc = Process.Start(new ProcessStartInfo("true") { RedirectStandardOutput = true });
        Assert.NotNull(proc);
        proc.WaitForExit(5000);
        var ex = Record.Exception(() => ProcessHelper.RequestTerminate(proc));
        Assert.Null(ex);
    }

    [Fact]
    public async Task WaitForExit_ExitedProcess_ReturnsTrueImmediately()
    {
        using var proc = Process.Start(new ProcessStartInfo("true") { RedirectStandardOutput = true });
        Assert.NotNull(proc);
        proc.WaitForExit(5000);
        var sw = Stopwatch.StartNew();
        var m = typeof(ProcessHelper).GetMethod("WaitForExitAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        Assert.True(await (Task<bool>)m!.Invoke(null, [proc, TimeSpan.FromSeconds(3)])!);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitForPidExit_NonexistentPid_ReturnsPromptly()
    {
        var sw = Stopwatch.StartNew();
        await ProcessHelper.WaitForPidExitAsync(int.MaxValue);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }

    // WaitForPortStateAsync is internal (no InternalsVisibleTo in repo):
    // reflection, same pattern as StopSafetyTests.InvokeShutdownRequest.
    private static Task<bool> InvokePortWait(string name, int port, TimeSpan timeout)
    {
        var m = typeof(RestartHandler).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m);
        return (Task<bool>)m!.Invoke(null, new object[] { port, timeout })!;
    }

    [Fact]
    public async Task WaitForPortFree_FreePort_ReturnsTrue()
    {
        Assert.True(await InvokePortWait("WaitForPortFreeAsync", FreePort(), TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task WaitForPortFree_ListeningPort_ReturnsFalseAfterBound()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try
        {
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            var sw = Stopwatch.StartNew();
            Assert.False(await InvokePortWait("WaitForPortFreeAsync", port, TimeSpan.FromMilliseconds(300)));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
        }
        finally { l.Stop(); }
    }
}
