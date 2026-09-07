using System.Collections.Concurrent;
using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Pending accounting balances exactly under parallel reserve/release.
[Collection("Ported")]
public class LimiterStressTests
{
    [Fact]
    public void ParallelReserveRelease_BalancesToZero()
    {
        // Matched TryReserve/ReleaseSync pairs (PendingLimiter.cs:61-70,108-117)
        // across racing threads must converge to zero pending, never negative.
        var limiter = new PendingLimiter(maxBytes: 1000000);
        const int workers = 8;
        const int iterations = 500;
        using var barrier = new Barrier(workers + 1);
        var errors = new ConcurrentQueue<Exception>();
        var threads = Enumerable.Range(0, workers).Select(_ =>
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    for (int i = 0; i < iterations; i++)
                    {
                        Assert.True(limiter.TryReserve(10));
                        limiter.ReleaseSync(10);
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            })
            { IsBackground = true };
            return thread;
        }).ToList();
        threads.ForEach(t => t.Start());
        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
        foreach (var thread in threads)
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "worker did not finish; possible deadlock");
        Assert.Empty(errors);
        Assert.Equal(0, limiter.PendingBytes);
        Assert.Equal(0, limiter.PendingCount);
        Assert.Equal((0, 0, 0), limiter.Snapshot());
    }

    [Fact]
    public void ParallelTaskReserveRelease_BalancesToZero()
    {
        // Task-tracked reservations (PendingLimiter.cs:76-86,91-102) released by
        // their own task key must also converge to zero pending.
        var limiter = new PendingLimiter(maxBytes: 1000000);
        const int workers = 8;
        const int iterations = 200;
        using var barrier = new Barrier(workers + 1);
        var errors = new ConcurrentQueue<Exception>();
        var threads = Enumerable.Range(0, workers).Select(_ =>
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    for (int i = 0; i < iterations; i++)
                    {
                        var task = new Task(() => { });
                        Assert.True(limiter.TryReserve(task, 8));
                        limiter.Release(task);
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            })
            { IsBackground = true };
            return thread;
        }).ToList();
        threads.ForEach(t => t.Start());
        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
        foreach (var thread in threads)
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "worker did not finish; possible deadlock");
        Assert.Empty(errors);
        Assert.Equal((0, 0, 0), limiter.Snapshot());
    }

    [Fact]
    public void OverRelease_ClampsAtZero()
    {
        // Releasing more than reserved (PendingLimiter.cs:98-99,112-113) clamps
        // instead of going negative, so a duplicate completion cannot corrupt debt.
        var limiter = new PendingLimiter(maxBytes: 1000);
        var task = new Task(() => { });
        Assert.True(limiter.TryReserve(task, 5));
        limiter.Release(task);
        limiter.Release(task);
        limiter.ReleaseSync(10);
        Assert.Equal(0, limiter.PendingBytes);
        Assert.Equal(0, limiter.PendingCount);
    }
}
