using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Script attach state is guarded by scoped locks on the script itself for the
// attach guard and the tracked-child clear, and on the child for the hook-set
// mutation: attach exposes the child, detach removes hooks and clears the
// tracked child, and attaching elsewhere while attached throws.
[Collection("Ported")]
public class ScriptLockScopeTests
{
    private sealed class MarkedScript : Script
    {
        public bool Fired;

        [Before]
        public void at_marked() => Fired = true;
    }

    [Fact]
    public void InstallHooks_Attach_ExposesChildViaGetter()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new MarkedScript();
            script.InstallHooks(child);
            Assert.Same(child, script.Child);
            Assert.True(child.HasHook("at_marked"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void InstallHooks_AttachedElsewhere_ThrowsAndKeepsOriginal()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var first = GameObject.Create("first");
            var second = GameObject.Create("second");
            var script = new MarkedScript();
            script.InstallHooks(first);
            Assert.Throws<InvalidOperationException>(() => script.InstallHooks(second));
            Assert.Same(first, script.Child);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void RemoveHooks_WithExplicitChild_DetachesHooksAndClearsTrackedChild()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new MarkedScript();
            script.InstallHooks(child);
            script.RemoveHooks(child);
            Assert.False(child.HasHook("at_marked"));
            Assert.Equal(1, child.Hookable<int>("at_marked", () => 1));
            Assert.False(script.Fired);
            Assert.Null(script.Child);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void RemoveHooks_NoArg_DetachesInstalledChild()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new MarkedScript();
            script.InstallHooks(child);
            script.RemoveHooks();
            Assert.False(child.HasHook("at_marked"));
            Assert.Null(script.Child);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void RemoveHooks_ThenReattachToOther_Succeeds()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var first = GameObject.Create("first");
            var second = GameObject.Create("second");
            var script = new MarkedScript();
            script.InstallHooks(first);
            script.RemoveHooks();
            script.InstallHooks(second);
            Assert.Same(second, script.Child);
            Assert.True(second.HasHook("at_marked"));
            Assert.False(first.HasHook("at_marked"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
