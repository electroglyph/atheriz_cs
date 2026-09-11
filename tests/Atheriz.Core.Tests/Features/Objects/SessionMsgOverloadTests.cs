// Session.Msg overloads: the text-only overload delegates to the typed one,
// preserving the null-connection no-op and the msgType wire mapping.
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class SessionMsgOverloadTests
{
    [Fact]
    public void Msg_NullConnection_IsNoOp()
    {
        using var env = GlobalTestEnv.Enter();
        var session = new Session();
        Assert.Null(Record.Exception(() => session.Msg("hi")));
        Assert.Null(Record.Exception(() => session.Msg("hi", "prompt")));
    }

    [Fact]
    public void Msg_TextOnly_SendsTextCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        var session = new Session(connection: conn);
        session.Msg("hi");
        var sent = conn.Sent;
        Assert.Single(sent);
        Assert.Equal("text", sent[0].Cmd);
        Assert.Contains("hi", sent[0].Args[0]?.ToString() ?? "");
    }

    [Fact]
    public void Msg_WithMsgType_SendsNamedCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        var session = new Session(connection: conn);
        session.Msg("hi", "prompt");
        var sent = conn.Sent;
        Assert.Single(sent);
        Assert.Equal("prompt", sent[0].Cmd);
        Assert.Equal("hi", sent[0].Args[0]);
    }
}
