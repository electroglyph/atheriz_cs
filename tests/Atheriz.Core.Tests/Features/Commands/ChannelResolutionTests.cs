using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Channel resolution throws one identical type+message from a single site
// for every not-found shape (deleted cache, non-channel hit, total miss),
// and Run reports the same user message for all three.
[Collection("Ported")]
public sealed class ChannelResolutionTests
{
    [Fact]
    public void MissingId_ThrowsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new BaseChannelCommand { id = 27481 };
        var ex = Assert.Throws<InvalidOperationException>(() => cmd.channel);
        Assert.Equal("Channel 27481 not found.", ex.Message);
        Assert.False(cmd.TryGetChannel(out _));
    }

    [Fact]
    public void NonChannelHit_ThrowsSameNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("notachan");
        ObjectRegistry.AddObject(obj);
        var cmd = new BaseChannelCommand { id = obj.Id };
        var ex = Assert.Throws<InvalidOperationException>(() => cmd.channel);
        Assert.Equal($"Channel {obj.Id} not found.", ex.Message);
        Assert.False(cmd.TryGetChannel(out _));
    }

    [Fact]
    public void DeletedCachedChannel_ThrowsSameNotFound_AndDropsCache()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("pinchan");
        var cmd = new BaseChannelCommand();
        cmd.channel = chan;
        chan.IsDeleted = true;
        var ex = Assert.Throws<InvalidOperationException>(() => cmd.channel);
        Assert.Equal($"Channel {chan.Id} not found.", ex.Message);
        Assert.Null(cmd._channel);
        Assert.False(cmd.TryGetChannel(out _));
    }

    [Fact]
    public void LiveChannel_Resolves()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("pinchanlive");
        var cmd = new BaseChannelCommand();
        cmd.channel = chan;
        Assert.True(cmd.TryGetChannel(out var got));
        Assert.Same(chan, got);
        Assert.Same(chan, cmd.channel);
    }

    [Fact]
    public void Run_MissingChannel_ReportsGone()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new BaseChannelCommand { id = 27482 };
        var puppet = new GameObject { Name = "Hero" };
        puppet.ClearMessages();
        cmd.Run(puppet, new GameArgumentParser.ParsedArgs());
        Assert.Contains("That channel no longer exists.", puppet.PeekMessages());
    }
}
