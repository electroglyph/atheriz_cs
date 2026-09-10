using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Hook removal is target-identity based: detaching one script instance must
// not detach a distinct same-Id instance (hot-reload rewire shape), which is
// why the single-pass removal uses ReferenceEquals and not == (Id-equality).
[Collection("Ported")]
public class RemoveHooksIdentityTests
{
    private sealed class MarkedScript : Script
    {
        public bool Fired;
        [Before]
        public void at_marked() => Fired = true;
    }

    [Fact]
    public void RemoveHooks_SameIdDistinctInstance_Survives()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var a = new MarkedScript();
            a.InstallHooks(child);
            var b = new MarkedScript();
            b.Id = a.Id;
            b.InstallHooks(child);

            a.RemoveHooks(child);

            Assert.True(child.HasHook("at_marked"));
            Assert.Equal(1, child.Hookable<int>("at_marked", () => 1));
            Assert.False(a.Fired);
            Assert.True(b.Fired);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void RemoveHooks_OwnHooks_AllDetached()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var a = new MarkedScript();
            a.InstallHooks(child);
            Assert.True(child.HasHook("at_marked"));
            a.RemoveHooks(child);
            Assert.False(child.HasHook("at_marked"));
            Assert.Equal(1, child.Hookable<int>("at_marked", () => 1));
            Assert.False(a.Fired);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
