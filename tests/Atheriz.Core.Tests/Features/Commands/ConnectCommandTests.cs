// Connect double-cast removal: the direct cast throws InvalidCastException
// for non-Account rows exactly like the old as-plus-explicit pair, and valid
// logins still welcome.
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;
using System.Diagnostics;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class ConnectCommandTests
{
    private static async Task<TaskCompletionSource<string>> WaitForPromptAsync(TestConnection conn, TaskCompletionSource<string>? previous)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            var future = conn.Session!.InputFuture;
            if (future is not null && !ReferenceEquals(future, previous)) return future;
            await Task.Delay(10);
        }
        throw new TimeoutException("character selection prompt did not appear");
    }

    private static string SentText(TestConnection conn)
        => string.Join("\n", conn.Sent.SelectMany(s => s.Args).OfType<string>());

    [Fact]
    public void ConnectCommand_RunWithValidCredentials_WelcomesAccount()
    {
        using var env = GlobalTestEnv.Enter();
        var account = Account.Create("connect_acc", "connect_pw");
        Assert.NotNull(account);
        var caller = GameObject.Create("connect_caller");
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();

        var pa = new ConnectCommand().Parser!.ParseArgs(["connect_acc", "connect_pw"]);
        new ConnectCommand().Run(caller, pa);

        Assert.Contains("Welcome connect_acc.", string.Join("\n", caller.PeekMessages()));
    }

    [Fact]
    public void ConnectCommand_RunWithWrongPassword_ReportsInvalid()
    {
        using var env = GlobalTestEnv.Enter();
        var account = Account.Create("connect_acc2", "connect_correct");
        Assert.NotNull(account);
        var caller = GameObject.Create("connect_caller2");
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();

        var pa = new ConnectCommand().Parser!.ParseArgs(["connect_acc2", "connect_wrong"]);
        new ConnectCommand().Run(caller, pa);

        Assert.Contains("Invalid password.", string.Join("\n", caller.PeekMessages()));
    }

    [Fact]
    public async Task CharSelection_NumbersCharactersFromOne_AndPuppetsSelection()
    {
        using var env = GlobalTestEnv.Enter();
        var account = Account.Create("select_one", "select_pw");
        Assert.NotNull(account);
        var hero = GameObject.Create("hero", isPc: true);
        ObjectRegistry.AddObject(hero);
        account.AddCharacter(hero);
        var conn = new TestConnection("select_one_conn");

        var selection = ConnectCommand.CharSelectionAsync(conn, account);
        var prompt = await WaitForPromptAsync(conn, null);
        prompt.TrySetResult("1");
        await selection.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(hero, conn.Session!.Puppet);
        Assert.Contains("1. hero", SentText(conn));
    }

    [Fact]
    public async Task CharSelection_ChoiceZero_IsRejected()
    {
        using var env = GlobalTestEnv.Enter();
        var account = Account.Create("select_zero", "select_pw");
        Assert.NotNull(account);
        var hero = GameObject.Create("hero", isPc: true);
        ObjectRegistry.AddObject(hero);
        account.AddCharacter(hero);
        var conn = new TestConnection("select_zero_conn");

        var selection = ConnectCommand.CharSelectionAsync(conn, account);
        var firstPrompt = await WaitForPromptAsync(conn, null);
        firstPrompt.TrySetResult("0");
        var secondPrompt = await WaitForPromptAsync(conn, firstPrompt);
        secondPrompt.TrySetResult("1");
        await selection.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("Invalid choice.", SentText(conn));
        Assert.Same(hero, conn.Session!.Puppet);
    }
}
