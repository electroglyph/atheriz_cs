using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Audit;

// Should-be tests for audit §1 A1 (lost concurrent checkpoint edits), A7
// (torn multi-table checkpoints), §2 B21 (load under lock / stale overwrite)
// and B23 (silent in-memory DB substitution + divergent default paths).
// Fails while the bug is present, passes once fixed. Production untouched.
//
// Determinism note: every timing-sensitive test below is gate-driven
// (ManualResetEventSlim / Task join), never sleep-driven. A gate that never
// fires fails loudly after its timeout instead of hanging the suite.
[Collection("Ported")]
public class AuditCheckpointShouldBeTests
{
    // Node subclass whose Name read pauses the save thread exactly inside
    // DTO serialization (after the refs snapshot, before MarkHandlerClean),
    // so the test can land a concurrent mutation strictly in the
    // snapshot-then-clear window. No sleeps, no guesswork: the gate is armed
    // before the save starts, and nothing else reads this instance's Name
    // (area/grid/transition/door snapshots read other objects' fields).
    private sealed class BlockingNode : Node
    {
        public readonly ManualResetEventSlim Entered = new(false);
        public readonly ManualResetEventSlim Proceed = new(false);
        public volatile bool Armed;
        public BlockingNode(Coord c) : base(c) { }
        public BlockingNode() : base() { }
        public override string Name
        {
            get
            {
                if (Armed)
                {
                    Entered.Set();
                    Proceed.Wait(TimeSpan.FromSeconds(15));
                }
                return base.Name;
            }
            set { }
        }
    }

    // --- A1: concurrent mutation wiped by snapshot-then-clear ---

