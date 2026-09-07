using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Features.Globals;

// Mutation-visibility and singleton-lifecycle contracts for the world handlers.
[Collection("Ported")]
public class GlbMutationPersistenceTests
{
    private static long ReadAreaGen(NodeHandler nh)
    {
        var field = typeof(NodeHandler).GetField("_areaGen", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (long)field!.GetValue(nh)!;
    }

    // --- RemoveNode generation guard ---

    [Fact]
    public void RemoveNode_AdvancesAreaGeneration_LikeSiblingMutators()
    {
        // Every NodeHandler mutator advances the area generation counter so the
        // post-commit clean-guard (NodeHandler.cs:355-362) cannot wipe a
        // mutation that landed mid-save. AddNode/AddArea/RemoveArea/Clear all
        // bump it (NodeHandler.Partial.cs:87,96-98,102-112,121-139);
        // RemoveNode (NodeHandler.Partial.cs:159-168) must bump it too.
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var node = new Node(new Coord("limbo", 0, 0, 0));
            if (ObjectRegistry.Get(node.Id).Count == 0) ObjectRegistry.AddObject(node);
            nh.AddNode(node);
            long before = ReadAreaGen(nh);
            nh.RemoveNode(node.Coord);
            long after = ReadAreaGen(nh);
            Assert.True(after > before, $"RemoveNode must bump _areaGen (was {before}, now {after})");
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // --- Stale load must not clobber live objects ---

    [Fact]
    public void NodeHandler_Load_PreservesLiveModifiedObjects()
    {
        // Loading rows that are older than live in-memory edits must not
        // discard the live objects (NodeHandler.cs:113-124 overwrites on id
        // collision): the registry must keep the live instance and its newer
        // content instead of the stale row.
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var coord = new Coord("limbo", 0, 0, 0);
            var live = new Node(coord, "room", "stale-original");
            if (ObjectRegistry.Get(live.Id).Count == 0) ObjectRegistry.AddObject(live);
            nh.AddNode(live);
            using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); nh.Save(db, force: true); }
            live.Desc = "live-edit";
            using (var db = new AtherizDbContext(env.TempPath)) { nh.Load(db); }
            var current = ObjectRegistry.Get(live.Id);
            Assert.Single(current);
            Assert.Same(live, current[0]);
            Assert.Equal("live-edit", current[0].Desc);
        }
        finally { NodeHandler.SetCurrent(null); }
    }

    // --- Exposed chains mapping must be a snapshot ---

    [Fact]
    public void MapEdit_ChainsSnapshot_SurvivesClearOfExposedDictionary()
    {
        // The exposed chains mapping (MapEdit.cs:98-99) must be a snapshot like
        // ChainsSnapshot (MapEdit.cs:101-109): clearing it must not evict live
        // chains from the store.
        MapEdit.ResetForTesting();
        try
        {
            string key = MapEdit.Grant("9.9.9.9", "limbo", 0, session: null);
            MapEdit.chains.Clear();
            Assert.NotNull(MapEdit.GetChain(key));
            Assert.Single(MapEdit.ChainsSnapshot);
        }
        finally { MapEdit.ResetForTesting(); }
    }

    [Fact]
    public void MapEdit_ChainsSnapshot_SurvivesInsertIntoExposedDictionary()
    {
        // Writing through the exposed chains mapping (MapEdit.cs:98-99) must
        // not plant entries in the store: only Grant/AddChain may do that.
        MapEdit.ResetForTesting();
        try
        {
            string key = MapEdit.Grant("9.9.9.9", "limbo", 0, session: null);
            Assert.NotNull(MapEdit.GetChain(key));
            MapEdit.chains["injected"] = new MapEditChain("injected", "9.9.9.9", "limbo", 0);
            Assert.False(MapEdit.ChainsSnapshot.ContainsKey("injected"));
            Assert.Single(MapEdit.ChainsSnapshot);
        }
        finally { MapEdit.ResetForTesting(); }
    }

    // --- Consume must hand out a copy ---

    [Fact]
    public void MapEdit_ConsumeResult_MutatingItLeavesStoreIntact()
    {
        // Consume (MapEdit.cs:257-322) must hand out a copy like GetChain
        // (MapEdit.cs:326-336) does: mutating the returned chain must not
        // rewrite the stored chain behind the lock.
        MapEdit.ResetForTesting();
        try
        {
            string key = MapEdit.Grant("1.2.3.4", "limbo", 0, session: null);
            var res = MapEdit.Consume(key, "1.2.3.4", 0);
            Assert.Equal(MapEditStatus.Processed, res.Status);
            Assert.NotNull(res.NewKey);
            Assert.NotNull(res.Chain);
            res.Chain!.Chain.Add(new Coord("evil", 1, 2, 3));
            res.Chain.Validation = new List<int> { 999 };
            var stored = MapEdit.GetChain(res.NewKey!);
            Assert.NotNull(stored);
            Assert.DoesNotContain(new Coord("evil", 1, 2, 3), stored!.Chain);
            Assert.True(stored.Validation == null || !stored.Validation.Contains(999));
        }
        finally { MapEdit.ResetForTesting(); }
    }

    // --- Shutdown must not conjure singletons ---

    [Fact]
    public void Shutdown_PartialBoot_DoesNotCreateGameTime()
    {
        // Stopping a server that never fully booted must not construct world
        // singletons just to stop them (StartStop.cs:496-521): after shutdown
        // with no prior boot the game-time holder must still be empty.
        using var env = GlobalTestEnv.Enter();
        Assert.Null(GlobalServices.TryGetGameTime());
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = true, AutosaveMinutes = 0, AutosaveOnShutdown = false };
        StartStop.DoShutdown(settings);
        try
        {
            Assert.Null(GlobalServices.TryGetGameTime());
        }
        finally { try { StartStop.ResetForTesting(); } catch { } }
    }

    // --- Shutdown must release world handlers ---

    [Fact]
    public void Shutdown_ClearsWorldHandlers_SoNextBootReloads()
    {
        // Shutdown must release the cached node/map handlers
        // (GlobalServices.cs:183-197 keeps them while ResetForTesting at
        // :200-219 clears all): a lookup after shutdown must build fresh
        // instances instead of resurrecting pre-shutdown world state.
        using var env = GlobalTestEnv.Enter();
        var mh1 = GlobalServices.GetMapHandler();
        var nh1 = GlobalServices.GetNodeHandler();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = false, AutosaveMinutes = 0, AutosaveOnShutdown = false };
        StartStop.DoShutdown(settings);
        try
        {
            Assert.False(ReferenceEquals(mh1, GlobalServices.GetMapHandler()));
            Assert.False(ReferenceEquals(nh1, GlobalServices.GetNodeHandler()));
        }
        finally { try { StartStop.ResetForTesting(); } catch { } }
    }
}
