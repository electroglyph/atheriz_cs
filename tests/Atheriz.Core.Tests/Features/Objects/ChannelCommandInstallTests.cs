using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Subscribing installs the channel command on the internal cmdset and
// unsubscribing removes it (base_obj.py:759-777).
[Collection("Ported")]
public class ChannelCommandInstallTests
{
    private static bool HasChannelCmd(GameObject go, string key)
        => go.InternalCmdSet?.GetAll().Any(c => c.Key == key) == true;

    [Fact]
    public void Subscribe_Installs_Unsubscribe_Removes()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var channel = Channel.Create("installchan");
            var go = GameObject.Create("fan", isPc: true);
            ObjectRegistry.AddObject(go);
            go.IsConnected = true;
            Assert.False(HasChannelCmd(go, "installchan"));
            go.Subscribe(channel);
            Assert.True(HasChannelCmd(go, "installchan"));
            go.Unsubscribe(channel);
            Assert.False(HasChannelCmd(go, "installchan"));
            Assert.Empty(go.ChannelsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
