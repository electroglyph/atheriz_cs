// Pins for U6 (Links setter guard): null assignment fails loud at the single
// write point, valid assignment round-trips, and the exits display keeps its
// shape for empty and populated link lists.
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class LinksSetterTests
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

    [Fact]
    public void GetDisplayExits_UsesCompassOrderThenAlphabetical()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("linkorder", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        foreach (var (name, x, y) in new[] { ("down", 0, -1), ("west", -1, 0), ("portal", 2, 0), ("south", 0, -2), ("north", 0, 1), ("east", 1, 0), ("up", 0, 2) })
            node.AddLink(new NodeLink(name, new Coord("linkorder", x, y, 0)));

        var exits = node.GetDisplayExits();

        Assert.True(exits.IndexOf("north", StringComparison.Ordinal) < exits.IndexOf("south", StringComparison.Ordinal));
        Assert.True(exits.IndexOf("south", StringComparison.Ordinal) < exits.IndexOf("east", StringComparison.Ordinal));
        Assert.True(exits.IndexOf("east", StringComparison.Ordinal) < exits.IndexOf("west", StringComparison.Ordinal));
        Assert.True(exits.IndexOf("west", StringComparison.Ordinal) < exits.IndexOf("up", StringComparison.Ordinal));
        Assert.True(exits.IndexOf("up", StringComparison.Ordinal) < exits.IndexOf("down", StringComparison.Ordinal));
        Assert.True(exits.IndexOf("down", StringComparison.Ordinal) < exits.IndexOf("portal", StringComparison.Ordinal));
    }
}
