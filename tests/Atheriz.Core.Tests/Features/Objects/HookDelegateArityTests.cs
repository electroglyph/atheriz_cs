using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Hook delegate binding covers arities 0-16 through the BCL factories and
// keeps the >16-params divergence: unbindable hooks are skipped loudly (no
// hook installed, original path runs) instead of crashing InstallHooks.
[Collection("Ported")]
public class HookDelegateArityTests
{
    private sealed class ZeroHookScript : Script
    {
        public bool Fired;
        [Before]
        public void at_zero() => Fired = true;
    }

    private sealed class SixteenHookScript : Script
    {
        public bool Fired;
        [Before]
        public void at_sixteen(object? p1, object? p2, object? p3, object? p4, object? p5, object? p6, object? p7, object? p8, object? p9, object? p10, object? p11, object? p12, object? p13, object? p14, object? p15, object? p16) => Fired = true;
    }

    private sealed class SeventeenFuncScript : Script
    {
        [After]
        public string at_seventeen(object? p1, object? p2, object? p3, object? p4, object? p5, object? p6, object? p7, object? p8, object? p9, object? p10, object? p11, object? p12, object? p13, object? p14, object? p15, object? p16, object? p17) => "never";
    }

    [Fact]
    public void ZeroParamHook_BindsAndFires()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new ZeroHookScript();
            script.InstallHooks(child);
            Assert.True(child.HasHook("at_zero"));
            Assert.Equal(3, child.Hookable<int>("at_zero", () => 3));
            Assert.True(script.Fired);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void SixteenParamHook_BindsAndFires()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new SixteenHookScript();
            script.InstallHooks(child);
            Assert.True(child.HasHook("at_sixteen"));
            Assert.Equal(3, child.Hookable<int>("at_sixteen", () => 3,
                null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null));
            Assert.True(script.Fired);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void SeventeenParamFuncHook_SkippedOriginalRuns()
    {
        // 17 params exceed the Func range: the BCL factory throws, the
        // CreateHookDelegate catch converts it to a loud skip, and the
        // original path runs with no hook installed.
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new SeventeenFuncScript();
            script.InstallHooks(child);
            Assert.False(child.HasHook("at_seventeen"));
            Assert.Equal("orig", child.Hookable<string>("at_seventeen", () => "orig",
                null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
