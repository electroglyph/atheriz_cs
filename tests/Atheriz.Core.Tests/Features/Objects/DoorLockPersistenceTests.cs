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

    [Fact]
    public void AccessLock_UnresolvableNamedPolicy_FailsClosedOnRoundtrip()
    {
        // A named-but-unresolvable policy must fail closed across a save/load
        // round-trip instead of failing open.
        var door = Door.Create(new Coord("f3a", 0, 0, 0), "east", new Coord("f3a", 1, 0, 0), "west");
        var target = GameObject.Create("target");
        var other = GameObject.Create("other");
        door.AddLock("open", o => o.Id != target.Id, LockPolicies.NotSelf);
        Assert.True(door.Access(other, "open"));
        var back = Door.FromDto(door.ToDto());
        Assert.False(back.Access(other, "open"));
        Assert.Contains("not-self", string.Join(";", back.ToDto().Locks));
    }
}
