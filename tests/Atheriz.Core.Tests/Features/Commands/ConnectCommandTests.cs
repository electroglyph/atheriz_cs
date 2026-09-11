// Connect double-cast removal: the direct cast throws InvalidCastException
// for non-Account rows exactly like the old as-plus-explicit pair, and valid
// logins still welcome.
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class ConnectCommandTests
{
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
}
