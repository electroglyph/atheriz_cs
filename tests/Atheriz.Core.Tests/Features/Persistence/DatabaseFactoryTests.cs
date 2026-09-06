using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Features.Persistence;

// DbContext factory path and guard behavior.
[Collection("Ported")]
public class DatabaseFactoryTests
{
    // --- Factory substitution + divergent paths ---

    [Fact]
    public void FactoryDefaultPath_MatchesSaveObjectsDefault()
    {
        // Create() and SaveObjects resolve the same default DB path when
        // ATHERIZ_SAVE_PATH is unset (both honor Global.SavePath), so the two
        // entry points never write different DB files.
        using var env = GlobalTestEnv.Enter();
        var origEnv = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        var origSave = AtherizSettings.Global.SavePath;
        var custom = Path.Combine(env.TempPath, "custom");
        Directory.CreateDirectory(custom);
        AtherizSettings.Global.SavePath = custom;
        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", null);
        try
        {
            using var fctx = AtherizDbContextFactory.Create();
            var cs = fctx.Database.GetConnectionString() ?? "";
            Assert.Contains(custom, cs);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", origEnv);
            AtherizSettings.Global.SavePath = origSave;
        }
    }

    [Fact]
    public void Factory_GuardViolation_ThrowsInsteadOfMemorySubstitution()
    {
        // Create() must throw on path-guard violations (InvalidOperationException)
        // instead of returning an ephemeral :memory: context where writes
        // succeed and go nowhere.
        Assert.False(GameUtils.IsInGameFolder(),
            "test must run outside a game folder so the relative-path guard fires");
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var _ = AtherizDbContextFactory.Create("relative_xyz_atheriz");
        });
    }
}