    [Fact]
    public void NodeHandler_AreaAddedMidSave_IsNotLost()
    {
        // audit A1: NodeHandler snapshots refs, then MarkHandlerClean clears
        // _modified UNCONDITIONALLY on the area branch (no _areaGen, unlike the
        // trans/door branches). An area added between snapshot and clean is
        // wiped and never serialized: lost until an unrelated later mutation.
        using var env = GlobalTestEnv.Enter();
        var coordA = new Coord("limbo", 0, 0, 0);
        var coordB = new Coord("limboB", 0, 0, 0);
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var blocker = new BlockingNode(coordA);
            if (ObjectRegistry.Get(blocker.Id).Count == 0) ObjectRegistry.AddObject(blocker);
            nh.AddNode(blocker); // pre-seed dirty so handlerWas=true at snapshot
            blocker.Armed = true; // nothing reads this instance's Name until DTO build
            using var db = new AtherizDbContext(env.TempPath);
            db.Database.EnsureCreated();
            var saveTask = Task.Run(() => nh.Save(db, force: true));
            Assert.True(blocker.Entered.Wait(TimeSpan.FromSeconds(15)), "save never reached serialization");
            var nodeB = new Node(coordB);
            if (ObjectRegistry.Get(nodeB.Id).Count == 0) ObjectRegistry.AddObject(nodeB);
            nh.AddNode(nodeB); // lands strictly between snapshot and clean
            blocker.Proceed.Set();
            saveTask.Wait(TimeSpan.FromSeconds(20));
            Assert.True(saveTask.IsCompletedSuccessfully, "save task faulted: " + saveTask.Exception);
            using var db2 = new AtherizDbContext(env.TempPath);
            var probe = new NodeHandler(autoLoad: false);
            probe.Load(db2);
            Assert.NotNull(probe.GetNode(coordA)); // control: baseline persisted
            Assert.NotNull(probe.GetNode(coordB)); // the concurrent add must survive
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void NodeHandler_DoorAddedMidSave_SurvivesViaGenGuard()
    {
        // Pin: the door branch IS gen-guarded (_doorGen), so the same
        // interleaving must survive for doors. Guards the existing fix.
        using var env = GlobalTestEnv.Enter();
        var coordA = new Coord("limbo", 0, 0, 0);
        var coordB = new Coord("limbo", 2, 0, 0);
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var blocker = new BlockingNode(coordA);
            if (ObjectRegistry.Get(blocker.Id).Count == 0) ObjectRegistry.AddObject(blocker);
            nh.AddNode(blocker);
            blocker.Armed = true;
            using var db = new AtherizDbContext(env.TempPath);
            db.Database.EnsureCreated();
            var saveTask = Task.Run(() => nh.Save(db, force: true));
            Assert.True(blocker.Entered.Wait(TimeSpan.FromSeconds(15)), "save never reached serialization");
            nh.AddDoor(Door.Create(coordA, "east", coordB, "west", closed: true));
            blocker.Proceed.Set();
            saveTask.Wait(TimeSpan.FromSeconds(20));
            Assert.True(saveTask.IsCompletedSuccessfully, "save task faulted: " + saveTask.Exception);
            nh.Save(force: true);
            using var db2 = new AtherizDbContext(env.TempPath);
            var probe = new NodeHandler(autoLoad: false);
            probe.Load(db2);
            var doors = probe.GetDoors(coordA);
            Assert.NotNull(doors);
            Assert.Contains(doors!.Values, d => d.FromExit == "east");
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    [Fact]
    public void Object_SaveClearing_IsAtomicUnderHammer()
    {
        // Pin documenting the corrected mechanism: per-object
        // snapshot+clear is atomic under the object's write lock
        // (GameObjectDtoConverter.BuildSaveJson), so concurrent edits
        // re-dirty and are never wiped. Hammers that contract.
        using var env = GlobalTestEnv.Enter();
        var o = GameObject.Create("racer");
        ObjectRegistry.AddObject(o);
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); }
        using var stop = new CancellationTokenSource();
        var saver = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            { try { ObjectRegistry.SaveObjects(env.TempPath); } catch { } }
        });
        for (int i = 0; i < 200; i++) o.Desc = "v" + i;
        ObjectRegistry.SaveObjects(env.TempPath, force: true);
        stop.Cancel();
        saver.Wait(TimeSpan.FromSeconds(15));
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        var back = ObjectRegistry.Get(o.Id).FirstOrDefault();
        Assert.NotNull(back);
        Assert.Equal("v199", back!.Desc);
    }

    // --- A7: torn multi-table checkpoint ---

    private static AtherizSettings CrashSettings(string temp) =>
        new() { SavePath = temp, TimeSystemEnabled = false, AutosaveMinutes = 0 };

    [Fact]
    public void TornCheckpoint_SurfacesError_NotSilent()
    {
        // audit A7: objects/map/nodes/time save in separate transactions; a
        // crash between tables leaves objects-new/map-old, and nothing
        // validates or reports it on the next startup.
        using var env = GlobalTestEnv.Enter();
        var settings = CrashSettings(env.TempPath);
        var mh = GlobalServices.GetMapHandler();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var o = GameObject.Create("settler");
            ObjectRegistry.AddObject(o);
            mh.SetMapInfo("zona", 0, new MapInfo());
            var node = new Node(new Coord("limbo", 0, 0, 0));
            if (ObjectRegistry.Get(node.Id).Count == 0) ObjectRegistry.AddObject(node);
            nh.AddNode(node);
            var gt = new GameTime(settings, autoLoad: false);
            Autosave.AutosaveTick(settings, mh, nh, gt);
            // Crash between tables: map committed, then process dies before the rest.
            using (var db = new AtherizDbContext(env.TempPath))
            {
                var rows = db.MapData.ToList();
                Assert.NotEmpty(rows);
                db.MapData.RemoveRange(rows);
                db.SaveChanges();
            }
            GlobalServices.ResetForTesting();
            NodeHandler.SetCurrent(null);
            Exception? ex;
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                ex = Record.Exception(() => StartStop.DoStartup(settings: settings));
                log = cap.Read();
            }
            Assert.True(ex != null || !string.IsNullOrWhiteSpace(log),
                "torn checkpoint (map rows missing) must surface an error, not load silently");
        }
        finally
        {
            GlobalServices.ResetForTesting();
            NodeHandler.SetCurrent(null);
            try { StartStop.ResetForTesting(); } catch { }
        }
    }

    [Fact]
    public void IntactCheckpoint_LoadsSilently()
    {
        // Pin: a complete checkpoint must NOT trip the torn detector above.
        using var env = GlobalTestEnv.Enter();
        var settings = CrashSettings(env.TempPath);
        var mh = GlobalServices.GetMapHandler();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var o = GameObject.Create("settler");
            ObjectRegistry.AddObject(o);
            mh.SetMapInfo("zona", 0, new MapInfo());
            var node = new Node(new Coord("limbo", 0, 0, 0));
            if (ObjectRegistry.Get(node.Id).Count == 0) ObjectRegistry.AddObject(node);
            nh.AddNode(node);
            var gt = new GameTime(settings, autoLoad: false);
            Autosave.AutosaveTick(settings, mh, nh, gt);
            GlobalServices.ResetForTesting();
            NodeHandler.SetCurrent(null);
            Exception? ex;
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                ex = Record.Exception(() => StartStop.DoStartup(settings: settings));
                log = cap.Read();
            }
            Assert.Null(ex);
            Assert.True(string.IsNullOrWhiteSpace(log), "intact load must stay silent, got: " + log);
        }
        finally
        {
            GlobalServices.ResetForTesting();
            NodeHandler.SetCurrent(null);
            try { StartStop.ResetForTesting(); } catch { }
        }
    }

    // --- B21: load under lock / stale overwrite ---

    [Fact]
    public void Load_DoesNotStarveReaders()
    {
        // audit B21, corrected by measurement: JsonTableLoader.LoadInto
        // already buffers deserialization off-lock and holds the write lock
        // only for the add-swap loop, so readers progress mid-load. This pins
        // that contract: readers must succeed while a 150-node load runs, and
        // the load must still be fully applied afterwards. (A regression to
        // hold-across-deserialize would starve this test red.)
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
        // audit B21: overwriting _areas[name] on load never removes the old
        // area's nodes from ObjectRegistry (leak / duplicate ids).
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

    // --- B23: factory substitution + divergent paths ---

    [Fact]
    public void FactoryDefaultPath_MatchesSaveObjectsDefault()
    {
        // audit B23: Create() resolves ATHERIZ_SAVE_PATH ?? "save" while
        // SaveObjects resolves ATHERIZ_SAVE_PATH ?? Global.SavePath — with the
        // env var unset the two entry points write different DB files.
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
        // audit B23: Create() catches ANY InvalidOperationException (incl.
        // PathGuards violations) and returns an ephemeral :memory: context, so
        // writes succeed and go nowhere. It must throw instead.
        Assert.False(GameUtils.IsInGameFolder(),
            "test must run outside a game folder so the relative-path guard fires");
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var _ = AtherizDbContextFactory.Create("relative_xyz_atheriz");
        });
    }
}
