using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Script hooks with more than 8 parameters install but never fire: the
// reflection fallback builds an un-attributed lambda, which Hookable's
// strict attribute classification ignores (Script.cs:128-161,183-188;
// Hooks/Hookable.cs:30-56).
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
}
