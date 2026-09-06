using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Features.Globals;

// Concurrent checkpoint edits survive snapshot-then-clear.
[Collection("Ported")]
public class CheckpointTests
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

    // --- Concurrent mutation wiped by snapshot-then-clear ---

    [Fact]
    public void NodeHandler_AreaAddedMidSave_IsNotLost()
    {
        // NodeHandler snapshots refs before clearing _modified; an area added
        // between snapshot and clean must survive via the generation guard
        // (like the trans/door branches) instead of being wiped and lost
        // until an unrelated later mutation.
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
            // The concurrent add must have SURVIVED as dirty (gen guard skips
            // the clean), so a follow-up incremental save persists it. (Without
            // the guard the flag is wiped and no later save recovers it.)
            nh.Save(force: false);
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
}
