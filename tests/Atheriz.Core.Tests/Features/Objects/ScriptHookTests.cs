using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Wide hooks (up to 16 params) bind as closed Action/Func delegates whose
// Method carries the [Before]/[After]/[Replace] marker for Hookable.
// Signatures beyond Action/Func range cannot be honored: InstallHooks logs
// loudly and skips them instead of installing a marker-less wrapper that
// Hookable would silently ignore .
[Collection("Ported")]
public class ScriptHookTests
{
    private sealed class BigHookScript : Script
    {
        public bool Fired;

        [Before]
        public void at_big_hook(object? p1, object? p2, object? p3, object? p4, object? p5, object? p6, object? p7, object? p8, object? p9)
        {
            Fired = true;
        }
    }

    [Fact]
    public void WideHook_FiresAfterInstall()
    {
        // Correct: an installed 9-param before-hook runs when its hook fires.
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new BigHookScript();
            script.InstallHooks(child);
            Assert.True(child.HasHook("at_big_hook"));
            Assert.Equal(7, child.Hookable<int>("at_big_hook", () => 7,
                null, null, null, null, null, null, null, null, null));
            Assert.True(script.Fired);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    private sealed class TooWideHookScript : Script
    {
        [Before]
        public void at_too_wide(object? p1, object? p2, object? p3, object? p4, object? p5, object? p6, object? p7, object? p8, object? p9, object? p10, object? p11, object? p12, object? p13, object? p14, object? p15, object? p16, object? p17)
        {
        }
    }

    [Fact]
    public void UnbindableHook_SkippedLoudly_NotInstalled()
    {
        // 17 params exceeds Action<...> range: no silent dead install — the
        // hook is skipped (error logged) and the original path runs.
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new TooWideHookScript();
            script.InstallHooks(child);
            Assert.False(child.HasHook("at_too_wide"));
            Assert.Equal(7, child.Hookable<int>("at_too_wide", () => 7,
                null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
