using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// A throwing lock predicate denies instead of propagating, on both the
// object and the door lock paths.
[Collection("Ported")]
public class ThrowingLockPredicateTests
{
    [Fact]
    public void Access_ThrowingPredicate_FailsClosed()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("Guarded");
        ObjectRegistry.AddObject(obj);
        var viewer = GameObject.Create("Viewer", isPc: true);
        ObjectRegistry.AddObject(viewer);
        obj.AddLock("view", _ => throw new InvalidOperationException("boom"));

        var ex = Record.Exception(() => obj.Access(viewer, "view"));
        Assert.Null(ex);
        Assert.False(obj.Access(viewer, "view"));
    }

    [Fact]
    public void DoorAccess_ThrowingPredicate_FailsClosed()
    {
        using var env = GlobalTestEnv.Enter();
        var viewer = GameObject.Create("Viewer", isPc: true);
        ObjectRegistry.AddObject(viewer);
        var door = new Door(new Coord("ThrowLock", 0, 0, 0), new Coord("ThrowLock", 0, 1, 0), "north", "south");
        door.AddLock("open", _ => throw new InvalidOperationException("boom"));

        var ex = Record.Exception(() => door.Access(viewer, "open"));
        Assert.Null(ex);
        Assert.False(door.Access(viewer, "open"));
    }
}
