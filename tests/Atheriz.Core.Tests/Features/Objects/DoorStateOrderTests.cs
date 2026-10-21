using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Objects;

// Door refusal wording and the order in which state checks win over key
// checks, observed through the messages a caller actually receives.
[Collection("Ported")]
public class DoorStateOrderTests
{
    private static (Node Room, GameObject Caller) MakeRoom(string area)
    {
        var handler = new NodeHandler();
        NodeHandler.SetCurrent(handler);
        var room = new Node(new Coord(area, 0, 0, 0));
        handler.AddNode(room);
        var caller = GameObject.Create("caller");
        ObjectRegistry.AddObject(caller);
        caller.Location = new LocationRef.CoordLocation(room.Coord);
        room.AddObject(caller);
        return (room, caller);
    }

    private static string ReceivedText(GameObject caller) => string.Join("\n", caller.PeekMessages());

    [Fact]
    public void TryLock_OpenDoor_RefusalSaysYouCannotLockAnOpenDoor()
    {
        using var env = GlobalTestEnv.Enter();
        var (room, caller) = MakeRoom("lowercase");
        var door = new Door(room.Coord, new Coord("lowercase", 0, 1, 0), "north", "south", closed: false);

        Assert.False(door.TryLock(caller));

        var text = ReceivedText(caller);
        Assert.Contains("but you can't lock an open door.", text);
        Assert.DoesNotContain("but You", text);
    }

    [Fact]
    public void TryLock_OpenKeyedDoorWithoutKey_ReportsOpenDoorBeforeMissingKey()
    {
        using var env = GlobalTestEnv.Enter();
        var (room, caller) = MakeRoom("openkeyed");
        var key = GameObject.Create("brass key");
        ObjectRegistry.AddObject(key);
        var door = new Door(room.Coord, new Coord("openkeyed", 0, 1, 0), "north", "south", closed: false)
        {
            KeyId = key.Id,
        };

        Assert.False(door.TryLock(caller));

        var text = ReceivedText(caller);
        Assert.Contains("you can't lock an open door.", text);
        Assert.DoesNotContain("lack the key", text);
        Assert.False(door.Locked);
    }

    [Fact]
    public void TryUnlock_UnlockedKeyedDoorWithoutKey_ReportsAlreadyUnlocked()
    {
        using var env = GlobalTestEnv.Enter();
        var (room, caller) = MakeRoom("unlockedkeyed");
        var key = GameObject.Create("brass key");
        ObjectRegistry.AddObject(key);
        var door = new Door(room.Coord, new Coord("unlockedkeyed", 0, 1, 0), "north", "south", closed: true, locked: false)
        {
            KeyId = key.Id,
        };

        Assert.False(door.TryUnlock(caller));

        var text = ReceivedText(caller);
        Assert.Contains("it is already unlocked.", text);
        Assert.DoesNotContain("lack the key", text);
    }

    [Fact]
    public void TryUnlock_LockedKeyedDoorWithoutKey_StillReportsMissingKey()
    {
        using var env = GlobalTestEnv.Enter();
        var (room, caller) = MakeRoom("lockedkeyed");
        var key = GameObject.Create("brass key");
        ObjectRegistry.AddObject(key);
        var door = new Door(room.Coord, new Coord("lockedkeyed", 0, 1, 0), "north", "south", closed: true, locked: true)
        {
            KeyId = key.Id,
        };

        Assert.False(door.TryUnlock(caller));

        Assert.Contains("lack the key", ReceivedText(caller));
        Assert.True(door.Locked);
    }
}
