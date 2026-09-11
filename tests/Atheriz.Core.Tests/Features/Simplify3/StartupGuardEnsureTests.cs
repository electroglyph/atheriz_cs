using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Both startup paths share one guard/ensure preamble: same guards, same
// messages, same exit codes. Pid-claim and host validation stay per-caller.
[Collection("Ported")]
public class StartupGuardEnsureTests
{
    [Fact]
    public void GuardSavePath_RelativePath_ThrowsWithSaveMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PathGuards.GuardSavePath("rel-save"));
        Assert.Contains("SAVE_PATH", ex.Message);
    }

    [Fact]
    public void GuardSecretPath_RelativePath_ThrowsWithSecretMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PathGuards.GuardSecretPath("rel-secret"));
        Assert.Contains("SECRET_PATH", ex.Message);
    }

    [Fact]
    public void EnsureSaveDirectory_RelativePath_FailsOnSameGuard()
    {
        var guard = Assert.Throws<InvalidOperationException>(() => PathGuards.GuardSavePath("rel-save"));
        var ensure = Assert.Throws<InvalidOperationException>(() => PathGuards.EnsureSaveDirectory("rel-save"));
        Assert.Equal(guard.Message, ensure.Message);
    }

    [Fact]
    public void EnsureSecretDirectory_RelativePath_FailsOnSameGuard()
    {
        var guard = Assert.Throws<InvalidOperationException>(() => PathGuards.GuardSecretPath("rel-secret"));
        var ensure = Assert.Throws<InvalidOperationException>(() => PathGuards.EnsureSecretDirectory("rel-secret"));
        Assert.Equal(guard.Message, ensure.Message);
    }

    [Fact]
    public void Program_CallsSharedEnsureFromBothPaths()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Program.cs");
        Assert.Equal(1, SourceScan.Count(src, "static void EnsureDirsOrExit(AtherizSettings s)"));
        Assert.Contains("GuardSavePath(s.SavePath)", src);
        Assert.Contains("GuardSecretPath(s.SecretPath)", src);
        Assert.Contains("EnsureSaveDirectory(s.SavePath)", src);
        Assert.Contains("EnsureSecretDirectory(s.SecretPath)", src);
        Assert.Contains("EnsureDirsOrExit(effSpawn)", src);
        Assert.Contains("EnsureDirsOrExit(settings)", src);
        Assert.DoesNotContain("GuardSavePath(effSpawn.SavePath)", src);
        Assert.DoesNotContain("GuardSavePath(settings.SavePath)", src);
        // Per-caller behavior stays out of the helper.
        Assert.Contains("IsSafeHost(hostOverride)", src);
        Assert.Equal(2, SourceScan.Count(src, "PidFile.TryAcquire("));
    }
}
