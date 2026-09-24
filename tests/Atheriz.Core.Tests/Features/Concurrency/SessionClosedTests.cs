using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// A session past AtDisconnect is closed: it attaches no new puppet and its
// text path dispatches nothing, so late input cannot act on a dead session.
// AtConnect clears the flag for session reuse.
[Collection("Ported")]
public class SessionClosedTests
{
    [Fact]
    public void Closed_SetByDisconnect_ClearedByConnect()
    {
        var session = new Session();
        Assert.False(session.Closed);
        session.AtDisconnect();
        Assert.True(session.Closed);
        session.AtConnect();
        Assert.False(session.Closed);
    }

    [Fact]
    public void TryAttach_ClosedSession_RefusesNotAvailable()
    {
        var conn = new TestConnection("closed-attach");
        conn.Session.AtDisconnect();
        var character = new GameObject { Name = "ClosedChar" };

        Assert.False(SessionPuppetHelper.TryAttach(conn, character));
        Assert.Contains(conn.Sent, s => s.Cmd == "text"
            && s.Args.Count > 0
            && s.Args[0]?.ToString()?.Contains("not available") == true);
        Assert.Null(conn.Session.Puppet);
    }

    [Fact]
    public void TryAttach_OpenSession_Attaches()
    {
        // Positive control: the flag does not break normal attach.
        var conn = new TestConnection("open-attach");
        var character = new GameObject { Name = "OpenChar" };

        Assert.True(SessionPuppetHelper.TryAttach(conn, character));
        Assert.Same(character, conn.Session.Puppet);
        Assert.Same(conn.Session, character.Session);
    }

    [Fact]
    public void Text_ClosedSession_DropsDispatch()
    {
        // A puppet reference landing on a dead session (attach/teardown race)
        // must not dispatch: late input is dropped.
        var funcs = new InputFuncs();
        var conn = new TestConnection("closed-text");
        var character = new GameObject { Name = "ClosedText" };
        conn.Session.Puppet = character;
        conn.Session.AtDisconnect();
        // Stale attach racing the teardown: puppet set, session dead.
        conn.Session.Puppet = character;
        character.ClearMessages();

        funcs.Text(conn, new List<object?> { "look" }, new Dictionary<string, object?>());

        Assert.Empty(character.PeekMessages());
    }
}
