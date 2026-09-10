using System.Reflection;
using Atheriz.Core.Objects;
using MyGame;

namespace Atheriz.Core.Tests.Features.Simplify;

// CustomNode carries no pass-through overrides (11 deleted): forward-only
// bodies with identical arguments are dispatch-identical to the base.
[Collection("Ported")]
public class TemplateNodeStripTests
{
    [Fact]
    public void CustomNode_DeclaresNoOverrides()
    {
        var declared = typeof(CustomNode).GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.Empty(declared);
        Assert.Equal(2, typeof(CustomNode).GetConstructors().Length);
    }

    [Fact]
    public void CustomNode_Ctors_And_BaseDispatch_Survive()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new CustomNode();
        Assert.NotNull(node);
        var named = new CustomNode(new Coord("limbo", 1, 2, 3), "hall", "d");
        // Node.Name is coord-derived with a no-op setter (pre-existing Node
        // override): the forwarded "hall" is unobservable, exactly as on a
        // base Node built the same way.
        Assert.Equal("limbo(1,2,3)", named.Name);
        Assert.Equal(new Node(new Coord("limbo", 1, 2, 3), "hall", "d").Name, named.Name);
        named.AtInit();
        named.AtTick();
    }
}
