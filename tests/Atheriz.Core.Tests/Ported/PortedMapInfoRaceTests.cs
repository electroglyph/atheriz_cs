// Port of atheriz/tests/test_mapinfo_race.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedMapInfoRaceTests
{
    private static GameObject LocObj(string area, int z=0)
    {
        var o = GameObject.Create($"obj_{area}_{z}");
        o.Location = new Persistence.Dto.LocationRef.CoordLocation(new Coord(area,0,0,z));
        return o;
    }

    [Fact]
    public void MoveListenerAndMapableShareOneInstance()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new MapHandler(autoLoad:false);
        var listener = LocObj("race-area");
        var mapable = LocObj("race-area");
        var barrier = new Barrier(2);
        void RunListener() { barrier.SignalAndWait(); handler.AddListener(listener); }
        void RunMapable() { barrier.SignalAndWait(); handler.AddMapable(mapable); }
        var t1 = new Thread(RunListener); var t2 = new Thread(RunMapable);
        t1.Start(); t2.Start(); t1.Join(5000); t2.Join(5000);
        var snap = handler.Snapshot();
        Assert.Single(snap.Where(kv => kv.Key == ("race-area",0)));
        var mi = snap[("race-area",0)];
        Assert.Contains(listener.Id, mi.Listeners.Keys);
        Assert.Contains(mapable.Id, mi.Objects.Keys);
    }

    [Fact]
    public void OverlappingConstructionsYieldSingleInstance()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new MapHandler(autoLoad:false);
        var barrier = new Barrier(2);
        var results = new List<MapInfo>();
        var lk = new object();
        void Creator() { barrier.SignalAndWait(); var mi = handler.EnsureMapInfo("slow-area",0); lock(lk) results.Add(mi); }
        var t1 = new Thread(Creator); var t2 = new Thread(Creator);
        t1.Start(); t2.Start(); t1.Join(5000); t2.Join(5000);
        Assert.Equal(2, results.Count);
        Assert.Same(results[0], results[1]);
        Assert.Single(handler.Snapshot().Where(kv => kv.Key.Item1=="slow-area"));
    }

    [Fact]
    public void GetOrCreateReturnsExistingObjectUnchanged()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new MapHandler(autoLoad:false);
        var mi = new MapInfo("existing");
        mi.PreGrid[(0,0)]="#"; mi.PostGrid[(0,0)]="+";
        handler.SetMapInfo("existing",3, mi);
        var got = handler.EnsureMapInfo("existing",3);
        Assert.Same(mi, got);
        Assert.Equal("#", mi.PreGrid[(0,0)]);
        Assert.Equal("+", mi.PostGrid[(0,0)]);
    }

    [Fact]
    public void SaveReloadYieldsSingleChunk()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new MapHandler(autoLoad:false);
        var listener = LocObj("persist-area");
        var mapable = LocObj("persist-area");
        handler.AddListener(listener);
        handler.AddMapable(mapable);
        var mi = handler.GetMapInfo("persist-area",0);
        Assert.NotNull(mi);
        Assert.Contains(listener.Id, mi!.Listeners.Keys);
        handler.Save(force:true);
        var handler2 = new MapHandler(autoLoad:true);
        // Need to load from same DB file explicitly
        using (var db = Persistence.AtherizDbContextFactory.Create(env.TempPath)) handler2.Load(db);
        var chunks = handler2.Snapshot().Where(kv => kv.Key==("persist-area",0)).ToList();
        Assert.Single(chunks);
        Assert.Equal("persist-area", chunks[0].Value.Name);
    }
}
