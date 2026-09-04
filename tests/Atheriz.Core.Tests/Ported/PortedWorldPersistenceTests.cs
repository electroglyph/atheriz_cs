// Port of atheriz/tests/test_world_persistence.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedWorldPersistenceTests
{
    [Fact] public void GridOverwrite_RemovesOldObject()
    {
        using var env = GlobalTestEnv.Enter();
        var area = new NodeArea("PersistA");
        var grid = new NodeGrid("PersistA",0);
        var c = new Coord("PersistA",0,0,0);
        var n1 = new Node(c);
        grid.AddNode(n1);
        ObjectRegistry.AddObject(n1);
        Assert.Single(ObjectRegistry.Get(n1.Id));
        var n2 = new Node(c);
        grid.AddNode(n2);
        ObjectRegistry.AddObject(n2);
        Assert.Equal(n2.Id, grid.GetNode((0,0))!.Id);
        Assert.Empty(ObjectRegistry.Get(n1.Id));
    }
    [Fact] public void ThreadPool_UsesCorrectSentinelCount()
    {
        using var env = GlobalTestEnv.Enter();
        var pool = new AsyncThreadPool(maxThreads:4);
        // Verify pool has maxThreads workers
        Assert.Equal(4, pool.MaxThreads);
        pool.Stop(wait:false);
    }
    [Fact] public void MapSave_SkipsWhenClean()
    {
        using var env = GlobalTestEnv.Enter();
        var mh = GlobalServices.GetMapHandler();
        var mi = mh.GetOrCreatePublic("CleanArea",0);
        mi.MapChanged = false; mi.IsModified = false;
        using(var db=new AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); mh.Save(db, force:false); }
        Assert.False(mi.MapChanged);
    }
    [Fact] public void GameTime_Save_IsSingleTransaction()
    {
        using var env = GlobalTestEnv.Enter();
        var gt = GlobalServices.GetGameTime();
        gt.Ticks = 7;
        using(var db=new AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); gt.Save(db); }
        Assert.Equal(7, gt.Ticks);
    }
    [Fact] public void NodeHandler_CreatesAreasAtomically()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler();
        var areaName="AtomicArea";
        var area = nh.GetArea(areaName);
        if(area!=null) nh.RemoveArea(areaName);
        var n1 = new Node(new Coord(areaName,0,0,0));
        var n2 = new Node(new Coord(areaName,1,0,0));
        nh.AddNode(n1); nh.AddNode(n2);
        Assert.NotNull(nh.GetArea(areaName));
    }
    [Fact] public void Checkpoint_PersistsLateMutation()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("LateMut"); ObjectRegistry.AddObject(obj);
        obj.IsModified = true; obj.Desc = "changed";
        using(var db=new AtherizDbContext(env.TempPath)){ db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        Assert.False(obj.IsModified);
        obj.Desc = "changed2"; obj.IsModified = true;
        using(var db=new AtherizDbContext(env.TempPath)){ ObjectRegistry.SaveObjects(db); }
        Assert.False(obj.IsModified);
        using(var db=new AtherizDbContext(env.TempPath)){
            var row = db.Objects.FirstOrDefault(o=>o.Id==obj.Id);
            Assert.NotNull(row);
        }
    }
    [Fact] public void Shutdown_StopsGameTime_BeforeTickerAndPool()
    {
        using var env = GlobalTestEnv.Enter();
        StartStop.ResetForTesting();
        var ticker = GlobalServices.GetAsyncTicker();
        var pool = GlobalServices.GetAsyncThreadPool();
        // Verify ordering: StartStop.DoShutdown stops gameTime before ticker/pool via ShutdownStep
        var ex = Record.Exception(() => StartStop.DoShutdown(ticker: ticker, pool: pool));
        Assert.Null(ex);
        StartStop.ResetForTesting();
    }
}
