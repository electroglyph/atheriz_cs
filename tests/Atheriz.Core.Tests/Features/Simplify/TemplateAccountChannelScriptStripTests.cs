using System.Reflection;
using MyGame;

namespace Atheriz.Core.Tests.Features.Simplify;

// Account/Channel/Script templates carry no pass-through overrides (4 + 2 +
// 1 deleted). Scaffolded games adding their own overrides later are
// unaffected: they declare fresh overrides regardless.
[Collection("Ported")]
public class TemplateAccountChannelScriptStripTests
{
    private static void AssertNoOverrides(Type t, int ctorCount)
    {
        var declared = t.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.Empty(declared);
        Assert.Equal(ctorCount, t.GetConstructors().Length);
    }

    [Fact]
    public void Templates_DeclareNoOverrides()
    {
        AssertNoOverrides(typeof(CustomAccount), 1);
        AssertNoOverrides(typeof(CustomChannel), 1);
        AssertNoOverrides(typeof(CustomScript), 1);
    }

    [Fact]
    public void Templates_Ctors_And_BaseDispatch_Survive()
    {
        using var env = GlobalTestEnv.Enter();
        var acc = new CustomAccount();
        Assert.NotNull(acc);
        var chan = new CustomChannel();
        Assert.NotNull(chan);
        var script = new CustomScript();
        Assert.NotNull(script);
        acc.AtCreate();
        chan.AtCreate();
        script.AtInstall();
    }
}
