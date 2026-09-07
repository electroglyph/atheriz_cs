using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Slow async submissions must stay inside the pool bound.
[Collection("Ported")]
public class PoolBoundTests
{
    [Fact]
    public void GatedAsyncWork_StaysWithinPoolBound()
    {
        // The pool bound (AsyncThreadPool.cs slot accounting) must cover the
        // whole async operation, not just the synchronous prefix: with more
        // gated bodies outstanding than fixed workers (maxThreads-1 = 2), at
        // least the workers' worth of slots must read busy, and peak overlap
        // must never exceed MaxThreads.
        const int submitted = 4;
        using var pool = new AsyncThreadPool(maxThreads: 3, queueLimit: 10000, reliefLimit: 0);
        var release = new ManualResetEventSlim(false);
        int startedCount = 0;
        var twoStarted = new ManualResetEventSlim(false);
        var finished = new CountdownEvent(submitted);
        int concurrent = 0;
        int peak = 0;
        void Body()
        {
            int now = Interlocked.Increment(ref concurrent);
            int seen = Volatile.Read(ref peak);
            while (now > seen && Interlocked.CompareExchange(ref peak, now, seen) != seen)
                seen = Volatile.Read(ref peak);
            if (Interlocked.Increment(ref startedCount) >= 2) twoStarted.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            Interlocked.Decrement(ref concurrent);
            finished.Signal();
        }
        try
        {
            for (int i = 0; i < submitted; i++)
            {
                Func<Task> runner = () => Task.Run((Action)Body);
                Assert.True(pool.AddTask(runner, $"gated-{i}"));
            }
            // Two bodies are running (the rest queue behind held slots); both
            // impls reach this. Slots must read busy while bodies are parked.
            Assert.True(twoStarted.Wait(TimeSpan.FromSeconds(30)), "not all async bodies started; pool dropped work");
            Assert.True(pool.Busy >= 2, "async bodies freed their slots; bound covers sync prefix only");
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(30)), "gated bodies did not finish after release");
            Assert.True(Volatile.Read(ref peak) <= pool.MaxThreads,
                $"async work escaped the pool bound: peak {Volatile.Read(ref peak)} on a {pool.MaxThreads}-thread pool");
        }
        finally
        {
            release.Set();
            finished.Wait(TimeSpan.FromSeconds(30));
            pool.Stop(wait: true, timeout: TimeSpan.FromSeconds(10));
        }
    }
}
