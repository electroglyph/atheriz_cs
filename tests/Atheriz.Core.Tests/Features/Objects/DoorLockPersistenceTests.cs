using Atheriz.Core;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Door access locks must persist: ToDto/DoorDto carry coords/symbols/state
// plus declarative lock policies, so a locked door loads locked instead of
// open to everyone (Door.cs AddLock/Locks vs Access deny-when-present).
[Collection("Ported")]
public class DoorLockPersistenceTests
{
    [Fact]
    public void AccessLock_PolicyDeny_SurvivesDtoRoundtrip()
    {
        // Correct: a door denying "open" via a named policy still denies it
        // after save->load.
        var door = Door.Create(new Coord("limbo", 0, 0, 0), "north", new Coord("limbo", 0, 1, 0), "south");
        var sneaky = GameObject.Create("sneaky");
        door.AddLock("open", accessing => accessing.IsBuilder, LockPolicies.Builder);
        Assert.False(door.Access(sneaky, "open"));

        var back = Door.FromDto(door.ToDto());
        Assert.False(back.Access(sneaky, "open"));
    }

    [Fact]
    public void AccessLock_BareLambda_IsDroppedOnRoundtrip()
    {
        // Contract (mirrors GameObject lock policies): ad-hoc lambdas cannot
        // survive serialization and are dropped with a loud log, never executed.
        var door = Door.Create(new Coord("limbo", 0, 0, 0), "north", new Coord("limbo", 0, 1, 0), "south");
        var sneaky = GameObject.Create("sneaky");
        door.AddLock("open", _ => false);
        Assert.False(door.Access(sneaky, "open"));

        var back = Door.FromDto(door.ToDto());
        Assert.True(back.Access(sneaky, "open"));
    }
}
