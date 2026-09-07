using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Globals;

// Lifecycle hooks log but send no channel broadcast (server_events.py:8-16
// pass); character creation prints/logs only (server_events.py:96).
[Collection("Ported")]
public class ServerEventSilenceTests
{
    [Fact]
    public void LifecycleHooks_BroadcastNothing()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var channel = Channel.Create("Server");
            var builder = GameObject.Create("watcher", isPc: true);
            ObjectRegistry.AddObject(builder);
            builder.IsConnected = true;
            builder.Subscribe(channel);
            builder.ClearMessages();
            ServerEvents.AtServerStart();
            ServerEvents.AtServerReload();
            Assert.DoesNotContain(builder.PeekMessages(), m => m.Contains("starting") || m.Contains("reloading"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
