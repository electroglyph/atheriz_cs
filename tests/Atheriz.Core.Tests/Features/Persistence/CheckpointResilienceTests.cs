using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Features.Persistence;

// Failure-isolation contracts: one bad row/table must not take down the world.
[Collection("Ported")]
public class CheckpointResilienceTests
{
    // --- Failed map load keeps prior state ---

    [Fact]
    public void MapHandler_FailedLoad_PreservesLiveMap()
    {
        // A load whose rows are all corrupt must leave the live map untouched
        // (MapHandler.cs:891-917): transient corruption is not a deletion, so
        // live entries must survive instead of being swapped out for empty.
        using var env = GlobalTestEnv.Enter();
        var mh = new MapHandler(autoLoad: false);
        mh.SetMapInfo("zona", 0, new MapInfo());
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); mh.Save(db, force: true); }
        mh.SetMapInfo("live-only", 0, new MapInfo());
        using (var db = new AtherizDbContext(env.TempPath))
        {
            foreach (var row in db.MapData.ToList()) row.Data = "{corrupt-json";
            db.SaveChanges();
        }
        using (var db = new AtherizDbContext(env.TempPath)) { mh.Load(db); }
        Assert.NotNull(mh.GetMapInfo("zona", 0));
        Assert.NotNull(mh.GetMapInfo("live-only", 0));
    }

    // --- Deleted node rows stay deleted ---

    [Fact]
    public void NodeHandler_DeletedRow_StaysDeletedAfterLoad()
    {
        // Rows deleted from the database must not resurrect from memory on the
        // next load (NodeHandler.cs:62-94 never clears missing areas): after a
        // delete plus load the node must be gone from the handler and the
        // registry instead of being rewritten on the following save.
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var coord = new Coord("limbo", 0, 0, 0);
            var node = new Node(coord);
            if (ObjectRegistry.Get(node.Id).Count == 0) ObjectRegistry.AddObject(node);
            nh.AddNode(node);
            using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); nh.Save(db, force: true); }
            using (var db = new AtherizDbContext(env.TempPath))
            {
                var rows = db.Areas.Where(r => r.Name == "limbo").ToList();
                Assert.NotEmpty(rows);
                db.Areas.RemoveRange(rows);
                db.SaveChanges();
            }
            using (var db = new AtherizDbContext(env.TempPath)) { nh.Load(db); }
            Assert.Null(nh.GetNode(coord));
            Assert.Empty(ObjectRegistry.Get(node.Id));
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // --- One poison row must not abort the checkpoint ---

    private sealed class PoisonObject : GameObject
    {
        public PoisonObject(int id)
        {
            Id = id;
            Name = "poison";
            IsModified = true;
        }
        public override (string Sql, object[] Params) GetSaveOpsClearing()
            => throw new InvalidOperationException("injected poison row");
    }

    [Fact]
    public void SaveObjects_PoisonRow_DoesNotAbortSiblingCheckpoint()
    {
        // One unserializable object must not abort the whole checkpoint
        // (ObjectRegistry.cs:438-475 re-dirties and discards everything on a
        // single failure): healthy siblings must still reach the database
        // instead of every object skipping the write until manual removal.
        using var env = GlobalTestEnv.Enter();
        var a = GameObject.Create("healthy-a");
        ObjectRegistry.AddObject(a);
        var b = GameObject.Create("healthy-b");
        ObjectRegistry.AddObject(b);
        var poison = new PoisonObject(GameObject.GetNextId());
        ObjectRegistry.AddObject(poison);
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); }
        using (var db = new AtherizDbContext(env.TempPath))
        {
            try { ObjectRegistry.SaveObjects(db, force: true); }
            catch (InvalidOperationException) { }
        }
        using (var probe = new AtherizDbContext(env.TempPath))
        {
            Assert.NotNull(probe.Objects.Find(a.Id));
            Assert.NotNull(probe.Objects.Find(b.Id));
        }
    }

    // --- Node load must not burn generator ids ---

    [Fact]
    public void NodeHandler_Load_DoesNotBurnIdGenerator()
    {
        // Loading rows must adopt the ids stored in the rows
        // (Node.cs:58-75 registers a transient id per row before replacing
        // it): loading low ids over a high generator watermark must leave the
        // watermark untouched instead of consuming one value per row on a
        // registration that is immediately thrown away.
        using var env = GlobalTestEnv.Enter();
        var seed = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(seed);
        try
        {
            var n0 = new Node(new Coord("limbo", 0, 0, 0));
            if (ObjectRegistry.Get(n0.Id).Count == 0) ObjectRegistry.AddObject(n0);
            seed.AddNode(n0);
            var n1 = new Node(new Coord("limbo", 4, 0, 0));
            if (ObjectRegistry.Get(n1.Id).Count == 0) ObjectRegistry.AddObject(n1);
            seed.AddNode(n1);
            using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); seed.Save(db, force: true); }
        }
        finally { NodeHandler.SetCurrent(null); }
        ObjectRegistry.ClearAll();
        IdGenerator.SetId(100);
        var loader = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(loader);
        try
        {
            using (var db = new AtherizDbContext(env.TempPath)) { loader.Load(db); }
            Assert.Equal(100, IdGenerator.GetId());
            Assert.NotEmpty(ObjectRegistry.Get(0));
            Assert.NotEmpty(ObjectRegistry.Get(1));
            Assert.Empty(ObjectRegistry.Get(101));
            Assert.Empty(ObjectRegistry.Get(102));
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
