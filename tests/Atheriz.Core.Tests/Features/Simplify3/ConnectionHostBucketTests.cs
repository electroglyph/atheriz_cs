// Pins for the HostOf entry: per-IP accounting and disconnect use the
// registration-time host snapshot, so overwrites from the same host are not
// double-counted and a disconnect frees the registered bucket even after the
// live host changes.
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class ConnectionHostBucketTests
{
    private static TestConnection Conn(string host)
    {
        var c = new TestConnection();
        c.ClientHost = host;
        return c;
    }

    private static ConnectionManager Manager(int perIpLimit = 1)
    {
        var settings = new AtherizSettings { MaxConnectionsPerIp = perIpLimit };
        return PortedHelpers.MakeManager(settings);
    }

    [Fact]
    public void RegisterConnection_SameIdSameHostOverwrite_KeepsSingleCount()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = Manager();
        try
        {
            var first = Conn("10.0.0.1");
            Assert.True(mgr.RegisterConnection("c1", first));
            // Same conn id, same host: replaces without double-counting,
            // so the bucket still holds exactly one registration.
            var replacement = Conn("10.0.0.1");
            Assert.True(mgr.RegisterConnection("c1", replacement));
            Assert.Equal(1, mgr.ConnectionCount);
            var overflow = Conn("10.0.0.1");
            Assert.False(mgr.RegisterConnection("c2", overflow));
            Assert.True(overflow.Closed);
        }
        finally
        {
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = null;
        }
    }

    [Fact]
    public void Disconnect_AfterClientHostChange_DecrementsRegisteredBucket()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = Manager();
        try
        {
            var conn = Conn("10.0.0.1");
            Assert.True(mgr.RegisterConnection("a", conn));
            // The live host moves after registration; disconnect must still
            // free the registration-time bucket.
            conn.ClientHost = "10.9.9.9";
            mgr.Disconnect(conn);
            Assert.Equal(0, mgr.ConnectionCount);
            var next = Conn("10.0.0.1");
            Assert.True(mgr.RegisterConnection("b", next));
            mgr.Disconnect(next);
        }
        finally
        {
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = null;
        }
    }

    [Fact]
    public void RegisterConnection_OverwriteDifferentHost_FreesOldBucket()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = Manager();
        try
        {
            var first = Conn("10.0.0.1");
            Assert.True(mgr.RegisterConnection("c1", first));
            var takeover = Conn("10.0.0.2");
            Assert.True(mgr.RegisterConnection("c1", takeover));
            Assert.Equal(1, mgr.ConnectionCount);
            // The old host bucket was released by the overwrite.
            var revived = Conn("10.0.0.1");
            Assert.True(mgr.RegisterConnection("c3", revived));
            Assert.Equal(2, mgr.ConnectionCount);
            mgr.Disconnect(takeover);
            mgr.Disconnect(revived);
        }
        finally
        {
            mgr.Atp.Stop(wait: false);
            ConnectionManager.GlobalInstance = null;
        }
    }
}
