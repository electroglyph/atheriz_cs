using System.Reflection;
using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Globals;

// GameTime legacy migration and alarm payload lifetime.
[Collection("Ported")]
public class GameTimeTests
{
    // --- GameTime legacy migration + alarm data lifetime ---

    [Fact]
    public void GameTime_LegacyMigration_SavesToConfiguredSavePath()
    {
        // Legacy migration saves to the configured SavePath, so migrated ticks
        // land in the right DB before the legacy file is deleted.
        using var env = GlobalTestEnv.Enter();
        var customDir = Path.Combine(env.TempPath, "custom");
        Directory.CreateDirectory(customDir);
        File.WriteAllText(Path.Combine(customDir, "time"), "{\"ticks\": 12345}");
        var settings = new AtherizSettings { SavePath = customDir };
        var gt = new GameTime(settings, autoLoad: false);
        using (var db = new AtherizDbContext(customDir)) { db.Database.EnsureCreated(); gt.Load(db); }
        Assert.Equal(12345, gt.Ticks);
        Assert.False(File.Exists(Path.Combine(customDir, "time")));
        using (var db = new AtherizDbContext(customDir))
        {
            var row = db.GameTime.FirstOrDefault(r => r.Id == 0);
            Assert.NotNull(row);
        }
    }

    [Fact]
    public void GameTime_AlarmData_OutlivesSourceDocument_OnSave()
    {
        // Alarm Data holds JsonElements cloned on the way in, so saving after
        // the source JsonDocument is disposed never throws ObjectDisposedException.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath };
        var gt = new GameTime(settings, autoLoad: false);
        var caller = GameObject.Create("alarmcaller");
        ObjectRegistry.AddObject(caller);
        // Live borrow into AddAlarm (doc alive), then kill the doc: the alarm
        // must own its copy from AddAlarm on, so Save cannot throw.
        using (var doc = JsonDocument.Parse("{\"k\":\"v\"}"))
        {
            var data = new Dictionary<string, JsonElement>();
            foreach (var p in doc.RootElement.EnumerateObject()) data[p.Name] = p.Value;
            gt.AddAlarm("1", "2", caller, false, data);
        }
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            var ex = Record.Exception(() => gt.Save(db));
            Assert.Null(ex);
        }
    }

    [Fact]
    public void GameTime_CustomMonthsPerYear_ProportionalSeasons()
    {
        // Season boundaries derive from MonthsPerYear (6x30 calendar).
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, DaysPerMonth = 30, MonthsPerYear = 6 };
        var gt = new GameTime(settings, autoLoad: false);
        // 1440 ticks per day (60s ticks, 86400s days). Day 30 of a 180-day
        // year: the old month-band code called this winter (month 2); the
        // proportional boundary (180*2/12=30) starts spring here.
        gt.Ticks = 30 * 1440;
        Assert.Equal("spring", gt.GetTime().Season);
    }

    [Fact]
    public void GameTime_DefaultCalendar_SeasonsUnchanged()
    {
        // Default 12-month calendar is unchanged (month 3 -> spring, week 1).
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath };
        var gt = new GameTime(settings, autoLoad: false);
        gt.Ticks = 60 * 1440;
        var info = gt.GetTime();
        Assert.Equal("spring", info.Season);
        Assert.Equal(1, info.WeekOfSeason);
    }

    [Fact]
    public void GameTime_OrdinalDay_TeensUseTh()
    {
        // Teen ordinals test the last two digits (111th, not 111st).
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, DaysPerMonth = 120, MonthsPerYear = 12 };
        var gt = new GameTime(settings, autoLoad: false);
        gt.Ticks = 110 * 1440; // day-of-month 111
        Assert.Contains("111th", gt.GetTime().FormattedShort);
    }

    [Fact]
    public void GameTime_StopAfterIntervalChange_RemovesStartIntervalCoro()
    {
        // Stop after a mid-run interval change removes the coro from the
        // START interval's slot instead of orphaning it there.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = true, TimeUpdateSeconds = 2 };
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        var gameTime = new GameTime(settings, ticker, pool, autoLoad: false);
        try
        {
            gameTime.Start(ticker);
            Assert.NotNull(ticker.GetSlot(2));
            settings.TimeUpdateSeconds = 5;
            gameTime.Stop(ticker);
            var slot2 = ticker.GetSlot(2);
            Assert.NotNull(slot2);
            Assert.Empty(slot2!.Coros);
            Assert.Null(ticker.GetSlot(5));
        }
        finally { try { ticker.Stop(); } catch { } }
    }

    [Fact]
    public void GameTime_StartWithNonPositiveInterval_ClampsToOneSecond()
    {
        // A non-positive interval is clamped to the 1s default, never
        // registered verbatim (which would spin a zero-delay hot loop).
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = true, TimeUpdateSeconds = 0 };
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        var gameTime = new GameTime(settings, ticker, pool, autoLoad: false);
        try
        {
            gameTime.Start(ticker);
            var slot = ticker.GetSlot(1);
            Assert.NotNull(slot);
            Assert.Single(slot!.Coros);
        }
        finally { try { gameTime.Stop(ticker); } catch { } try { ticker.Stop(); } catch { } }
    }
}
