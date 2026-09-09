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

    [Fact]
    public void ConcurrentSubscribe_BothCommandsSurvive()
    {
        // Two racing Subscribes both saw a null InternalCmdSet, both
        // allocated, and the second orphaned the first channel's command.
        // The set itself is allocated under the peer write lock now.
        ObjectRegistry.ClearAll();
        try
        {
            var chA = Channel.Create("racechana");
            var chB = Channel.Create("racechanb");
            var go = GameObject.Create("racefan", isPc: true);
            ObjectRegistry.AddObject(go);
            go.IsConnected = true;
            var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
            {
                go.Subscribe(i % 2 == 0 ? chA : chB);
            })).ToArray();
            Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(30)));
            Assert.True(HasChannelCmd(go, "racechana"));
            Assert.True(HasChannelCmd(go, "racechanb"));
            Assert.Contains(chA.Id, go.ChannelsSnapshot);
            Assert.Contains(chB.Id, go.ChannelsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
