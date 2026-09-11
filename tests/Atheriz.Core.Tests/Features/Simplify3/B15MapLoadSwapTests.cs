using Atheriz.Core.Globals;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Map load clear-then-swap truth table: rows present swaps, explicitly empty
// DB wipes, and failed/corrupt loads preserve live state with a warning.
[Collection("Ported")]
public sealed class B15MapLoadSwapTests
{
    [Fact]
    public void Load_EmptyDb_WipesLiveMap()
    {
        using var env = GlobalTestEnv.Enter();
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            db.MapData.RemoveRange(db.MapData.ToList());
            db.SaveChanges();
        }
        var mh = new MapHandler(autoLoad: false);
        mh.SetMapInfo("live", 0, new MapInfo { Name = "live" });
        Assert.NotNull(mh.GetMapInfo("live", 0));
        using (var db = new AtherizDbContext(env.TempPath))
        {
            mh.Load(db);
        }
        Assert.Null(mh.GetMapInfo("live", 0));
    }

    [Fact]
    public void Load_NonEmptyDb_SwapsToDbState()
    {
        using var env = GlobalTestEnv.Enter();
        var mh = new MapHandler(autoLoad: false);
        mh.SetMapInfo("keep", 0, new MapInfo { Name = "keep" });
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            mh.Save(db, force: true);
        }
        mh.SetMapInfo("live-only", 0, new MapInfo { Name = "live-only" });
        using (var db = new AtherizDbContext(env.TempPath))
        {
            mh.Load(db);
        }
        Assert.NotNull(mh.GetMapInfo("keep", 0));
        Assert.Null(mh.GetMapInfo("live-only", 0));
    }

    [Fact]
    public void Load_CorruptRows_PreservesLiveMap()
    {
        using var env = GlobalTestEnv.Enter();
        var mh = new MapHandler(autoLoad: false);
        mh.SetMapInfo("zona", 0, new MapInfo { Name = "zona" });
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            mh.Save(db, force: true);
        }
        mh.SetMapInfo("live-only", 0, new MapInfo { Name = "live-only" });
        using (var db = new AtherizDbContext(env.TempPath))
        {
            foreach (var row in db.MapData.ToList())
                row.Data = "{corrupt-json";
            db.SaveChanges();
        }
        using (var db = new AtherizDbContext(env.TempPath))
        {
            mh.Load(db);
        }
        Assert.NotNull(mh.GetMapInfo("zona", 0));
        Assert.NotNull(mh.GetMapInfo("live-only", 0));
    }
}
