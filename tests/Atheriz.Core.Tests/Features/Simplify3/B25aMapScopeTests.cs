// Pins for the MapHandler / MapInfo scope conversion: grid updates,
// pre-render publish, batch deferral, listener/mapable membership, the
// same-area listener+mapable fast path, snapshots, clear tombstones, and
// save/load round-trip keep their observable behavior under the scopes.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B25aMapScopeTests
{
    private static GameObject MakeObj(string name)
    {
        var obj = GameObject.Create(name);
        ObjectRegistry.AddObject(obj);
        return obj;
    }

    [Fact]
    public void PreRender_AfterUpdateGrid_PublishesResolvedGrid()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var mi = new MapInfo { Name = "b25a-pre" };
            mi.UpdateGrid((2, 2), "#");
            mi.PreRender();
            Assert.True(mi.PostGrid.ContainsKey((2, 2)));
            Assert.Equal("#", mi.PostGrid[(2, 2)]);
        }
        finally
        {
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }

    [Fact]
    public void BatchUpdate_DefersRender_AppliesOnDispose()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var mi = new MapInfo { Name = "b25a-batch" };
            using (mi.BatchUpdate())
            {
                mi.UpdateGrid((0, 0), "#");
                mi.UpdateGrid((1, 0), "#");
            }
            Assert.True(mi.PreGrid.ContainsKey((0, 0)));
            Assert.True(mi.PreGrid.ContainsKey((1, 0)));
            Assert.True(mi.PostGrid.ContainsKey((0, 0)));
        }
        finally
        {
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }

    [Fact]
    public void RemoveListener_AfterAdd_TracksMembership()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var mi = new MapInfo { Name = "b25a-listen" };
            var obj = MakeObj("b25a-listener");
            mi.AddListener(obj);
            Assert.True(mi.Listeners.ContainsKey(obj.Id));
            mi.RemoveListener(obj);
            Assert.False(mi.Listeners.ContainsKey(obj.Id));
        }
        finally
        {
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }

    [Fact]
    public void AddMapableList_AddsAllEntries()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var mi = new MapInfo { Name = "b25a-mapables" };
            var first = MakeObj("b25a-m1");
            var second = MakeObj("b25a-m2");
            mi.AddMapableList(new[] { first, second }, notify: false);
            Assert.True(mi.Objects.ContainsKey(first.Id));
            Assert.True(mi.Objects.ContainsKey(second.Id));
        }
        finally
        {
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }

    [Fact]
    public void MoveListenerAndMapable_SameArea_InstallsBothEntries()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var handler = new MapHandler(autoLoad: false);
            var obj = MakeObj("b25a-both");
            var from = new Coord("b25a-area", 3, 3, 0);
            var to = new Coord("b25a-area", 4, 4, 0);
            handler.MoveListenerAndMapable(obj, to, from);
            var map = handler.GetMapInfo("b25a-area", 0);
            Assert.NotNull(map);
            Assert.True(map!.Listeners.ContainsKey(obj.Id));
            Assert.True(map!.Objects.ContainsKey(obj.Id));
        }
        finally
        {
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }

    [Fact]
    public void Save_Load_RoundTrip_PreservesGrid()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var mh = new MapHandler(autoLoad: false);
            var mi = new MapInfo { Name = "b25a-saved" };
            mi.PreGrid[(0, 0)] = "#";
            mh.SetMapInfo("b25a-saved", 0, mi);
            using (var db = new AtherizDbContext(env.TempPath))
            {
                db.Database.EnsureCreated();
                mh.Save(db, force: true);
            }
            var reloaded = new MapHandler(autoLoad: false);
            using (var db = new AtherizDbContext(env.TempPath))
            {
                reloaded.Load(db);
            }
            var got = reloaded.GetMapInfo("b25a-saved", 0);
            Assert.NotNull(got);
            Assert.Equal("#", got!.PreGrid[(0, 0)]);
        }
        finally
        {
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }

    [Fact]
    public void Clear_Save_Load_DoesNotResurrect()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var mh = new MapHandler(autoLoad: false);
            mh.SetMapInfo("b25a-gone", 0, new MapInfo { Name = "b25a-gone" });
            using (var db = new AtherizDbContext(env.TempPath))
            {
                db.Database.EnsureCreated();
                mh.Save(db, force: true);
            }
            mh.Clear();
            Assert.Null(mh.GetMapInfo("b25a-gone", 0));
            Assert.Empty(mh.Snapshot());
            using (var db = new AtherizDbContext(env.TempPath))
            {
                mh.Save(db, force: true);
            }
            var reloaded = new MapHandler(autoLoad: false);
            using (var db = new AtherizDbContext(env.TempPath))
            {
                reloaded.Load(db);
            }
            Assert.Null(reloaded.GetMapInfo("b25a-gone", 0));
        }
        finally
        {
            ObjectRegistry.ClearAll();
            GlobalServices.Reset();
        }
    }
}
