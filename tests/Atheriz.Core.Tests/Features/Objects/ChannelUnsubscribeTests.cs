using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Unsubscribe must remove exactly once and dirty-mark only when an entry was
// actually present; a second call is a clean no-op.
[Collection("Ported")]
public class ChannelUnsubscribeTests
{
    [Fact]
    public void Unsubscribe_RemovesEntryAndMarksModified()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var channel = Channel.Create("leavable");
            var go = GameObject.Create("leaver", isPc: true);
            ObjectRegistry.AddObject(go);
            go.IsModified = false;
            go.Subscribe(channel);
            Assert.Contains(channel.Id, go.ChannelsSnapshot);
            go.IsModified = false;
            go.Unsubscribe(channel);
            Assert.DoesNotContain(channel.Id, go.ChannelsSnapshot);
            Assert.True(go.IsModified);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Unsubscribe_AbsentChannel_LeavesCleanFlag()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var channel = Channel.Create("neverjoined");
            var go = GameObject.Create("stranger", isPc: true);
            ObjectRegistry.AddObject(go);
            go.IsModified = false;
            go.Unsubscribe(channel);
            Assert.False(go.IsModified);
            Assert.Empty(go.ChannelsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Unsubscribe_Twice_SecondCallIsCleanNoop()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var channel = Channel.Create("twiceleft");
            var go = GameObject.Create("twiceleaver", isPc: true);
            ObjectRegistry.AddObject(go);
            go.Subscribe(channel);
            go.Unsubscribe(channel);
            go.IsModified = false;
            go.Unsubscribe(channel);
            Assert.False(go.IsModified);
            Assert.Empty(go.ChannelsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
