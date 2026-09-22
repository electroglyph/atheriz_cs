using System.Reflection;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Network;

// Suppressed-log wrappers keep every failure-region shape: the pool-full
// busy path still notifies, and dispose stays idempotent.
[Collection("Ported")]
public sealed class BaseConnectionSuppressedLogTests
{
    [Fact]
    public void EnqueueInput_StoppedPool_SendsQueuedRetryNotification()
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
            // The message is queued with a retry armed (not dropped), so the
            // drain-failure path reports queued/retrying. Msg appends \r\n
            // (pre-existing connection.py:192-198 tail), so match by
            // containment, not equality.
            Assert.True(PortedHelpers.WaitFor(
                () => conn.Sent.Exists(s => s.Args.Exists(a => (a?.ToString() ?? "").Contains("Server busy; input queued; retrying."))), 5000),
                "queued/retrying notification was not sent on pool failure");
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

    private static void SetInputRunning(BaseConnection c, bool v)
    {
        var f = typeof(BaseConnection).GetField("_inputRunning", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f);
        f!.SetValue(c, v);
    }

    private static System.Collections.ICollection GetInputQueue(BaseConnection c)
    {
        var f = typeof(BaseConnection).GetField("_inputQueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (System.Collections.ICollection)f.GetValue(c)!;
    }

    private static bool SentContains(TestConnection c, string text)
        => c.Sent.Exists(s => s.Args.Exists(a => (a?.ToString() ?? "").Contains(text, StringComparison.Ordinal)));

    [Fact]
    public void EnqueueInput_QueueFull_ReportsDroppedNotRetry()
    {
        // The queue-full path drops the message: the client must hear
        // "dropped", never "pending retry".
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        ConnectionManager.GlobalInstance = mgr;
        var conn = new TestConnection();
        try
        {
            // Hold the drain so fills stay queued without touching the
            // throttle window or the pool.
            SetInputRunning(conn, true);
            Action<BaseConnection, List<object?>, Dictionary<string, object?>> noop = (_, _, _) => { };
            int cap = new AtherizSettings().ConnectionInputQueueLimit;
            for (int i = 0; i < cap; i++) conn.EnqueueInput(noop, [], []);
            Assert.Equal(cap, GetInputQueue(conn).Count);
            string log;
            using (var capLog = new CaptureAtherizLog())
            {
                conn.EnqueueInput(noop, [], []);
                Assert.True(PortedHelpers.WaitFor(() => capLog.Read().Contains("Input queue full", StringComparison.Ordinal), 5000));
                log = capLog.Read();
            }
            Assert.True(SentContains(conn, "Server busy; input dropped."), "queue-full must notify dropped");
            Assert.False(SentContains(conn, "retrying"), "queue-full must not promise a retry");
            Assert.Contains("input dropped", log, StringComparison.Ordinal);
            Assert.DoesNotContain("pending retry", log, StringComparison.Ordinal);
        }
        finally
        {
            conn.SetDisconnected(true);
            conn.ClearPendingInput();
            ConnectionManager.GlobalInstance = null;
            mgr.Atp.Stop(wait: false);
        }
    }
}
