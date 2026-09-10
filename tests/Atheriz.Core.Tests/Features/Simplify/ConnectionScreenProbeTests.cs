using Atheriz.Core.Commands;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// Static gate probes: GuestCommand/CreateAccountCommand are stateless
// (get-only Key/Desc; IsUnloggedInEnabled does type tests only, Run never
// invoked), so shared instances render byte-identical hints.
[Collection("Ported")]
public class ConnectionScreenProbeTests
{
    [Fact]
    public void ProbeCommands_AreStateless()
    {
        var g = new GuestCommand();
        Assert.Equal("guest", g.Key);
        Assert.Equal("Create a temporary guest character and enter the game.", g.Desc);
        var c = new CreateAccountCommand();
        Assert.Equal("create", c.Key);
        Assert.Equal("Create a new account.", c.Desc);
    }

    [Fact]
    public void Render_HintsFollowSettingsAndGate()
    {
        using var env = GlobalTestEnv.Enter();
        var g = AtherizSettings.Global;
        bool og = g.GuestEnabled, oc = g.AccountCreationEnabled;
        g.GuestEnabled = true;
        g.AccountCreationEnabled = true;
        CommandDispatcher.SetSettings(new AtherizSettings());
        var sess = new Session { ScreenReader = true };
        try
        {
            string full = ConnectionScreen.Render(new AtherizSettings(), sess);
            Assert.Contains("enter 'guest' to create a temporary character", full);
            Assert.Contains("enter 'create' to make a new account", full);
            var off = new AtherizSettings { GuestEnabled = false, AccountCreationEnabled = false };
            string bare = ConnectionScreen.Render(off, sess);
            Assert.DoesNotContain("'guest'", bare);
            Assert.DoesNotContain("'create'", bare);
        }
        finally
        {
            g.GuestEnabled = og;
            g.AccountCreationEnabled = oc;
            CommandDispatcher.SetSettings(new AtherizSettings());
        }
    }

    [Fact]
    public void Probes_AreSharedStatics()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "ConnectionScreen.cs");
        Assert.Contains("static readonly", src);
        Assert.Contains("GuestProbe", src);
        Assert.Contains("CreateProbe", src);
        Assert.DoesNotContain("new Commands.UnloggedIn.GuestCommand()", src);
        Assert.DoesNotContain("new Commands.UnloggedIn.CreateAccountCommand()", src);
    }
}
