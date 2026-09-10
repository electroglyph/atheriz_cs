using Atheriz.Core;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Link lookup scoping: HasLinkName/GetLinkByName match names and aliases
// case-insensitively through one shared lookup, while AddLinkIfAbsent guards
// on names only — an alias collision must not block creation.
[Collection("Ported")]
public class LinkAliasCollisionTests
{
    [Fact]
    public void AliasMatch_DoesNotBlockAddLinkIfAbsent()
    {
        var node = new Node(new Coord("limbo", 0, 0, 0));
        node.AddLink(new NodeLink("north", new Coord("limbo", 0, 1, 0), new List<string> { "n" }));

        Assert.True(node.HasLinkName("N"));

        bool factoryRan = false;
        bool added = node.AddLinkIfAbsent("n", () =>
        {
            factoryRan = true;
            return new NodeLink("n", new Coord("limbo", 1, 0, 0), new List<string>());
        });

        Assert.True(factoryRan);
        Assert.True(added);
        Assert.Equal(2, node.GetLinks().Count);
    }

    [Fact]
    public void NameMatch_BlocksAddLinkIfAbsent_CaseInsensitively()
    {
        var node = new Node(new Coord("limbo", 0, 0, 0));
        node.AddLink(new NodeLink("north", new Coord("limbo", 0, 1, 0), new List<string> { "n" }));

        bool factoryRan = false;
        bool added = node.AddLinkIfAbsent("NORTH", () =>
        {
            factoryRan = true;
            return new NodeLink("NORTH", new Coord("limbo", 0, -1, 0), new List<string>());
        });

        Assert.False(factoryRan);
        Assert.False(added);
        Assert.Single(node.GetLinks());
        Assert.NotNull(node.GetLinkByName("NoRtH"));
    }
}
