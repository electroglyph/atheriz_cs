using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;


namespace Atheriz.Core.Tests.Features.Objects;

// Node link parity: AddLink vs AddLinkIfAbsent dedup asymmetry (nodes.py:662-691).
[Collection("Ported")]
public class DoorLinkParityTests
{
    [Fact]
    public void AddLink_Twins_DedupNameOnly()
    {
        // Link lookups fold case, so both add paths dedup name-only
        // dedup name-only (case-insensitive). Same-name-different-coord is
        // REFUSED by AddLink and by AddLinkIfAbsent alike — the second link
        // would be installed but unreachable (shadowed).
        ObjectRegistry.ClearAll();
        try
        {
            var coordA = new Coord("limbo", 0, 2, 0);
            var coordB = new Coord("limbo", 0, 4, 0);
            var n1 = new Node(new Coord("limbo", 0, 0, 0));
            var n2 = new Node(new Coord("limbo", 10, 0, 0));
            if (ObjectRegistry.Get(n1.Id).Count == 0) ObjectRegistry.AddObject(n1);
            if (ObjectRegistry.Get(n2.Id).Count == 0) ObjectRegistry.AddObject(n2);
            n1.AddLink(new NodeLink("north", coordA, new List<string> { "n" }));
            n2.AddLink(new NodeLink("north", coordA, new List<string> { "n" }));
            n1.AddLink(new NodeLink("north", coordB, new List<string> { "n" }));
            bool absentAdded = n2.AddLinkIfAbsent("north", () => new NodeLink("north", coordB, new List<string> { "n" }));
            Assert.Single(n1.GetLinks(), l => l.Name == "north");
            Assert.False(absentAdded);
            Assert.Single(n2.GetLinks(), l => l.Name == "north");
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
