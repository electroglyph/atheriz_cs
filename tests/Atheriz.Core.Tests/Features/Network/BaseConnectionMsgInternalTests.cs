using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Network;

// MsgInternal kwargs handling without the second list alloc: the caller's
// list is copied once, text= roots args, otherwise the last kwarg pops to
// the command with its value rooted at args[0].
[Collection("Ported")]
public sealed class BaseConnectionMsgInternalTests
{
    [Fact]
    public void PromptKwarg_BecomesCommand_WithValueRooted()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        conn.MsgKw(new Dictionary<string, object?> { ["prompt"] = ">" });
        Assert.Single(conn.Sent);
        Assert.Equal("prompt", conn.Sent[0].Cmd);
        Assert.Equal([">"], conn.Sent[0].Args);
        Assert.Empty(conn.Sent[0].Kwargs);
    }

    [Fact]
    public void TextKwarg_RootsArgs_StaysTextCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        conn.MsgKw(new Dictionary<string, object?> { ["text"] = "hi" });
        Assert.Single(conn.Sent);
        Assert.Equal("text", conn.Sent[0].Cmd);
        Assert.Equal("hi\r\n", conn.Sent[0].Args[0]);
    }

    [Fact]
    public void MultiKwarg_PopsLastKey_KeepingRest()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        conn.MsgKw(new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 });
        Assert.Single(conn.Sent);
        Assert.Equal("b", conn.Sent[0].Cmd);
        Assert.Equal([2], conn.Sent[0].Args);
        Assert.Equal(new Dictionary<string, object?> { ["a"] = 1 }, conn.Sent[0].Kwargs);
    }

    [Fact]
    public void CallerArgsList_NotAliasedOrMutated()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        var caller = new List<object?> { "x" };
        conn.Msg(caller, new Dictionary<string, object?> { ["prompt"] = ">" });
        Assert.Single(caller);
        Assert.Equal("x", caller[0]);
        var sent = conn.Sent.Single(s => s.Cmd == "prompt");
        Assert.Equal(2, sent.Args.Count);
        Assert.Equal(">", sent.Args[0]);
        Assert.False(ReferenceEquals(caller, sent.Args));
    }
}
