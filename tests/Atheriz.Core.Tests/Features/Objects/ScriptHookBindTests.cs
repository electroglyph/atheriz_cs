using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Hook-delegate argument construction is shared by the Action and Func arms:
// both bind closed delegates whose Method keeps the marker, zero-arg hooks
// still bind, and signatures outside the Action/Func range are skipped loudly
// with the original path running.
[Collection("Ported")]
public class ScriptHookBindTests
{
    private sealed class TwoParamBeforeScript : Script
    {
        public object? SeenA;
        public object? SeenB;

        [Before]
        public void at_mark(object? a, object? b)
        {
            SeenA = a;
            SeenB = b;
        }
    }

    private sealed class OneParamAfterScript : Script
    {
        [After]
        public string at_greet(object? who, string? prior) => $"hi:{who}:{prior}";
    }

    private sealed class ZeroArgScript : Script
    {
        public bool Fired;

        [Before]
        public void at_zero() => Fired = true;
    }

    private sealed class TooWideActionScript : Script
    {
        [Before]
        public void at_wide(object? p1, object? p2, object? p3, object? p4, object? p5, object? p6, object? p7, object? p8, object? p9, object? p10, object? p11, object? p12, object? p13, object? p14, object? p15, object? p16, object? p17)
        {
        }
    }

    private sealed class TooWideFuncScript : Script
    {
        [After]
        public string at_widefn(object? p1, object? p2, object? p3, object? p4, object? p5, object? p6, object? p7, object? p8, object? p9, object? p10, object? p11, object? p12, object? p13, object? p14, object? p15, object? p16, object? p17) => "never";
    }

    [Fact]
    public void InstallHooks_ActionArmHook_FiresOnDispatch()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new TwoParamBeforeScript();
            script.InstallHooks(child);
            Assert.True(child.HasHook("at_mark"));
            Assert.Equal(7, child.Hookable<int>("at_mark", () => 7, "x", "y"));
            Assert.Equal("x", script.SeenA);
            Assert.Equal("y", script.SeenB);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void InstallHooks_FuncArmHook_ReplacesResult()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new OneParamAfterScript();
            script.InstallHooks(child);
            Assert.True(child.HasHook("at_greet"));
            Assert.Equal("hi:Bob:orig", child.Hookable<string>("at_greet", () => "orig", "Bob"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void InstallHooks_ZeroArgArm_StillBindsAndFires()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new ZeroArgScript();
            script.InstallHooks(child);
            Assert.True(child.HasHook("at_zero"));
            Assert.Equal(3, child.Hookable<int>("at_zero", () => 3));
            Assert.True(script.Fired);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void InstallHooks_OverwideActionArity_SkipsAndRunsOriginal()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new TooWideActionScript();
            script.InstallHooks(child);
            Assert.False(child.HasHook("at_wide"));
            Assert.Equal(7, child.Hookable<int>("at_wide", () => 7,
                null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void InstallHooks_OverwideFuncArity_SkipsAndRunsOriginal()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new TooWideFuncScript();
            script.InstallHooks(child);
            Assert.False(child.HasHook("at_widefn"));
            Assert.Equal("orig", child.Hookable<string>("at_widefn", () => "orig",
                null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
