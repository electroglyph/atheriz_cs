// Pins for U6 (Links setter guard): null assignment fails loud at the single
// write point, valid assignment round-trips, and the exits display keeps its
// shape for empty and populated link lists.
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B24LinksSetterTests
{
    [Fact]
    public void Links_SetNull_ThrowsArgumentNullException()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("linkguard", 0, 0, 0));
        Assert.Throws<ArgumentNullException>(() => node.Links = null!);
    }

    [Fact]
    public void Links_SetValid_RoundTrips()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("linkguard", 0, 0, 0));
        var links = new List<NodeLink> { new("north", new Coord("linkguard", 0, 1, 0)) };
        node.Links = links;
        Assert.Single(node.Links);
        Assert.Equal("north", node.Links[0].Name);
    }

    [Fact]
    public void GetDisplayExits_EmptyLinks_ReturnsEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("linkguard", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        Assert.Equal("", node.GetDisplayExits());
    }

    [Fact]
    public void GetDisplayExits_WithLinks_ListsNames()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("linkguard", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        node.AddLink(new NodeLink("north", new Coord("linkguard", 0, 1, 0)));
        node.AddLink(new NodeLink("south", new Coord("linkguard", 0, -1, 0)));
        var exits = node.GetDisplayExits();
        Assert.Contains("north", exits);
        Assert.Contains("south", exits);
    }
}
