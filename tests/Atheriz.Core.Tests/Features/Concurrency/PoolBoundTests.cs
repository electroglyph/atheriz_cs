using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Slow async submissions must stay inside the pool bound.
[Collection("Ported")]
public class PoolBoundTests
{
    [Fact]
    public void GatedAsyncWork_StaysWithinPoolBound()
    {
        // The pool bound (AsyncThreadPool.cs:255-267) must cover the whole async
        // operation, not just the synchronous prefix: gated async bodies that are
        // all outstanding at once must never run N-wide on a small pool.
        const int submitted = 16;
        using var pool = new AsyncThreadPool(maxThreads: 3, queueLimit: 10000, reliefLimit: 0);
        var release = new ManualResetEventSlim(false);
        var started = new CountdownEvent(submitted);
        var finished = new CountdownEvent(submitted);
        int concurrent = 0;
        int peak = 0;
        void Body()
        {
            int now = Interlocked.Increment(ref concurrent);
            int seen = Volatile.Read(ref peak);
            while (now > seen && Interlocked.CompareExchange(ref peak, now, seen) != seen)
                seen = Volatile.Read(ref peak);
            started.Signal();
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
            // Every body entered while the release gate is still closed, so all
            // submitted bodies overlapped: peak already reflects full overlap.
            Assert.True(started.Wait(TimeSpan.FromSeconds(30)), "not all async bodies started; pool dropped work");
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
