// Pins for the U6 follow-up (dropped the unreachable Links null-check in
// GetDisplayExits — the setter now throws on null): the exits display keeps
// its empty and populated shapes. The null-setter throw stays pinned by
// LinksSetterTests.
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class LinksNullCheckTests
{
    [Fact]
    public void GetDisplayExits_NoLinks_EmitsNothing()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("exitshape", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        Assert.Equal("", node.GetDisplayExits());
    }

    [Fact]
    public void GetDisplayExits_LinksPresent_EmitsJoinedNames()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("exitshape", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        node.AddLink(new NodeLink("north", new Coord("exitshape", 0, 1, 0)));
        node.AddLink(new NodeLink("south", new Coord("exitshape", 0, -1, 0)));
        var exits = node.GetDisplayExits();
        Assert.Contains("north", exits);
        Assert.Contains("south", exits);
    }
}
