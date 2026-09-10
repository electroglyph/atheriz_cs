using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Type-switches replace the `is`-chains with identical branch outcomes: the
// unlogged-in enable gates answer per verb, and quit says goodbye on every
// caller shape.
[Collection("Ported")]
public sealed class CommandTypeSwitchTests
{
    private sealed class OtherCmd : Command
    {
        public override string Key => "zzother";
        public override bool UseParser => false;
        public override void Run(IMessageTarget caller, object? args) { }
    }

    // A caller shaped like a bare session (Session itself is not an
    // IMessageTarget, so the test double inherits it and adds the seam).
    private sealed class SessionCaller : Session, IMessageTarget
    {
    }

    [Fact]
    public void IsUnloggedInEnabled_DefaultSettings_AllVerbsEnabled()
    {
        var g = AtherizSettings.Global;
        bool oa = g.AccountCreationEnabled, oc = g.CharCreationEnabled, og = g.GuestEnabled;
        g.AccountCreationEnabled = true; g.CharCreationEnabled = true; g.GuestEnabled = true;
        CommandDispatcher.SetSettings(new AtherizSettings());
        try
        {
            Assert.True(CommandDispatcher.IsUnloggedInEnabled(new CreateAccountCommand()));
            Assert.True(CommandDispatcher.IsUnloggedInEnabled(new NewCharacterCommand()));
            Assert.True(CommandDispatcher.IsUnloggedInEnabled(new GuestCommand()));
            Assert.True(CommandDispatcher.IsUnloggedInEnabled(new OtherCmd()));
        }
        finally
        {
            CommandDispatcher.SetSettings(new AtherizSettings());
            g.AccountCreationEnabled = oa; g.CharCreationEnabled = oc; g.GuestEnabled = og;
        }
    }

    [Fact]
    public void IsUnloggedInEnabled_DisabledVerb_GatedOffAlone()
    {
        var g = AtherizSettings.Global;
        bool oa = g.AccountCreationEnabled, oc = g.CharCreationEnabled, og = g.GuestEnabled;
        g.AccountCreationEnabled = true; g.CharCreationEnabled = true; g.GuestEnabled = true;
        CommandDispatcher.SetSettings(new AtherizSettings { AccountCreationEnabled = false });
        try
        {
            Assert.False(CommandDispatcher.IsUnloggedInEnabled(new CreateAccountCommand()));
            Assert.True(CommandDispatcher.IsUnloggedInEnabled(new NewCharacterCommand()));
            Assert.True(CommandDispatcher.IsUnloggedInEnabled(new GuestCommand()));
        }
        finally
        {
            CommandDispatcher.SetSettings(new AtherizSettings());
            g.AccountCreationEnabled = oa; g.CharCreationEnabled = oc; g.GuestEnabled = og;
        }
    }

    [Fact]
    public void QuitCommand_Run_SaysGoodbye_ForGameObject()
    {
        var puppet = new GameObject { Name = "Hero" };
        puppet.ClearMessages();
        new Atheriz.Core.Commands.LoggedIn.QuitCommand().Run(puppet, null);
        Assert.Contains("Goodbye!", puppet.PeekMessages());
    }

    [Fact]
    public void QuitCommand_Run_ClosesConnection_ForSessionCaller()
    {
        var conn = new TestConnection();
        var sess = new SessionCaller { Connection = conn };
        new Atheriz.Core.Commands.LoggedIn.QuitCommand().Run(sess, null);
        Assert.True(conn.Closed);
    }

    [Fact]
    public void QuitCommand_Run_SaysGoodbye_AndCloses_ForConnection()
    {
        var conn = new TestConnection();
        new Atheriz.Core.Commands.LoggedIn.QuitCommand().Run(conn, null);
        Assert.True(conn.Closed);
        Assert.Contains(conn.SentCommands, c => c == "text");
    }
}
