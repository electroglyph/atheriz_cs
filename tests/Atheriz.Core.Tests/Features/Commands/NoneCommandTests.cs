// None command fallback: unknown input still reaches the not-found report.
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
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

    private static GameObject MakePlayer(string name)
    {
        var c = GameObject.Create(name, "", isPc: false, privilege: Privilege.Player);
        ObjectRegistry.AddObject(c);
        c.ClearMessages();
        return c;
    }

    [Fact]
    public void NoneCommand_DoesNotSuggestInaccessibleCommands()
    {
        // Typo fallback must not suggest commands the caller cannot use.
        using var env = GlobalTestEnv.Enter();
        var low = MakePlayer("low");
        new NoneCommand().Run(low, "setx");
        var text = string.Join("\n", low.PeekMessages());
        Assert.DoesNotContain("\"set\"", text);
    }

    [Fact]
    public void NoneCommand_StillSuggestsVisibleCommands()
    {
        // Visible commands are still suggested.
        using var env = GlobalTestEnv.Enter();
        var low = MakePlayer("low2");
        new NoneCommand().Run(low, "lookk");
        var text = string.Join("\n", low.PeekMessages());
        Assert.Contains("\"look\"", text);
    }
}
