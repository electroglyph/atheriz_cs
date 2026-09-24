using System.Diagnostics;
using System.Reflection;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Simplify;

// One verified-kill funnel (server gate, quiet kill, owner-verified
// release) for the pid-file stop path. A dropped gate would silently kill
// a foreign process.
[Collection("Ported")]
public class VerifiedKillFunnelTests
{
    private static async Task<int> KillVerifiedPid(int pid, int port, string pidFilePath)
    {
        var m = typeof(StopHandler).GetMethod("KillVerifiedPidAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        return await (Task<int>)m.Invoke(null, [pid, port, pidFilePath])!;
    }

    private static async Task<(int Code, string Output)> CaptureOut(Func<Task<int>> fn)
    {
        var orig = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try { return (await fn(), sw.ToString()); }
        finally { Console.SetOut(orig); }
    }

    [Fact]
    public async Task ForeignLivePid_RefusedWithoutSignal()
    {
        // The test host itself: live, but not a server process. The gate
        // precedes any signal, so probing our own pid is safe.
        int self = Process.GetCurrentProcess().Id;
        var (code, output) = await CaptureOut(() => KillVerifiedPid(self, 59991, Path.GetTempFileName()));
        Assert.Equal(1, code);
        Assert.Contains("not a verified Atheriz server process; refusing to terminate", output);
    }

    [Fact]
    public async Task PidFilePath_DeadPid_RemovesStaleClaim()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_killfunnel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var pidFile = Path.Combine(dir, "server.pid");
        File.WriteAllText(pidFile, (int.MaxValue - 9).ToString());
        try
        {
            var (code, output) = await CaptureOut(() => KillVerifiedPid(int.MaxValue - 9, 59992, pidFile));
            Assert.Equal(0, code);
            Assert.Contains("removing stale PID file", output);
            Assert.False(File.Exists(pidFile));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void StopPath_UsesTheFunnel()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "StopHandler.cs");
        Assert.Equal(1, SourceScan.Count(src, "internal static async Task<int> KillVerifiedPidAsync("));
        Assert.Equal(2, SourceScan.Count(src, "KillVerifiedPidAsync(")); // def + pid-file call site
        Assert.Contains("KillVerifiedPidAsync(pid.Value, port, pidFilePath)", src);
        Assert.Contains("IsProcessListeningOnPort(pid, port)", src);
        Assert.DoesNotContain("pidFilePath: null", src);
    }
}
