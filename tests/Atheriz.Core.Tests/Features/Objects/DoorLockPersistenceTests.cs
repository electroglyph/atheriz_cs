using Atheriz.Core;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Door access locks must persist: ToDto/DoorDto carry coords/symbols/state
// but no locks, so a locked door loads lockless and open to everyone
// (Door.cs:95 vs :456-502; Access at :161-171 returns true when lockless).
[Collection("Ported")]
public class DoorLockPersistenceTests
{
    [Fact]
    public void AccessLock_SurvivesDtoRoundtrip()
    {
        // Correct: a door denying "open" still denies it after save->load.
        var door = Door.Create(new Coord("limbo", 0, 0, 0), "north", new Coord("limbo", 0, 1, 0), "south");
        var sneaky = GameObject.Create("sneaky");
        door.AddLock("open", _ => false);
        Assert.False(door.Access(sneaky, "open"));

        var back = Door.FromDto(door.ToDto());
        Assert.False(back.Access(sneaky, "open"));
    }
}
