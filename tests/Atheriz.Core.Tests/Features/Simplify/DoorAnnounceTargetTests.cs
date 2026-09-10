using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Door announces: every site builds its own mapping instance carrying the
// caller under the shared key, so the parser substitutes the actor's name.
[Collection("Ported")]
public class DoorAnnounceTargetTests
{
    [Fact]
    public void TryLock_Announce_SubstitutesCallerName()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var caller = GameObject.Create("caller");
            ObjectRegistry.AddObject(caller);
            var receiver = GameObject.Create("recv");
            ObjectRegistry.AddObject(receiver);
            room.AddObject(caller);
            room.AddObject(receiver);
            var door = new Door(new Coord("limbo", 0, 0, 0), new Coord("limbo", 0, 1, 0), "north", "south");

            Assert.True(door.TryLock(caller));

            var heard = string.Join("\n", receiver.PeekMessages());
            Assert.Contains("caller", heard);
            Assert.Contains("lock", heard);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
