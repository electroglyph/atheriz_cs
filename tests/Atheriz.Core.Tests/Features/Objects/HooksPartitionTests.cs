using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Single-partition hook dispatch keeps per-kind order and per-hook after
// chaining: the kind filters merged into one pass, but each hook still runs
// in install order and after-hooks still observe the running result.
[Collection("Ported")]
public class HooksPartitionTests
{
    private sealed class BeforePair
    {
        public readonly List<string> Calls = [];
        [Before] public void A() => Calls.Add("a");
        [Before] public void B() => Calls.Add("b");
    }

    private sealed class ChainHooks
    {
        public readonly List<object?> Seen = [];
        [After] public string First(object? result) { Seen.Add(result); return "one"; }
        [After] public string Second(object? result) { Seen.Add(result); return "two"; }
    }

    private sealed class ReplacePair
    {
        public bool BeforeRan;
        [Before] public void B() => BeforeRan = true;
        [Replace] public int R() => 42;
    }

    [Fact]
    public void BeforeHooks_RunInInstallOrder()
    {
        var host = new BeforePair();
        var obj = new GameObject();
        obj.InstallHook("at_order", (Action)host.A);
        obj.InstallHook("at_order", (Action)host.B);
        Assert.Equal(7, obj.Hookable<int>("at_order", () => 7));
        Assert.Equal(["a", "b"], host.Calls);
    }

    [Fact]
    public void AfterHooks_ChainRunningResultInOrder()
    {
        // Each after-hook sees the previous hook's result (per-hook args
        // allocation), so the final value is the last hook's return.
        var host = new ChainHooks();
        var obj = new GameObject();
        obj.InstallHook("at_chain", (Func<object?, string>)host.First);
        obj.InstallHook("at_chain", (Func<object?, string>)host.Second);
        Assert.Equal("two", obj.Hookable<string>("at_chain", () => "orig"));
        Assert.Equal(["orig", "one"], host.Seen);
    }

    [Fact]
    public void ReplaceHook_ShortCircuitsBeforeAndOriginal()
    {
        var host = new ReplacePair();
        var obj = new GameObject();
        obj.InstallHook("at_repl", (Action)host.B);
        obj.InstallHook("at_repl", (Func<int>)host.R);
        Assert.Equal(42, obj.Hookable<int>("at_repl", () => 7));
        Assert.False(host.BeforeRan);
    }
}
