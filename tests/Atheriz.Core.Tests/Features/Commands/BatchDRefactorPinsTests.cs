// Batch D (section 4.7 dual sync/async lifecycles) refactor pins: cancellable
// Session prompts, cancellable creation wizards, immutable channel
// construction, and the interface-dispatched quiet close. Each group fails if
// its production path regresses (verified by neutering during development).
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class BatchDRefactorPinsTests
{
    private sealed class OtherCaller : IMessageTarget
    {
        public void Msg(string text) { }
    }

    [Fact]
    public async Task Prompt_CancelledToken_CompletesPromptlyWithEmptyResult()
    {
        var session = new Session();
        using var cts = new CancellationTokenSource();
        var task = session.Prompt("batchd ask", false, cts.Token);
        Assert.False(task.IsCompleted);
        cts.Cancel();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("", result);
        Assert.Null(session.InputFuture);
    }

    [Fact]
    public async Task Prompt_AlreadyCancelledToken_ReturnsEmptyWithoutHanging()
    {
        var session = new Session();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await session.Prompt("batchd pre", false, cts.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("", result);
        Assert.Null(session.InputFuture);
    }

    [Fact]
    public async Task GuestRunAsync_CancelledToken_ReturnsWithoutThrowing()
    {
        using var env = GlobalTestEnv.Enter();
        var orig = AtherizSettings.Global.GuestEnabled;
        AtherizSettings.Global.GuestEnabled = true;
        try
        {
            var conn = new FakeConnection("batchd-guest");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ex = await Record.ExceptionAsync(() =>
                new GuestCommand().RunAsync(conn, cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Null(ex);
            Assert.Null(conn.Session.InputFuture);
        }
        finally { AtherizSettings.Global.GuestEnabled = orig; }
    }

    [Fact]
    public void ChannelCtor_CapturesChannel_MatchingLazyPath()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("BatchDChan");
        chan.Desc = "BatchD desc";
        var lazy = chan.GetCommand() as BaseChannelCommand;
        Assert.NotNull(lazy);
        var cmd = new BaseChannelCommand(chan);
        Assert.True(cmd.TryGetChannel(out var got));
        Assert.Same(chan, got);
        Assert.Equal(chan.Id, cmd.Id);
        Assert.Equal(chan.Id, cmd.id);
        Assert.Same(chan, cmd._channel);
        Assert.Equal(chan.Name.ToLowerInvariant(), cmd.Key);
        Assert.Equal(chan.Desc, cmd.Desc);
        Assert.Equal(lazy!.Key, cmd.Key);
        Assert.Equal(lazy.Desc, cmd.Desc);
    }

    [Fact]
    public void CloseQuietly_UnknownCallerShape_IsSilentNoop()
    {
        var ex = Record.Exception(() => ConnectionHelper.CloseQuietly(new OtherCaller()));
        Assert.Null(ex);
    }

    [Fact]
    public void CloseQuietly_SessionShapes_CloseExpectedConnection()
    {
        using var env = GlobalTestEnv.Enter();
        var puppet = GameObject.Create("BatchDQuitter", isPc: true);
        ObjectRegistry.AddObject(puppet);
        var conn = new TestConnection("batchd-quit");
        var sess = new Session(conn);
        puppet.Session = sess;
        sess.Puppet = puppet;
        ConnectionHelper.CloseQuietly(puppet);
        Assert.True(conn.Closed);

        var raw = new TestConnection("batchd-raw");
        ConnectionHelper.CloseQuietly(raw);
        Assert.True(raw.Closed);
    }
}
