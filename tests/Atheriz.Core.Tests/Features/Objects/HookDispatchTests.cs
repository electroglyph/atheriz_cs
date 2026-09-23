using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Hook dispatch needs one contract: hook failures surface as the hook's own
// error and a bad replace delegate never takes down the op. Known hooks get
// attach-time arity validation: a delegate that cannot take any dispatch
// shape is refused loudly at InstallHook, so the op runs hook-free.
[Collection("Ported")]
public class HookDispatchTests
{
    private sealed class BoomHooks
    {
        [Before]
        public string FailBefore(GameObject? target) => throw new InvalidOperationException("boom-before");

        [Replace]
        public int WrongArity(GameObject? a, GameObject? b, GameObject? c) => 99;

        [Before]
        public string BeforeWrongArity(GameObject? a, GameObject? b, GameObject? c) => "never";

        [After]
        public string AfterBoom(GameObject? target) => throw new InvalidOperationException("boom-after");
    }

    private static Delegate HookFor(string name, Type delegateType)
    {
        var host = new BoomHooks();
        return (Delegate)typeof(BoomHooks).GetMethod(name)!.CreateDelegate(delegateType, host);
    }

    [Fact]
    public void BeforeHook_Throw_SurfacesOriginalError()
    {
        // Correct: a throwing before-hook aborts with its own error, not a
        // reflection wrapper, so callers can handle it.
        var obj = new GameObject();
        obj.InstallHook("at_look", HookFor("FailBefore", typeof(Func<GameObject?, string>)));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            obj.Hookable<string>("at_look", () => "orig", (GameObject?)null));
        Assert.Equal("boom-before", ex.Message);
    }

    [Fact]
    public void ReplaceHook_ArityMismatch_RefusedAtAttach()
    {
        // A replace delegate that cannot take the call args is refused at
        // attach (loud log, no install), so the original runs hook-free.
        var obj = new GameObject();
        obj.InstallHook("at_look", HookFor("WrongArity", typeof(Func<GameObject?, GameObject?, GameObject?, int>)));
        Assert.False(obj.HasHook("at_look"));
        Assert.Equal("orig", obj.Hookable<string>("at_look", () => "orig", (GameObject?)null));
    }

    [Fact]
    public void BeforeHook_ArityMismatch_RefusedAtAttach()
    {
        // A mis-signed before hook is refused at attach instead of
        // installing and skipping at every dispatch: MoveTo-style callers
        // see a normal return with no hook installed.
        var obj = new GameObject();
        obj.InstallHook("at_look", HookFor("BeforeWrongArity", typeof(Func<GameObject?, GameObject?, GameObject?, string>)));
        Assert.False(obj.HasHook("at_look"));
        Assert.Equal("orig", obj.Hookable<string>("at_look", () => "orig", (GameObject?)null));
    }

    [Fact]
    public void AfterHook_ThrowOnArgsOnlyArity_Propagates()
    {
        // The hook skips the args+result attempt on arity, matches the
        // args-only attempt, and throws there: the error propagates instead
        // of being swallowed by the fallback's old catch-all.
        var obj = new GameObject();
        obj.InstallHook("at_look", HookFor("AfterBoom", typeof(Func<GameObject?, string>)));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            obj.Hookable<string>("at_look", () => "orig", (GameObject?)null));
        Assert.Equal("boom-after", ex.Message);
    }

    [Fact]
    public void InstallHook_UnknownName_BypassesValidation()
    {
        // Custom hook names define their own shapes: any delegate attaches.
        var obj = new GameObject();
        Action<string, bool, int> custom = (_, _, _) => { };
        obj.InstallHook("at_custom_shape", custom);
        Assert.True(obj.HasHook("at_custom_shape"));
    }

    private sealed class TickProbe
    {
        public bool Fired;
        [Before]
        public void OnTick() { Fired = true; }
    }

    [Fact]
    public void InstallHook_HookNameOverload_AttachesAndDispatches()
    {
        // The typed overload installs a valid hook that fires on dispatch.
        var obj = new GameObject();
        var probe = new TickProbe();
        obj.InstallHook(HookName.AtTick, (Action)probe.OnTick);
        Assert.True(obj.HasHook(HookName.AtTick));
        obj.Hookable<int>(HookName.AtTick, () => 0);
        Assert.True(probe.Fired);
    }

    [Fact]
    public void HookName_Name_RoundTrips_AllMembers()
    {
        // Every enum member maps to a distinct non-empty runtime name and
        // TryParseName inverts the mapping: adding a member without mapper
        // arms fails here, not as a silently unvalidated hook.
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (HookName name in Enum.GetValues<HookName>())
        {
            string runtime = name.Name();
            Assert.False(string.IsNullOrEmpty(runtime));
            Assert.True(names.Add(runtime));
            Assert.Equal(name, HookNameExtensions.TryParseName(runtime));
        }
        Assert.Null(HookNameExtensions.TryParseName("at_no_such_hook"));
        Assert.Null(HookNameExtensions.TryParseName(null));
    }
}
