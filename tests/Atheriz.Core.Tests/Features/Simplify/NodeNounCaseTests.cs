using Atheriz.Core;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Node nouns: the store is case-insensitive, so no case-variant hunt is
// needed on add and a single remove clears every casing.
[Collection("Ported")]
public class NodeNounCaseTests
{
    [Fact]
    public void AddNoun_OverwritesAcrossCasings()
    {
        var node = new Node(new Coord("limbo", 0, 0, 0));

        node.AddNoun("Sword", "a blade");
        Assert.Equal("a blade", node.GetNoun("sWORD"));

        node.AddNoun("SWORD", "other");
        Assert.Equal("other", node.GetNoun("sword"));
    }

    [Fact]
    public void RemoveNoun_ClearsEveryCasing()
    {
        var node = new Node(new Coord("limbo", 0, 0, 0));
        node.AddNoun("Sword", "a blade");

        node.RemoveNoun("SwOrD");

        Assert.Null(node.GetNoun("sword"));
        Assert.Null(node.GetNoun("SWORD"));
    }
}
