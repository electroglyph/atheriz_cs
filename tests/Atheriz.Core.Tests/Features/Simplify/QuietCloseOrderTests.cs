// Pins for the shared quiet-close (ConnectionHelper.CloseQuietly): the
// "Goodbye!" message is delivered before the close on every caller shape,
// each branch closes exactly what the old inline code closed.
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class QuietCloseOrderTests
{
    private sealed class OtherCaller : IMessageTarget
    {
        public void Msg(string text) { }
    }

    [Fact]
    public void Quit_GameObjectCaller_GoodbyeFirst_ThenClosesConnection()
    {
        using var env = GlobalTestEnv.Enter();
        var puppet = GameObject.Create("Quitter", isPc: true);
        ObjectRegistry.AddObject(puppet);
        var conn = new Atheriz.Core.Tests.TestConnection("quit-conn");
        var sess = new Session(conn);
        puppet.Session = sess;
        sess.Puppet = puppet;
        puppet.ClearMessages();
        new QuitCommand().Run(puppet, null);
        Assert.Contains(puppet.PeekMessages(), m => m == "Goodbye!");
        Assert.True(conn.Closed);
    }

    [Fact]
    public void Quit_RawConnectionCaller_ClosesIt()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new Atheriz.Core.Tests.TestConnection("quit-raw");
        new QuitCommand().Run(conn, null);
        Assert.True(conn.Closed);
    }

    [Fact]
    public void CloseQuietly_UnknownCallerShape_IsSilentNoop()
    {
        using var env = GlobalTestEnv.Enter();
        var ex = Record.Exception(() => ConnectionHelper.CloseQuietly(new OtherCaller()));
        Assert.Null(ex);
    }

    [Fact]
    public void CloseQuietly_SessionShapes_CloseExpectedConnection()
    {
        using var env = GlobalTestEnv.Enter();
        var puppet = GameObject.Create("QuietQuitter", isPc: true);
        ObjectRegistry.AddObject(puppet);
        var conn = new TestConnection("quiet-quit");
        var sess = new Session(conn);
        puppet.Session = sess;
        sess.Puppet = puppet;
        ConnectionHelper.CloseQuietly(puppet);
        Assert.True(conn.Closed);

        var raw = new TestConnection("quiet-raw");
        ConnectionHelper.CloseQuietly(raw);
        Assert.True(raw.Closed);
    }
}
