using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Look dispatch: AtLook reaches the Node appearance override through virtual
// dispatch (no type test), and container contents resolve via the snapshot
// path with no caller-side copy.
[Collection("Ported")]
public class LookDispatchTests
{
    [Fact]
    public void AtLook_UsesVirtualDispatch_ForNode()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var node = new Node(new Coord("limbo", 1, 2, 3));
            node.Desc = "A plain room.";
            var looker = GameObject.Create("looker");

            var seen = looker.AtLook(node);

            Assert.Equal(node.ReturnAppearance(looker), seen);
            Assert.Contains("A plain room.", seen);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtLook_NullTarget_SeesNothing()
    {
        var looker = GameObject.Create("looker");
        Assert.Equal("You see nothing here.", looker.AtLook(null));
    }

    [Fact]
    public void GetDisplayThings_GroupsSnapshotContents()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var item = GameObject.Create("sword");
            ObjectRegistry.AddObject(item);
            room.AddObject(item);
            var looker = GameObject.Create("looker");

            Assert.Contains("sword", room.GetDisplayThings(looker));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
