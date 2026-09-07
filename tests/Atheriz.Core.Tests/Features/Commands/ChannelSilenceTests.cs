using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// Unsubscribing is silent (channel.py:110-111) and a bare channel selection
// with no message falls through silently (channel.py:122 elif on []).
[Collection("Ported")]
public class ChannelSilenceTests
{
    [Fact]
    public void Unsubscribe_SendsNoMessage()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var channel = Channel.Create("quietchan");
            var go = GameObject.Create("listener", isPc: true);
            ObjectRegistry.AddObject(go);
            go.IsConnected = true;
            go.Subscribe(channel);
            go.ClearMessages();
            var cmd = new ChannelCommand();
            cmd.Run(go, cmd.Parser!.ParseArgs(new[] { "-c", "quietchan", "-u" }));
            Assert.DoesNotContain("Unsubscribed", string.Join(" ", go.PeekMessages()));
            Assert.Empty(go.ChannelsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void BareChannelSelection_SendsNothing()
    {
        ObjectRegistry.ClearAll();
        try
        {
            Channel.Create("barechan");
            var go = GameObject.Create("reader", isPc: true);
            ObjectRegistry.AddObject(go);
            go.IsConnected = true;
            go.ClearMessages();
            var cmd = new ChannelCommand();
            cmd.Run(go, cmd.Parser!.ParseArgs(new[] { "-c", "barechan" }));
            Assert.Empty(go.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
