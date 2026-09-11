// Shared global help lookup: hidden and unknown
// queries behave identically in both login sets, and visible queries show
// the shared formatted help.
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class HelpGlobalLookupTests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    private static GameObject MakeAdmin(string name)
    {
        var go = GameObject.Create(name, privilege: Privilege.Admin);
        ObjectRegistry.AddObject(go);
        go.ClearMessages();
        return go;
    }

    [Fact]
    public void HelpCommand_LoggedInHiddenCommand_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeAdmin("b19helper");

        RunJob(CommandDispatcher.DispatchLoggedIn(caller, "help spam", immediate: true));

        Assert.Contains(caller.PeekMessages(), m => m == "Command not found.");
    }

    [Fact]
    public void HelpCommand_LoggedInUnknownCommand_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeAdmin("b19helper");

        RunJob(CommandDispatcher.DispatchLoggedIn(caller, "help b19nosuchcmd", immediate: true));

        Assert.Contains(caller.PeekMessages(), m => m == "Command not found.");
    }

    [Fact]
    public void HelpCommand_LoggedInVisibleCommand_ShowsHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeAdmin("b19helper");

        RunJob(CommandDispatcher.DispatchLoggedIn(caller, "help look", immediate: true));

        Assert.Single(caller.PeekMessages());
        Assert.DoesNotContain(caller.PeekMessages(), m => m == "Command not found.");
    }

    [Fact]
    public void HelpCommand_UnloggedInHiddenCommand_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("b19help-hidden");

        var job = CommandDispatcher.ResolveUnloggedIn(conn, "help none");
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);

        Assert.Contains(conn.Sent, t => t.Args.Any(a => a != null && a.ToString()!.Contains("Command not found.")));
    }

    [Fact]
    public void HelpCommand_UnloggedInUnknownCommand_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("b19help-unknown");

        var job = CommandDispatcher.ResolveUnloggedIn(conn, "help b19nosuchcmd");
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);

        Assert.Contains(conn.Sent, t => t.Args.Any(a => a != null && a.ToString()!.Contains("Command not found.")));
    }

    [Fact]
    public void HelpCommand_UnloggedInVisibleCommand_ShowsHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("b19help-visible");

        var job = CommandDispatcher.ResolveUnloggedIn(conn, "help connect");
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);

        Assert.DoesNotContain(conn.Sent, t => t.Args.Any(a => a != null && a.ToString()!.Contains("Command not found.")));
        Assert.Contains(conn.Sent, t => t.Args.Any(a => a != null && a.ToString()!.Length > 0));
    }

    [Fact]
    public void HelpHelper_TryShowGlobal_EmptyQuery_ReturnsFalseWithoutMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeAdmin("b19helper");

        Assert.False(HelpHelper.TryShowGlobal(CommandRegistry.LoggedIn, caller, string.Empty));
        Assert.Empty(caller.PeekMessages());
    }
}
