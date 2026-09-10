using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Regression;

// Pins for asset/pid/stop/static-file behavior.
[Collection("Ported")]
public class AssetPidStaticFileTests
{
    // The CWD-duplicating engine fallback is gone: no relative-vs-absolute
    // spelling pair reaches Distinct.
    [Fact]
    public void AssetResolver_NoCwdDuplicatingFallback()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "AssetPathResolver.cs");
        Assert.DoesNotContain("engineFallback", src);
        Assert.DoesNotContain("\"wwwroot\", \"wwwroot\"", src);
    }

    // No "already starting" age gate survives: a live PID returns
    // "already running" above, so anything reaching the stale path is
    // deleted and retried. Behavior: a fresh-stale pid file is replaced.
    [Fact]
    public void PidFile_NoStartingAgeBranch()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.DoesNotContain("already starting", src);
        var dir = Path.Combine(Path.GetTempPath(), "assetpfx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "server.pid"), int.MaxValue.ToString());
            bool ok = PidFile.TryAcquire(dir, out var pf, out var reason, webserverPort: 59998);
            try { Assert.True(ok, reason); }
            finally { try { pf?.Dispose(); } catch { } }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // The pid-file stop path routes through the shared kill helper instead
    // of inlining its own terminate/wait/kill sequence.
    [Fact]
    public void StopPidPath_UsesSharedKillHelper()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "StopHandler.cs");
        Assert.Contains("KillProcessWithDots(proc)", src);
        Assert.DoesNotContain("WaitForExitDotsAsync(proc, 50)", src);
    }

    // The content-hash cache check compiles once, not per static-file request.
    [Fact]
    public void StaticFile_HashRegexHoisted()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "StaticFileConfig.cs");
        Assert.Contains("static readonly Regex", src);
        Assert.DoesNotContain("Regex.IsMatch(path", src);
    }
}
