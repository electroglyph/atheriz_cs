using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests;

// F006: Node/GameObject identity + lock unification.
// Registry-touching: serialized with the Ported collection (GlobalTestEnv resets
// shared globals; running parallel would race collection-free tests like MovePuppetTests).
[Collection("Ported")]
public class NodeIdentityTests
{
    [Fact]
    public void NodeNameAndSymbolAgreeAcrossStaticTypes()
    {
        using var env = GlobalTestEnv.Enter();
        var coord = new Coord("ident", 1, 2, 3);
        var node = new Node(coord);
        GameObject go = node;
        // No static-type divergence: GameObject-typed refs see the coord-derived name.
        Assert.Equal(node.Name, go.Name);
        Assert.Equal(coord.ToString(), go.Name);
        // Symbol shares base storage (was a shadowing `new` auto-prop).
        go.Symbol = "#";
        Assert.Equal("#", node.Symbol);
        node.Symbol = "@";
        Assert.Equal("@", go.Symbol);
    }

    [Fact]
    public void NodeNameSetterIsIgnoredNeverObserved()
    {
        using var env = GlobalTestEnv.Enter();
        var coord = new Coord("ident", 4, 5, 6);
        var node = new Node(coord);
        GameObject go = node;
        node.Name = "n1"; // ported tests assign names; write is accepted and ignored
        Assert.Equal(coord.ToString(), node.Name);
        Assert.Equal(coord.ToString(), go.Name);
    }

    [Fact]
    public void IdEqualityLivesOnBaseOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var n1 = new Node(new Coord("ident", 0, 0, 0));
        var n2 = new Node(new Coord("ident", 1, 0, 0));
        GameObject g1 = n1;
        Assert.True(n1.Equals(g1));
        Assert.True(g1.Equals(n1));
        Assert.Equal(n1.GetHashCode(), g1.GetHashCode());
        Assert.False(n1.Equals(n2));
        // Mixed static types in one set stay consistent (was ref-equality vs id-equality).
        var set = new HashSet<GameObject> { n1 };
        Assert.Contains(g1, set);
        Assert.DoesNotContain(n2, set);
    }

    [Fact]
    public void NodeUsesSingleSharedLock()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("ident", 7, 7, 7));
        Assert.Same(node.SyncRoot, node.NodeLock);
        Assert.Same(node.SyncRoot, node.Lock);
    }

    [Fact]
    public void NodeScriptsUseSharedBaseSet()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("ident", 8, 8, 8));
        GameObject go = node;
        Assert.Equal(go.ScriptsSnapshot, node.ScriptsSet);
    }

    [Fact]
    public void NodeGridEqualityIgnoresInsertionOrder()
    {
        using var env = GlobalTestEnv.Enter();
        var g1 = new NodeGrid("ident", 0);
        var g2 = new NodeGrid("ident", 0);
        var a = new Node(new Coord("ident", 0, 0, 0));
        var b = new Node(new Coord("ident", 1, 1, 0));
        g1.Nodes[(0, 0)] = a; g1.Nodes[(1, 1)] = b;
        g2.Nodes[(1, 1)] = b; g2.Nodes[(0, 0)] = a;
        Assert.True(g1.Equals(g2));
        Assert.Equal(g1.GetHashCode(), g2.GetHashCode());
        var other = new NodeGrid("ident", 1);
        Assert.False(g1.Equals(other));
    }

    [Fact]
    public void NodeAreaEqualityIgnoresInsertionOrder()
    {
        using var env = GlobalTestEnv.Enter();
        var a1 = new NodeArea("ident");
        var a2 = new NodeArea("ident");
        a1.AddGrid(new NodeGrid("ident", 0));
        a1.AddGrid(new NodeGrid("ident", 1));
        a2.AddGrid(new NodeGrid("ident", 1));
        a2.AddGrid(new NodeGrid("ident", 0));
        Assert.True(a1.Equals(a2));
        Assert.Equal(a1.GetHashCode(), a2.GetHashCode());
        var other = new NodeArea("other");
        Assert.False(a1.Equals(other));
    }
}
