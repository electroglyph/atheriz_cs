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
using Atheriz.Core.Utils;
using Atheriz.Server.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Features.Persistence;

// World loading: corrupt-row reporting and load-then-swap semantics.
[Collection("Ported")]
public class WorldLoadingTests
{
    // --- Silent load data loss ---

    [Fact]
    public void LoadObjects_CorruptRow_IsReported_NotSwallowed()
    {
        // DTO parse / FromDto / ResolveRelations failures are reported with
        // the corrupt row id in the log instead of vanishing silently.
        using var env = GlobalTestEnv.Enter();
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            db.Objects.Add(new ObjectRow { Id = 999991, Type = "object", Version = 1, Data = "{not valid json" });
            db.SaveChanges();
        }
        ObjectRegistry.ClearAll();
        string log;
        using (var cap = new CaptureAtherizLog())
        {
            ObjectRegistry.LoadObjects(env.TempPath);
            log = cap.Read();
        }
        Assert.Contains("999991", log);
    }

    // --- MapHandler.Load merges, never deletes ---

    [Fact]
    public void MapHandler_Load_RemovesRowsDeletedFromDb()
    {
        // MapHandler load replaces state with DB contents, so rows deleted
        // from the DB stay deleted instead of resurrecting from memory on save.
        using var env = GlobalTestEnv.Enter();
        var mh = GlobalServices.GetMapHandler();
        try
        {
            mh.SetMapInfo("zona", 0, new MapInfo());
            using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); mh.Save(db, force: true); }
            using (var db = new AtherizDbContext(env.TempPath))
            {
                var rows = db.MapData.Where(r => r.Area == "zona").ToList();
                Assert.NotEmpty(rows);
                db.MapData.RemoveRange(rows);
                db.SaveChanges();
            }
            using (var db = new AtherizDbContext(env.TempPath)) { mh.Load(db); }
            Assert.Null(mh.GetMapInfo("zona", 0));
        }
        finally { GlobalServices.ResetForTesting(); }
    }

    // --- Load under lock / stale overwrite ---

    [Fact]
    public void Load_DoesNotStarveReaders()
    {
        // JsonTableLoader.LoadInto buffers deserialization off-lock and holds
        // the write lock only for the add-swap loop, so readers progress
        // mid-load. This pins that contract: readers must succeed while a
        // 150-node load runs, and the load must still be fully applied
        // afterwards. (A regression to hold-across-deserialize would starve this test red.)
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        const int count = 150;
        try
        {
            for (int i = 0; i < count; i++)
            {
                var n = new Node(new Coord("limbo", i * 2, 0, 0));
                if (ObjectRegistry.Get(n.Id).Count == 0) ObjectRegistry.AddObject(n);
                nh.AddNode(n);
            }
            using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); nh.Save(db, force: true); }
            NodeHandler? fresh = null;
            using (var db = new AtherizDbContext(env.TempPath))
            {
                fresh = new NodeHandler(autoLoad: false);
                var loader = new Thread(() =>
                {
                    try { fresh.Load(db); } catch { }
                });
                int ok = 0, attempts = 0;
                loader.Start();
                while (loader.IsAlive)
                {
                    attempts++;
                    if (fresh.Lock.TryEnterReadLock(0))
                    {
                        try { ok++; }
                        finally { fresh.Lock.ExitReadLock(); }
                    }
                }
                loader.Join(TimeSpan.FromSeconds(120));
                Assert.True(attempts > 0, "reader never sampled the load");
                Assert.True(ok > 0, $"readers starved for the whole load (attempts={attempts})");
            }
            Assert.NotNull(fresh!.GetNode(new Coord("limbo", 0, 0, 0)));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void Load_SameAreaName_EvictsStaleNodes()
    {
        // Overwriting _areas[name] on load also removes the old area's nodes
        // from ObjectRegistry, avoiding leaks and duplicate ids.
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var keeper = new Node(new Coord("limbo", 0, 0, 0));
            if (ObjectRegistry.Get(keeper.Id).Count == 0) ObjectRegistry.AddObject(keeper);
            nh.AddNode(keeper);
            using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); nh.Save(db, force: true); }
            var nh2 = new NodeHandler(autoLoad: false);
            NodeHandler.SetCurrent(nh2);
            var stale = new Node(new Coord("limbo", 9, 9, 0));
            if (ObjectRegistry.Get(stale.Id).Count == 0) ObjectRegistry.AddObject(stale);
            nh2.AddNode(stale);
            using (var db = new AtherizDbContext(env.TempPath)) { nh2.Load(db); }
            Assert.Empty(ObjectRegistry.Get(stale.Id));
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
