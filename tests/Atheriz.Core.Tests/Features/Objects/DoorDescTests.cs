using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// DoorDesc flavor renders in Desc when set, and the bare desc keeps its
// exact shape when empty.
[Collection("Ported")]
public sealed class DoorDescTests
{
    [Fact]
    public void Door_Desc_AppendsDoorDescWhenSet()
    {
        var from = new Coord("limbo", 0, 0, 0);
        var to = new Coord("limbo", 0, 1, 0);
        var plain = new Door(from, to, "north", "south");
        Assert.Equal("A closed door leading north", plain.Desc(from));
        var fancy = new Door(from, to, "north", "south") { DoorDesc = "Oak, bound in iron." };
        Assert.Equal("A closed door leading north Oak, bound in iron.", fancy.Desc(from));
    }
}
