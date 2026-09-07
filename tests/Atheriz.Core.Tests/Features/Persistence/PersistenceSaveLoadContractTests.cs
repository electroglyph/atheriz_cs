using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Features.Persistence;

// Instance-path and crash-journal contracts for world saves.
[Collection("Ported")]
public class PersistenceSaveLoadContractTests
{
    // --- Parameterless Save honors the instance path ---

    [Fact]
    public void GameTime_ParameterlessSave_UsesDefaultSavePath()
    {
        // Parameterless Save resolves the process-default path
        // (ATHERIZ_SAVE_PATH else Global.SavePath, like SaveObjects) — never
        // a divergent file. Per-instance targeting uses Save(db); the
        // instance settings must not reroute the parameterless call.
        using var env = GlobalTestEnv.Enter();
        var origEnv = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        var origSave = AtherizSettings.Global.SavePath;
        var customDir = Path.Combine(env.TempPath, "custom");
        var globalDir = Path.Combine(env.TempPath, "global");
        Directory.CreateDirectory(customDir);
        Directory.CreateDirectory(globalDir);
        AtherizSettings.Global.SavePath = globalDir;
        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", null);
        try
        {
            var settings = new AtherizSettings { SavePath = customDir };
            var gt = new GameTime(settings, autoLoad: false);
            gt.Ticks = 42;
            gt.Save();
            using var db = new AtherizDbContext(globalDir);
            var probe = new GameTime(settings, autoLoad: false);
            probe.Load(db);
            Assert.Equal(42, probe.Ticks);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", origEnv);
            AtherizSettings.Global.SavePath = origSave;
        }
    }

    [Fact]
    public void MapHandler_ParameterlessSave_UsesDefaultSavePath()
    {
        // Same process-default contract as GameTime.Save: the parameterless
        // call resolves ATHERIZ_SAVE_PATH else Global.SavePath; per-instance
        // targeting uses Save(db).
        using var env = GlobalTestEnv.Enter();
        var origEnv = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        var origSave = AtherizSettings.Global.SavePath;
        var customDir = Path.Combine(env.TempPath, "custom");
        var globalDir = Path.Combine(env.TempPath, "global");
        Directory.CreateDirectory(customDir);
        Directory.CreateDirectory(globalDir);
        AtherizSettings.Global.SavePath = globalDir;
        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", null);
        try
        {
            var settings = new AtherizSettings { SavePath = customDir };
            var mh = new MapHandler(settings, autoLoad: false);
            mh.SetMapInfo("zona", 0, new MapInfo());
            mh.Save();
            using var db = new AtherizDbContext(globalDir);
            var probe = new MapHandler(settings, autoLoad: false);
            probe.Load(db);
            Assert.NotNull(probe.GetMapInfo("zona", 0));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", origEnv);
            AtherizSettings.Global.SavePath = origSave;
        }
    }

    // --- Legacy migration targets the caller's database ---

    [Fact]
    public void GameTime_LegacyMigration_WritesToCallerDatabase()
    {
        // Loading from a caller-supplied context must migrate the legacy time
        // file into THAT database (GameTime.cs:213-217), not the instance's
        // configured file path, so in-memory/test callers observe the result.
        using var env = GlobalTestEnv.Enter();
        var fileDir = Path.Combine(env.TempPath, "filedb");
        Directory.CreateDirectory(fileDir);
        File.WriteAllText(Path.Combine(fileDir, "time"), "{\"ticks\": 777, \"alarms\": {}}");
        var settings = new AtherizSettings { SavePath = fileDir };
        var gt = new GameTime(settings, autoLoad: false);
        using var memDb = AtherizDbContextFactory.CreateForTests();
        gt.Load(memDb);
        Assert.Equal(777, gt.Ticks);
        var row = memDb.GameTime.AsNoTracking().FirstOrDefault(r => r.Id == 0);
        Assert.NotNull(row);
    }

    // --- Retry success marks the checkpoint clean ---

    private sealed class FlakyMapHandler : MapHandler
    {
        private int _calls;
        public FlakyMapHandler(AtherizSettings settings) : base(settings, autoLoad: false) { }
        public override void Save(AtherizDbContext db, bool force = false)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new InvalidOperationException("injected first-attempt map failure");
            base.Save(db, force);
        }
    }

    [Fact]
    public void SaveWorld_RetrySuccess_MarksCheckpointClean()
    {
        // A first-attempt map failure followed by a successful retry must still
        // mark the checkpoint clean (StartStop.cs:456-494): a success is a
        // success, so the next boot must not report a false torn checkpoint.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = false, AutosaveMinutes = 0, AutosaveOnShutdown = true };
        var flaky = new FlakyMapHandler(settings);
        flaky.SetMapInfo("zona", 0, new MapInfo());
        GlobalServices.SetMapHandler(flaky);
        CheckpointJournal.MarkClean(env.TempPath);
        StartStop.DoShutdown(settings);
        try
        {
            Assert.False(CheckpointJournal.IsDirty(env.TempPath));
            using var db = new AtherizDbContext(env.TempPath);
            Assert.NotEmpty(db.MapData.Where(r => r.Area == "zona").ToList());
        }
        finally { try { StartStop.ResetForTesting(); } catch { } }
    }
}
