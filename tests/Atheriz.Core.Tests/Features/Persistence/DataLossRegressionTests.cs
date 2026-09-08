using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Persistence;

// Data-loss regression pins.
[Collection("Ported")]
public class DataLossRegressionTests
{
    // MapHandler tombstones: Clear()+Save()+Load must not resurrect removed rows.
    [Fact] public void MapHandler_ClearSaveLoad_DoesNotResurrect()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new MapHandler(autoLoad: false);
        handler.SetMapInfo("a", 0, new MapInfo("a"));
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            handler.Save(db, force: true);
        }
        handler.Clear();
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Save(db, force: true);
        }
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Load(db);
        }
        Assert.Empty(handler.Snapshot());
    }

    // Re-added keys are live again: Clear → re-add → Save → Load keeps the row.
    [Fact] public void MapHandler_ClearReaddSaveLoad_KeepsRow()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new MapHandler(autoLoad: false);
        handler.SetMapInfo("a", 0, new MapInfo("a"));
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            handler.Save(db, force: true);
        }
        handler.Clear();
        handler.SetMapInfo("a", 0, new MapInfo("a"));
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Save(db, force: true);
        }
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Load(db);
        }
        Assert.True(handler.Snapshot().ContainsKey(("a", 0)));
    }

    // NodeGrid.Clear marks the grid dirty (NodeArea.Clear parity).
    [Fact] public void NodeGrid_Clear_MarksModified()
    {
        using var env = GlobalTestEnv.Enter();
        var grid = new NodeGrid("limbo", 0);
        grid.IsModified = false;
        grid.Clear();
        Assert.True(grid.IsModified);
    }

    // GameTime.Load preserve-live: an unexpected failure (here: disposed DB)
    // must not wipe live ticks/alarms. Pinned missing-row/corrupt paths still
    // reset to zero (see GameTimeCorruptBlobResetsTicksToZero).
    [Fact] public void GameTime_UnexpectedLoadFailure_PreservesLiveTicks()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath };
        var gt = new GameTime(settings, autoLoad: false);
        gt.Ticks = 42;
        var db = new AtherizDbContext(env.TempPath);
        db.Database.EnsureCreated();
        db.Dispose();
        gt.Load(db);
        Assert.Equal(42, gt.Ticks);
    }
}
