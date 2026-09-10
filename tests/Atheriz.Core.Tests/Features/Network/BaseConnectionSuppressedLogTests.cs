using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Network;

// Suppressed-log wrappers keep every failure-region shape: the pool-full
// busy path still notifies, and dispose stays idempotent.
[Collection("Ported")]
public sealed class BaseConnectionSuppressedLogTests
{
    [Fact]
    public void EnqueueInput_StoppedPool_SendsBusyNotification()
    {
        using var env = GlobalTestEnv.Enter();
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 10);
        pool.Stop(wait: false);
        var mgr = PortedHelpers.MakeManager(pool: pool);
        ConnectionManager.GlobalInstance = mgr;
        var conn = new TestConnection();
        try
        {
            Action<BaseConnection, List<object?>, Dictionary<string, object?>> handler = (_, _, _) => { };
            conn.EnqueueInput(handler, [], []);
            // Msg appends \r\n (pre-existing connection.py:192-198 tail), so
            // match by containment, not equality.
            Assert.True(PortedHelpers.WaitFor(
                () => conn.Sent.Exists(s => s.Args.Exists(a => (a?.ToString() ?? "").Contains("Server busy; input dropped."))), 5000),
                "busy notification was not sent on pool failure");
        }
        finally
        {
            conn.SetDisconnected(true);
            conn.ClearPendingInput();
            ConnectionManager.GlobalInstance = null;
            mgr.Atp.Stop(wait: false);
        }
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConnection();
        conn.Dispose();
        conn.Dispose();
    }
}
