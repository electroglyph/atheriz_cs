using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Network;

// Re-registering the same connection object under a new id must evict the
// stale id instead of double-counting the per-IP bucket and orphaning a
// slot on disconnect.
[Collection("Ported")]
public class ConnectionReregisterTests
{
    [Fact]
    public void RegisterConnection_SameObjectNewId_KeepsSingleSlot()
    {
        ObjectRegistry.ClearAll();
        var mgr = new ConnectionManager(settings: new AtherizSettings { MaxConnectionsPerIp = 2 });
        var c = new TestConn("id1", "1.1.1.1");
        Assert.True(mgr.RegisterConnection("id1", c));
        Assert.True(mgr.RegisterConnection("id2", c));
        // Only one per-IP slot is held: a second same-host connection fits.
        var c2 = new TestConn("id3", "1.1.1.1");
        Assert.True(mgr.RegisterConnection("id3", c2));
        // Disconnecting the re-registered object frees its slot entirely —
        // no orphaned id leaks a phantom count afterward.
        mgr.Disconnect(c);
        var c3 = new TestConn("id4", "1.1.1.1");
        Assert.True(mgr.RegisterConnection("id4", c3));
        mgr.Disconnect(c2);
        mgr.Disconnect(c3);
    }
}
