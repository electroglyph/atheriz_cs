using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Network;

// Disconnect during drain clears the queue without invoking handlers: the
// single remaining _disconnected test owns that outcome.
[Collection("Ported")]
public sealed class BaseConnectionDrainDisconnectTests
{
    [Fact]
    public void DisconnectDuringDrain_DropsQueuedHandlers()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager(pool: new AsyncThreadPool(maxThreads: 2, queueLimit: 100));
        ConnectionManager.GlobalInstance = mgr;
        var conn = new TestConnection();
        var blockerStarted = new ManualResetEventSlim(false);
        var blockerRelease = new ManualResetEventSlim(false);
        int markersRan = 0;
        Action<BaseConnection, List<object?>, Dictionary<string, object?>> blocker = (_, _, _) =>
        {
            blockerStarted.Set();
            blockerRelease.Wait(TimeSpan.FromSeconds(10));
        };
        Action<BaseConnection, List<object?>, Dictionary<string, object?>> marker = (_, _, _) =>
            Interlocked.Increment(ref markersRan);
        try
        {
            conn.EnqueueInput(blocker, [], []);
            Assert.True(blockerStarted.Wait(5000), "blocker handler did not start");
            conn.EnqueueInput(marker, [], []);
            conn.SetDisconnected(true);
            blockerRelease.Set();
            // The worker finishes the blocker, then sees the disconnect and
            // clears the queue instead of running the marker.
            Thread.Sleep(500);
            Assert.Equal(0, Volatile.Read(ref markersRan));
        }
        finally
        {
            blockerRelease.Set();
            ConnectionManager.GlobalInstance = null;
            mgr.Atp.Stop(wait: false);
        }
    }
}
