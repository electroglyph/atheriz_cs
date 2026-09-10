using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// The three GameTime load-reset paths (missing row, corrupt row, null payload)
// share one zeroing helper: each must clear live ticks AND alarms.
[Collection("Ported")]
public class GameTimeLoadResetTests
{
    private static void Reset()
    {
        ObjectRegistry.ClearAll();
        GlobalServices.Reset();
        MapEdit.Reset();
    }

    private static GameTime LiveGameTime(string dir, long ticks)
    {
        var settings = new AtherizSettings { SavePath = dir };
        var gt = new GameTime(settings, autoLoad: false);
        gt.Ticks = ticks;
        var caller = GameObject.Create("resetwatcher");
        ObjectRegistry.AddObject(caller);
        gt.AddAlarm("3", "4", caller);
        Assert.NotEmpty(gt.SnapshotAlarms());
        return gt;
    }

    [Fact]
    public void Load_MissingRow_ResetsTicksAndAlarms()
    {
        // No gametime row and no legacy file: live state zeroes out loudly.
        Reset();
        using var env = GlobalTestEnv.Enter();
        try
        {
            var gt = LiveGameTime(env.TempPath, 99);
            using (var db = new AtherizDbContext(env.TempPath))
            {
                db.Database.EnsureCreated();
                gt.Load(db);
            }
            Assert.Equal(0, gt.Ticks);
            Assert.Empty(gt.SnapshotAlarms());
        }
        finally { Reset(); }
    }

    [Fact]
    public void Load_CorruptRow_ResetsTicksAndAlarms()
    {
        // An undecodable gametime row zeroes out instead of preserving stale live state.
        Reset();
        using var env = GlobalTestEnv.Enter();
        try
        {
            var gt = LiveGameTime(env.TempPath, 77);
            using (var db = new AtherizDbContext(env.TempPath))
            {
                db.Database.EnsureCreated();
                db.GameTime.Add(new GameTimeRow { Id = 0, Data = "not-json{{{ " });
                db.SaveChanges();
                gt.Load(db);
            }
            Assert.Equal(0, gt.Ticks);
            Assert.Empty(gt.SnapshotAlarms());
        }
        finally { Reset(); }
    }

    [Fact]
    public void Load_NullPayload_ResetsTicksAndAlarms()
    {
        // A row decoding to null zeroes out like the missing/corrupt paths.
        Reset();
        using var env = GlobalTestEnv.Enter();
        try
        {
            var gt = LiveGameTime(env.TempPath, 55);
            using (var db = new AtherizDbContext(env.TempPath))
            {
                db.Database.EnsureCreated();
                db.GameTime.Add(new GameTimeRow { Id = 0, Data = "null" });
                db.SaveChanges();
                gt.Load(db);
            }
            Assert.Equal(0, gt.Ticks);
            Assert.Empty(gt.SnapshotAlarms());
        }
        finally { Reset(); }
    }
}
