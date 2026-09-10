using Atheriz.Core.Concurrency;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Relief dequeue wait-loop: an idle relief worker exits after its bounded
// wait, and shutdown with a waiting relief thread settles promptly
// (sentinel requeue/expand behavior preserved).
[Collection("Ported")]
public sealed class AsyncThreadPoolReliefDequeueTests
{
    [Fact]
    public void ReliefWorker_ExitsAfterIdleWait()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 2);
        var gate = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        int ran = 0;
        Assert.True(pool.AddTask(() => { started.Set(); gate.Wait(TimeSpan.FromSeconds(10)); }));
        Assert.True(started.Wait(5000), "blocker did not start");
        // Queued behind the blocker, this forces a relief spawn.
        Assert.True(pool.AddTask(() => Interlocked.Increment(ref ran)));
        try
        {
            Assert.True(PortedHelpers.WaitFor(() => Volatile.Read(ref ran) == 1, 5000),
                "relief worker did not run the queued task");
            Assert.True(PortedHelpers.WaitFor(() => pool.ReliefCount == 0, 8000),
                "relief worker did not exit after its idle wait");
        }
        finally { gate.Set(); }
    }

    [Fact]
    public void Stop_WithWaitingRelief_SettlesPromptly()
    {
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 2);
        var gate = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        try
        {
            Assert.True(pool.AddTask(() => { started.Set(); gate.Wait(TimeSpan.FromSeconds(10)); }));
            Assert.True(started.Wait(5000), "blocker did not start");
            Assert.True(pool.AddTask(() => { }));
            Assert.True(PortedHelpers.WaitFor(() => pool.ReliefCount == 1, 5000),
                "relief worker did not spawn");
            gate.Set();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            pool.Stop(wait: true, timeout: TimeSpan.FromSeconds(5));
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"stop took {sw.Elapsed}");
        }
        finally
        {
            gate.Set();
            pool.Dispose();
        }
    }
}
