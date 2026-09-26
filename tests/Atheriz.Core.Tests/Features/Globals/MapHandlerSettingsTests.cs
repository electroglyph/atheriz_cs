using Atheriz.Core.Globals;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// Audit 10 finding 3: MapHandler must load from the settings it was
// constructed with, not the ambient factory path. Seed a chunk through one
// SavePath and prove a handler built with those settings sees it.
[Collection("Ported")]
public sealed class MapHandlerSettingsTests
{
    [Fact]
    public void MapHandler_Load_ReadsConstructedSettingsSavePath()
    {
        using var env = GlobalTestEnv.Enter();
        // Exercised with the env override cleared (same reason as the
        // NodeHandler pinned-path test): with the override set, both sides
        // resolve to it and the pin would pass vacuously.
        var orig = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", null);
        try
        {
            var dirA = Path.Combine(env.TempPath, "settingsA");
            Directory.CreateDirectory(dirA);
            var settingsA = new AtherizSettings { SavePath = dirA, SecretPath = dirA };
            var seed = new MapHandler(autoLoad: false);
            var mi = new MapInfo { Name = "pinned-area" };
            mi.SetPreCell((0, 0), "#");
            seed.SetMapInfo("pinned-area", 0, mi);
            using (var db = new AtherizDbContext(settingsA))
            {
                db.Database.EnsureCreated();
                seed.Save(db, force: true);
            }

            var loaded = new MapHandler(settingsA);
            var got = loaded.GetMapInfo("pinned-area", 0);
            Assert.NotNull(got);
            Assert.Equal("#", got!.PreGrid[(0, 0)]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", orig);
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }
}
