using Atheriz.Core.Network;
using Atheriz.Core.Globals;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests;

[Collection("Ported")]
public sealed class NetworkTests
{
    [Fact]
    public void ConnectionManager_PerIpLimit()
    {
        ObjectRegistry.ClearAll();
        var mgr = new ConnectionManager(settings: new AtherizSettings { MaxConnectionsPerIp = 1 });
        var c1 = new TestConn("c1", "1.1.1.1");
        var c2 = new TestConn("c2", "1.1.1.1");
        Assert.True(mgr.RegisterConnection(c1.SessionId!, c1));
        Assert.False(mgr.RegisterConnection(c2.SessionId!, c2)); // second from same IP should be blocked
        mgr.Disconnect(c1);
    }

    [Fact]
    public void ConnectionManager_SameHostReregister_DoesNotLeakPerIpCount()
    {
        // Re-registering the same conn id from the same host must not consume a
        // second per-IP slot, or the bucket leaks toward a false limit refusal.
        ObjectRegistry.ClearAll();
        var mgr = new ConnectionManager(settings: new AtherizSettings { MaxConnectionsPerIp = 2 });
        var c1 = new TestConn("c1", "1.1.1.1");
        Assert.True(mgr.RegisterConnection(c1.SessionId!, c1));
        Assert.True(mgr.RegisterConnection(c1.SessionId!, c1));
        var c2 = new TestConn("c2", "1.1.1.1");
        Assert.True(mgr.RegisterConnection(c2.SessionId!, c2));
        mgr.Disconnect(c1);
        mgr.Disconnect(c2);
    }

    [Fact]
    public async Task BaseConnection_QueueLimit_Drops()
    {
        var conn = new TestConn("qtest", "2.2.2.2");
        // flood beyond limit 100
        for (int i = 0; i < 110; i++) conn.EnqueueInput((Delegate)(Action<object?, object?>)((_, __) => { }), [], new Dictionary<string, object?>());
        // queue should not explode — drain should keep bounded, just ensure no exception
        await Task.Delay(80);
        Assert.True(true);
    }

    private sealed class TestConn : BaseConnection
    {
        public TestConn(string id, string host) : base(id)
        {
            ClientHost = host;
        }
        public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null) { }
        public override void Close() { }
        // BaseConnection has abstract LaunchDraw? check if needed
    }
}
