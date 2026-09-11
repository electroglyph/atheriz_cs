// None command fallback: unknown input still reaches the not-found report.
using Atheriz.Core.Commands;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class NoneCommandTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    [Fact]
    public void NoneCommand_RunUnknownInput_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        CommandRegistry.Reset();
        var _ = CommandRegistry.LoggedIn;
        try
        {
            var puppet = new GameObject { Name = "Hero" };
            puppet.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(puppet, "nosuchcmd_xyz xyz", immediate: true);
            RunJob(job);
            Assert.Contains(puppet.PeekMessages(), m => m.Contains("not found"));
        }
        finally { CommandRegistry.Reset(); }
    }
}
