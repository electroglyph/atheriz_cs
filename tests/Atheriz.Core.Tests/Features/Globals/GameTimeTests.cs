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
}
