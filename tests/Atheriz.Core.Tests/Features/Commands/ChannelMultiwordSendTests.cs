using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Per-channel shortcut verbs accept multi-word messages like the `channel`
// verb: the message argument is ZeroOrMore, joined with single spaces.
[Collection("Ported")]
public sealed class ChannelMultiwordSendTests
{
    private static GameObject MakePc(string name)
    {
        var go = GameObject.Create(name, isPc: true);
        ObjectRegistry.AddObject(go);
        go.IsConnected = true;
        go.ClearMessages();
        return go;
    }

    [Fact]
    public void BaseChannelCommand_MultiWordMessage_ParsesAndSendsWholeText()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("o1chan");
        var cmd = chan.GetCommand();
        Assert.NotNull(cmd);
        var caller = MakePc("o1_sender");
        var before = chan.History.Count;
        // Would throw CommandError (unrecognized arguments: world) when the
        // message argument was a single optional token.
        cmd!.Run(caller, cmd.Parser!.ParseArgs(["hello", "world"]));
        Assert.Equal(before + 1, chan.History.Count);
        Assert.Contains("hello world", chan.History);
        Assert.DoesNotContain("unrecognized arguments", string.Join("\n", caller.PeekMessages()));
    }

    [Fact]
    public void BaseChannelCommand_EmptyMessage_ShowsHelp()
    {
        // No message words still falls through to help (established
        // contract, unchanged by the multi-word fix).
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("o1empty");
        var cmd = chan.GetCommand();
        Assert.NotNull(cmd);
        var caller = MakePc("o1_quiet");
        var before = chan.History.Count;
        cmd!.Run(caller, cmd.Parser!.ParseArgs([]));
        Assert.Equal(before, chan.History.Count);
    }
}
