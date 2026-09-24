using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// The channel-capturing constructor matches the lazy lookup path.
[Collection("Ported")]
public sealed class BaseChannelCommandTests
{
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
}
