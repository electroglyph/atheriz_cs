using System.Reflection;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Simplify;

// One verified-kill funnel (gates, kill, owner-verified release) for the
// port-scan-found and pid-file stop paths. A dropped gate would silently kill
// a foreign process.
[Collection("Ported")]
public class VerifiedKillFunnelTests
{
    private static async Task KillVerifiedPid(int pid, int port, string? pidFilePath)
    {
        var m = typeof(StopHandler).GetMethod("KillVerifiedPidAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        await (Task)m.Invoke(null, [pid, port, pidFilePath])!;
    }

    private static async Task<string> CaptureOut(Func<Task> fn)
    {
        var orig = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try { await fn(); }
        finally { Console.SetOut(orig); }
        return sw.ToString();
    }

    [Fact]
    public async Task ScanPath_ForeignPid_RefusedWithoutSignal()
    {
        string output = await CaptureOut(() => KillVerifiedPid(int.MaxValue - 7, 59991, null));
        Assert.Contains("not a verified server; refusing to terminate", output);
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
            string output = await CaptureOut(() => KillVerifiedPid(int.MaxValue - 9, 59992, pidFile));
            Assert.Contains("removing stale PID file", output);
            Assert.False(File.Exists(pidFile));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void StopPaths_ShareTheFunnel()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "StopHandler.cs");
        Assert.Equal(1, SourceScan.Count(src, "internal static async Task KillVerifiedPidAsync("));
        Assert.Equal(3, SourceScan.Count(src, "KillVerifiedPidAsync(")); // def + 2 call sites
        Assert.Contains("KillVerifiedPidAsync(foundPid, port, pidFilePath: null)", src);
        Assert.Contains("KillVerifiedPidAsync(pid.Value, port, pidFilePath)", src);
    }
}
