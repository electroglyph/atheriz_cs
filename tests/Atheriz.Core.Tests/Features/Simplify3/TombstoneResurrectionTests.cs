using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Delete-then-re-add resurrection per domain: a tombstoned key that is live
// again before the checkpoint must survive it instead of being deleted.
[Collection("Ported")]
public sealed class TombstoneResurrectionTests
{
    private static readonly Coord C1 = new("rez", 0, 0, 0);
    private static readonly Coord C2 = new("rez", 1, 0, 0);
    private static readonly Coord C3 = new("rez", 5, 5, 0);

    [Fact]
    public void Save_RemoveAreaThenReadd_PersistsResurrectedRow()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler(autoLoad: false);
        handler.AddArea(new NodeArea("rez"));
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            handler.Save(db, force: true);
        }
        handler.RemoveArea("rez");
        handler.AddArea(new NodeArea("rez"));
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Save(db, force: true);
        }
        var fresh = new NodeHandler(autoLoad: false);
        using (var db = new AtherizDbContext(env.TempPath))
        {
            fresh.Load(db);
        }
        Assert.NotNull(fresh.GetArea("rez"));
    }

    [Fact]
    public void Save_RemoveTransitionThenReadd_PersistsResurrectedRow()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler(autoLoad: false);
        handler.AddTransition(new Transition(C1, C2, "east"));
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            handler.Save(db, force: true);
        }
        handler.RemoveTransition(C2);
        handler.AddTransition(new Transition(C1, C2, "east"));
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Save(db, force: true);
        }
        var fresh = new NodeHandler(autoLoad: false);
        using (var db = new AtherizDbContext(env.TempPath))
        {
            fresh.Load(db);
        }
        Assert.NotEmpty(fresh.FindTransitions());
    }

    [Fact]
    public void Save_RemapDoorThenReaddOldCoord_PersistsResurrectedRow()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler(autoLoad: false);
        handler.AddDoor(new Door(C1, C2, "east", "west", (0, 0), "X", "O", true, false));
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            handler.Save(db, force: true);
        }
        handler.RemapDoors(new Dictionary<Coord, Coord> { [C1] = C3 });
        handler.AddDoor(new Door(C1, C2, "east", "west", (0, 0), "X", "O", true, false));
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Save(db, force: true);
        }
        var fresh = new NodeHandler(autoLoad: false);
        using (var db = new AtherizDbContext(env.TempPath))
        {
            fresh.Load(db);
        }
        Assert.NotNull(fresh.GetDoors(C1));
        Assert.NotNull(fresh.GetDoors(C3));
    }
}
