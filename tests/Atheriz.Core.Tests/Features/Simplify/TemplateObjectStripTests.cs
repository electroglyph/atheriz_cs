using System.Reflection;
using MyGame;

namespace Atheriz.Core.Tests.Features.Simplify;

// CustomObject carries no pass-through overrides: every former body only
// forwarded to base with identical arguments, so virtual dispatch reaches the
// same implementation with the overrides deleted.
[Collection("Ported")]
public class TemplateObjectStripTests
{
    [Fact]
    public void CustomObject_DeclaresNoOverrides()
    {
        var declared = typeof(CustomObject).GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.Empty(declared);
        Assert.Equal(2, typeof(CustomObject).GetConstructors().Length);
    }

    [Fact]
    public void CustomObject_Ctors_And_BaseDispatch_Survive()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = new CustomObject("tmpl_obj");
        Assert.Equal("tmpl_obj", obj.Name);
        Assert.False(obj.IsPc);
        Assert.True(new CustomObject("pc", true).IsPc);
        var plain = new CustomObject();
        Assert.NotNull(plain);
        // Dispatch reaches the base implementation without the overrides.
        plain.AtCreate();
        plain.AtTick();
        plain.AtInit();
    }
}
