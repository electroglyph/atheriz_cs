using Atheriz.Core;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Server.Cli;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// The engine checkout itself is not a game folder, so engine-side writers
// must never plant a save directory in the caller's working directory:
// `new`/`reset` setup logs used to land in the checkout root, test-run
// logs in the test host folder, and a `reset` from the root left
// save/.wipe-lock behind.
[Collection("Ported")]
public class RootSaveFolderTests
{
    [Fact]
    public void Logger_RelativeSavePathOutsideGameFolder_WritesNoDirectory()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.False(Atheriz.Core.Utils.GameUtils.IsInGameFolder());
        var rel = "atheriz_noroot_" + Guid.NewGuid().ToString("N");
        var full = Path.GetFullPath(rel);
        Assert.False(Directory.Exists(full));
        try
        {
            AtherizLogger.ApplySettings(new AtherizSettings { SavePath = rel });
            AtherizLogger.LogInformation("noroot probe line");
            Assert.False(Directory.Exists(full));
        }
        finally
        {
            AtherizLogger.ApplySettings(new AtherizSettings { SavePath = env.TempPath });
            try { if (Directory.Exists(full)) Directory.Delete(full, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WipeLock_RelativeSavePathOutsideGameFolder_CreatesNothing()
    {
        using var env = GlobalTestEnv.Enter();
        _ = env;
        Assert.False(Atheriz.Core.Utils.GameUtils.IsInGameFolder());
        var rel = "atheriz_noroot_" + Guid.NewGuid().ToString("N");
        var full = Path.GetFullPath(rel);
        Assert.False(Directory.Exists(full));
        var handle = PidFile.TryAcquireWipeLock(rel);
        try
        {
            Assert.Null(handle);
        }
        finally
        {
            try { handle?.Dispose(); } catch { }
        }
        Assert.False(Directory.Exists(full));
    }

    [Fact]
    public async Task Takeover_RelativeSavePathOutsideGameFolder_RefusesWithoutDirectory()
    {
        using var env = GlobalTestEnv.Enter();
        _ = env;
        Assert.False(Atheriz.Core.Utils.GameUtils.IsInGameFolder());
        var rel = "atheriz_noroot_" + Guid.NewGuid().ToString("N");
        var full = Path.GetFullPath(rel);
        Assert.False(Directory.Exists(full));
        var settings = new AtherizSettings { SavePath = rel };
        var rc = await TakeoverHandler.TakeoverAsync(
            settings, parentPid: -1,
            waitParent: (_, _) => Task.FromResult(true),
            waitPortFree: (_, _) => Task.FromResult(true),
            start: (_, _, _) => Task.FromResult(0));
        Assert.Equal(1, rc);
        Assert.False(Directory.Exists(full));
    }

    [Fact]
    public void Setup_RelativeSavePathOutsideGameFolder_RefusesWithoutDirectories()
    {
        using var env = GlobalTestEnv.Enter();
        _ = env;
        Assert.False(Atheriz.Core.Utils.GameUtils.IsInGameFolder());
        var rel = "atheriz_noroot_" + Guid.NewGuid().ToString("N");
        var full = Path.GetFullPath(rel);
        Assert.False(Directory.Exists(full));
        Assert.Throws<InvalidOperationException>(
            () => InitialSetup.RunSetup(new SetupOptions(rel, Prompt: false)));
        Assert.False(Directory.Exists(full));
    }
}
