using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// NodeHandler regression pins: tombstone deletes, gen bumps,
// atomic delete-eviction, loud load failure.
[Collection("Ported")]
public class NodeHandlerRegressionTests
{
    private static readonly Coord C1 = new("a", 0, 0, 0);
    private static readonly Coord C2 = new("a", 1, 0, 0);
    private static readonly Coord C3 = new("a", 5, 5, 0);

    // RemoveArea()+Save()+Load must not resurrect the area row.
    [Fact] public void RemoveAreaSaveLoad_DoesNotResurrect()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler(autoLoad: false);
        handler.AddArea(new NodeArea("a"));
        using (var db = new AtherizDbContext(env.TempPath))
        {
            db.Database.EnsureCreated();
            handler.Save(db, force: true);
        }
        handler.RemoveArea("a");
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Save(db, force: true);
        }
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Load(db);
        }
        Assert.Null(handler.GetArea("a"));
    }

    // RemoveTransition()+Save()+Load must not resurrect the transition row.
    [Fact] public void RemoveTransitionSaveLoad_DoesNotResurrect()
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
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Save(db, force: true);
        }
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Load(db);
        }
        Assert.Empty(handler.FindTransitions());
    }

    // RemapDoors moves the dict and the old-coord row dies on Save:
    // a fresh Load sees doors only at the new coord.
    [Fact] public void RemapDoorsOldCoordRow_DeletedOnSave()
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
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Save(db, force: true);
        }
        using (var db = new AtherizDbContext(env.TempPath))
        {
            handler.Load(db);
        }
        Assert.Null(handler.GetDoors(C1));
        Assert.NotNull(handler.GetDoors(C3));
    }

    // RemoveTransition bumps the trans gen (AddTransition parity) so a
    // concurrent save cannot clear _modified2 post-commit and lose the removal.
    [Fact] public void RemoveTransition_BumpsTransGen()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler(autoLoad: false);
        var genField = typeof(NodeHandler).GetField("_transGen", BindingFlags.Instance | BindingFlags.NonPublic)!;
        long before = (long)genField.GetValue(handler)!;
        handler.AddTransition(new Transition(C1, C2, "east"));
        long afterAdd = (long)genField.GetValue(handler)!;
        handler.RemoveTransition(C2);
        long afterRemove = (long)genField.GetValue(handler)!;
        Assert.True(afterAdd > before);
        Assert.True(afterRemove > afterAdd);
    }

    // Load-swallow is loud and keeps live state: a dead DB logs and returns,
    // leaving in-memory areas untouched.
    [Fact] public void LoadFailure_KeepsLiveState()
    {
        using var env = GlobalTestEnv.Enter();
        var handler = new NodeHandler(autoLoad: false);
        handler.AddArea(new NodeArea("live"));
        var db = new AtherizDbContext(env.TempPath);
        db.Dispose();
        handler.Load(db);
        Assert.NotNull(handler.GetArea("live"));
    }
}
