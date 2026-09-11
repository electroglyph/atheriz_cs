// Unlogged-in quit close: "Goodbye!" stays at the
// call site and every caller shape closes exactly what the old inline code
// closed, via the shared quiet-close.
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class QuitCloseTests
{
    private sealed class OtherCaller : IMessageTarget
    {
        public void Msg(string text) { }
    }

    // Session itself is not an IMessageTarget; shape the double like the
    // CommandTypeSwitchTests precedent (inherit + add the seam).
    private sealed class SessionCaller : Session, IMessageTarget
    {
        public SessionCaller(TestConnection conn) : base(conn) { }
    }

    [Fact]
    public void UnloggedQuit_GameObjectCaller_GoodbyeFirstThenClosesConnection()
    {
        using var env = GlobalTestEnv.Enter();
        var puppet = GameObject.Create("b20a_quitter", isPc: true);
        ObjectRegistry.AddObject(puppet);
        var conn = new TestConnection("b20a-quit");
        var sess = new Session(conn);
        puppet.Session = sess;
        sess.Puppet = puppet;
        puppet.ClearMessages();
        new Atheriz.Core.Commands.UnloggedIn.QuitCommand().Run(puppet, null);
        Assert.Contains("Goodbye!", puppet.PeekMessages());
        Assert.True(conn.Closed);
    }

    [Fact]
    public void UnloggedQuit_SessionCaller_ClosesConnection()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("b20a-quit-sess");
        var sess = new SessionCaller(conn);
        new Atheriz.Core.Commands.UnloggedIn.QuitCommand().Run(sess, null);
        Assert.True(conn.Closed);
    }

    [Fact]
    public void UnloggedQuit_RawConnectionCaller_ClosesIt()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection("b20a-quit-raw");
        new Atheriz.Core.Commands.UnloggedIn.QuitCommand().Run(conn, null);
        Assert.True(conn.Closed);
    }

    [Fact]
    public void UnloggedQuit_UnknownCallerShape_IsSilentNoop()
    {
        using var env = GlobalTestEnv.Enter();
        var ex = Record.Exception(() => new Atheriz.Core.Commands.UnloggedIn.QuitCommand().Run(new OtherCaller(), null));
        Assert.Null(ex);
    }

    [Fact]
    public void LoggedQuit_GameObjectCaller_MatchesUnloggedClose()
    {
        using var env = GlobalTestEnv.Enter();
        var puppet = GameObject.Create("b20a_loggedquit", isPc: true);
        ObjectRegistry.AddObject(puppet);
        var conn = new TestConnection("b20a-loggedquit");
        var sess = new Session(conn);
        puppet.Session = sess;
        sess.Puppet = puppet;
        puppet.ClearMessages();
        new QuitCommand().Run(puppet, null);
        Assert.Contains("Goodbye!", puppet.PeekMessages());
        Assert.True(conn.Closed);
    }
}
