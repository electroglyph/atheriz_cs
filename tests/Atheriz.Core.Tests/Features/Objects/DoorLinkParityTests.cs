using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;
using TimeProvider = Atheriz.Core.Utils.TimeProvider;

namespace Atheriz.Core.Tests.Features.Objects;

// Node link parity: AddLink vs AddLinkIfAbsent dedup asymmetry (nodes.py:662-691).
[Collection("Ported")]
public class DoorLinkParityTests
{
    [Fact]
    public void AddLink_Twins_KeepPythonAsymmetry()
    {
        // Behavior pin, corrected for Python parity (nodes.py:662-691):
        // add_link dedups (name, coord) while add_link_if_absent dedups
        // name-only — DELIBERATELY different. Same-name-different-coord is
        // ADDED by AddLink and REFUSED by AddLinkIfAbsent. Pins the asymmetry.
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
            Assert.Equal(2, n1.GetLinks().Count(l => l.Name == "north"));
            Assert.False(absentAdded);
            Assert.Single(n2.GetLinks(), l => l.Name == "north");
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
