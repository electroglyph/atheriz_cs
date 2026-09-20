// A failed input re-attach on a stopped pool must terminate the retry
// chain instead of rescheduling forever.
using System.Reflection;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Network;

[Collection("Ported")]
public sealed class RetryDrainTests
{
    [Fact]
    public void RetryDrain_StoppedPool_TerminatesInsteadOfSpinning()
    {
        using var env = GlobalTestEnv.Enter();
        var pool = new AsyncThreadPool(maxThreads: 1, queueLimit: 100);
        pool.Stop(wait: false);
        var mgr = PortedHelpers.MakeManager(pool: pool);
        var prev = ConnectionManager.GlobalInstance;
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var field = typeof(BaseConnection).GetField("_outstandingRetryDrains",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            int Gauge() => (int)field!.GetValue(null)!;
            // The gauge is process-wide: other collections may hold
            // transient drains, so probe until one attempt observes its
            // own scheduled link, then require that link to terminate.
            int baseline = Gauge();
            bool scheduled = false;
            for (int i = 0; i < 20 && !scheduled; i++)
            {
                baseline = Gauge();
                var probe = new TestConnection("drain-probe");
                probe.EnqueueInput(new Action(() => { }), new List<object?>(), new Dictionary<string, object?>());
                scheduled = Gauge() >= baseline + 1;
                if (!scheduled) Thread.Sleep(50);
            }
            Assert.True(scheduled, "retry link was never observed as scheduled");
            Assert.True(PortedHelpers.WaitFor(() => Gauge() <= baseline, 5000),
                $"retry link did not terminate (baseline={baseline}, now={Gauge()})");
        }
        finally
        {
            ConnectionManager.GlobalInstance = prev;
        }
    }
}
