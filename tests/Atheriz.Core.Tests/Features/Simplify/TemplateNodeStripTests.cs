using System.Reflection;
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
        Assert.Equal("hall", named.Name);
        named.AtInit();
        named.AtTick();
    }
}
