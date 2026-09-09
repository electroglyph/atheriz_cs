using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Hook dispatch needs one contract: hook failures surface as the hook's own
// error and a bad replace delegate never takes down the op.
// Before/replace delegates currently crash MoveTo/Msg with raw reflection
// exceptions (Hooks/Hookable.cs:30-35).
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
    public void ReplaceHook_ArityMismatch_FallsBackToOriginal()
    {
        // Correct: a replace delegate that cannot take the call args is
        // ignored and the original runs (mirrors the after-hook arity
        // fallback at Hooks/Hookable.cs:50).
        var obj = new GameObject();
        obj.InstallHook("at_look", HookFor("WrongArity", typeof(Func<GameObject?, GameObject?, GameObject?, int>)));
        Assert.Equal("orig", obj.Hookable<string>("at_look", () => "orig", (GameObject?)null));
    }

    [Fact]
    public void BeforeHook_ArityMismatch_SkippedOriginalRuns()
    {
        // A mis-signed before hook gets the replace-hook treatment (skip +
        // loud log), not an exception out of the entry point: MoveTo-style
        // callers see a normal return instead of a reflection throw.
        var obj = new GameObject();
        obj.InstallHook("at_look", HookFor("BeforeWrongArity", typeof(Func<GameObject?, GameObject?, GameObject?, string>)));
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
}
