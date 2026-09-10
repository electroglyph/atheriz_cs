using Atheriz.Core.Commands;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// The normal and glued global lookups share one internal-first tier order:
// an internal verb shadows the global one on both spellings, and a missing
// internal falls through to global. Pinned through real dispatch.
[Collection("Ported")]
public sealed class GlobalLookupResolutionTests
{
    private sealed class LookupCmd : Command
    {
        private readonly string _key;
        private readonly string _mark;
        public LookupCmd(string key, string mark) { _key = key; _mark = mark; }
        public override string Key => _key;
        public override bool UseParser => false;
        public override void Run(IMessageTarget caller, object? args) => caller.Msg(_mark);
    }

    [Fact]
    public void Dispatch_NormalPath_PrefersInternalOverGlobal()
    {
        CommandRegistry.Reset();
        try
        {
            CommandRegistry.LoggedIn.Add(new LookupCmd("zzglobe", "global-mark"));
            var puppet = new GameObject { Name = "Hero" };
            puppet.InternalCmdSet = new CmdSet();
            puppet.InternalCmdSet.Add(new LookupCmd("zzglobe", "internal-mark"));
            puppet.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(puppet, "zzglobe", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            Assert.Contains("internal-mark", puppet.PeekMessages());
        }
        finally { CommandRegistry.Reset(); }
    }

    [Fact]
    public void Dispatch_GluedPath_PrefersInternalOverGlobal()
    {
        CommandRegistry.Reset();
        try
        {
            CommandRegistry.LoggedIn.Add(new LookupCmd(",", "global-comma"));
            var puppet = new GameObject { Name = "Hero" };
            puppet.InternalCmdSet = new CmdSet();
            puppet.InternalCmdSet.Add(new LookupCmd(",", "internal-comma"));
            puppet.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(puppet, ",hello", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            Assert.Contains("internal-comma", puppet.PeekMessages());
        }
        finally { CommandRegistry.Reset(); }
    }

    [Fact]
    public void Dispatch_NormalPath_FallsThroughToGlobal()
    {
        CommandRegistry.Reset();
        try
        {
            CommandRegistry.LoggedIn.Add(new LookupCmd("zzglobe", "global-mark"));
            var puppet = new GameObject { Name = "Hero" };
            puppet.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(puppet, "zzglobe", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            Assert.Contains("global-mark", puppet.PeekMessages());
        }
        finally { CommandRegistry.Reset(); }
    }
}
