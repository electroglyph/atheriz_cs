using System.Reflection;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Persistence;

// Both EnsureCreated paths share one WAL-fallback wrapper; gates are
// untouched, the factory never calls it, and pragma failures stay loud.
[Collection("Ported")]
public class WalFallbackWrapperTests
{
    [Fact]
    public void TryApplyWalPragmas_PragmaFailure_LogsFallbackErrorsWithoutThrowing()
    {
        using var env = GlobalTestEnv.Enter();
        var db = new AtherizDbContext(env.TempPath);
        db.EnsureCreated();
        db.Dispose();
        var m = typeof(AtherizDbContext).GetMethod("TryApplyWalPragmas", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(m);
        string log;
        using (var cap = new CaptureAtherizLog())
        {
            var ex = Record.Exception(() => m!.Invoke(db, null));
            log = cap.Read();
            Assert.Null(ex);
        }
        Assert.Contains("WAL pragmas failed", log);
        Assert.Contains("WAL fallback pragmas failed", log);
        Assert.DoesNotContain("WAL pragma fallback:", log);
    }

    [Fact]
    public void EnsureCreated_BothPaths_ShareSingleWrapper()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Persistence", "AtherizDbContext.cs");
        Assert.Equal(1, SourceScan.Count(src, "private void TryApplyWalPragmas()"));
        Assert.Equal(2, SourceScan.Count(src, "TryApplyWalPragmas();"));
        Assert.Equal(1, SourceScan.Count(src, "WAL pragma fallback:"));
        var factory = SourceScan.Read("src", "Atheriz.Core", "Persistence", "AtherizDbContextFactory.cs");
        Assert.DoesNotContain("TryApplyWalPragmas", factory);
    }

    [Fact]
    public void EnsureCreated_SyncPath_ReleasesGate()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = new AtherizDbContext(env.TempPath);
        db.EnsureCreated();
        Assert.False(DbWriteGate.IsHeld);
    }
}
