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

    // The pid-file stop path routes through the shared quiet terminate
    // helper instead of inlining its own terminate/wait/kill sequence.
    [Fact]
    public void StopPidPath_UsesSharedKillHelper()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "StopHandler.cs");
        Assert.Contains("ProcessHelper.TerminateAsync(proc)", src);
        Assert.DoesNotContain("KillProcessWithDots", src);
    }

    // The content-hash cache check is source-generated, not compiled per
    // request and not newed per request.
    [Fact]
    public void StaticFile_HashRegexGenerated()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Hosting", "StaticFileConfig.cs");
        Assert.Contains("[GeneratedRegex(", src);
        Assert.Contains("partial Regex HashedBundlePattern()", src);
        Assert.DoesNotContain("new Regex(", src);
    }

    // No root index.html may live in wwwroot: a stale Draw build committed
    // there was once served at "/" instead of the landing page. The Draw
    // entry lives at atheriz_draw/index.html; the landing page is the
    // web/templates/index.html template. (Belt and braces beside the
    // behavioral pin — the "/" route no longer consults wwwroot at all, so
    // a stray file could only surface inertly under /static/index.html.)
    [Fact]
    public void Wwwroot_HasNoRootIndexHtml()
    {
        var stray = Path.Combine(SourceScan.RepoRoot(), "src", "Atheriz.Server", "wwwroot", "index.html");
        Assert.False(File.Exists(stray), "wwwroot/index.html must not exist — Draw entry is atheriz_draw/index.html, landing is web/templates/index.html");
    }
}
