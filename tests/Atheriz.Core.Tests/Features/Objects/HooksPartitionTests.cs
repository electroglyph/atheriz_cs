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

    private sealed class BeforeFive
    {
        public readonly List<string> Calls = [];
        [Before] public void A() => Calls.Add("a");
        [Before] public void B() => Calls.Add("b");
        [Before] public void C() => Calls.Add("c");
        [Before] public void D() => Calls.Add("d");
        [Before] public void E() => Calls.Add("e");
    }

    private sealed class ArgsOnlyAfter
    {
        [After] public string Zed() => "zed";
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

    [Fact]
    public void BeforeHooks_FiveRunInInstallOrder()
    {
        // The store is insertion-ordered: five hooks must dispatch a-e even
        // though a hash set would scramble them.
        var host = new BeforeFive();
        var obj = new GameObject();
        obj.InstallHook("at_five", (Action)host.A);
        obj.InstallHook("at_five", (Action)host.B);
        obj.InstallHook("at_five", (Action)host.C);
        obj.InstallHook("at_five", (Action)host.D);
        obj.InstallHook("at_five", (Action)host.E);
        Assert.Equal(7, obj.Hookable<int>("at_five", () => 7));
        Assert.Equal(["a", "b", "c", "d", "e"], host.Calls);
    }

    [Fact]
    public void SameDelegate_InstalledTwice_RunsOnce()
    {
        // Same-delegate installs dedupe (HashSet.Add semantics): one entry,
        // one run.
        var host = new BeforePair();
        var obj = new GameObject();
        var a = (Action)host.A;
        obj.InstallHook("at_dupe", a);
        obj.InstallHook("at_dupe", a);
        Assert.Equal(7, obj.Hookable<int>("at_dupe", () => 7));
        Assert.Equal(["a"], host.Calls);
    }

    [Fact]
    public void AfterHook_ArgsOnlyShape_FiresWhenResultShapeMisses()
    {
        // Arity branch: a zero-param after hook cannot take args+result, so
        // it runs args-only and still replaces the result.
        var host = new ArgsOnlyAfter();
        var obj = new GameObject();
        obj.InstallHook("at_args_only", (Func<string>)host.Zed);
        Assert.Equal("zed", obj.Hookable<string>("at_args_only", () => "orig"));
    }
}
