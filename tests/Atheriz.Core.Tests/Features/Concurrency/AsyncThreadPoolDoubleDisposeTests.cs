using System.Collections.Concurrent;
using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Dispose guards the disposed flag under the pool lock (set-first-then-work):
// a second Dispose is a no-op and concurrent Disposes agree on one worker.
[Collection("Ported")]
public class AsyncThreadPoolDoubleDisposeTests
{
    [Fact]
    public void Dispose_DoubleDispose_SecondIsNoOp()
    {
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        pool.Dispose();
        Assert.True(pool.IsStopped);
        var ex = Record.Exception(() => pool.Dispose());
        Assert.Null(ex);
        Assert.True(pool.IsStopped);
    }

    [Fact]
    public void Dispose_ConcurrentDispose_ExactlyOnceNoThrow()
    {
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var errors = new ConcurrentQueue<Exception>();
        Parallel.For(0, 8, _ =>
        {
            try { pool.Dispose(); }
            catch (Exception ex) { errors.Enqueue(ex); }
        });
        Assert.Empty(errors);
        Assert.True(pool.IsStopped);
    }
}
