using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests;

public class ConcurrencyTests
{
    [Fact]
    public void ThreadPool_AddTask_ReturnsTrue_WhenNotFull()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 10);
        var mre = new ManualResetEventSlim(false);
        Assert.True(pool.AddTask(() => mre.Set()));
        Assert.True(mre.Wait(1000));
        pool.Stop();
    }

    [Fact]
    public void ThreadPool_AddTask_ReturnsFalse_WhenFull()
    {
        using var pool = new AsyncThreadPool(maxThreads: 1, queueLimit: 1, reliefLimit: 0);
        // Block worker
        var block = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        pool.AddTask(() => { started.Set(); block.Wait(2000); });
        Assert.True(started.Wait(1000));
        // Queue capacity 1, try to fill
        // First queued item while worker busy should succeed, second should fail (queue full)
        Assert.True(pool.AddTask(() => { }));
        Assert.False(pool.AddTask(() => { }));
        block.Set();
        pool.Stop();
    }

    [Fact]
    public async Task ThreadPool_Delay_ExecutesAfterDelay()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 10);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.Delay(TimeSpan.FromMilliseconds(50), () => tcs.TrySetResult(true));
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000)) == tcs.Task;
        Assert.True(completed, "Delay task did not execute within 1s");
        Assert.True(tcs.Task.Result);
        pool.Stop();
    }

    [Fact]
    public async Task ThreadPool_Delay_AsyncFunc()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 10);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.Delay(TimeSpan.FromMilliseconds(30), async () => { await Task.Delay(10); tcs.TrySetResult(true); });
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000)) == tcs.Task;
        Assert.True(completed, "Async Delay task did not execute within 1s");
        pool.Stop();
    }

    [Fact]
    public async Task ThreadPool_AsyncTask_Runs()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 10);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.AddTask(async () => { await Task.Delay(20); tcs.TrySetResult(true); });
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000)) == tcs.Task;
        Assert.True(completed);
        pool.Stop();
    }

    [Fact]
    public async Task Ticker_Fires_AtInterval()
    {
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 1000);
        var ticker = new AsyncTicker(pool);
        int ticks = 0;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnTick()
        {
            if (Interlocked.Increment(ref ticks) >= 3) tcs.TrySetResult(true);
        }
        ticker.AddCoro(OnTick, TimeSpan.FromMilliseconds(50));
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000)) == tcs.Task;
        ticker.Stop();
        pool.Stop();
        Assert.True(completed, $"Ticker only fired {ticks} times in 1s, expected >=3");
        Assert.InRange(ticks, 3, 20);
    }

    [Fact]
    public async Task Ticker_RemoveCoro_StopsFiring()
    {
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 1000);
        var ticker = new AsyncTicker(pool);
        int ticks = 0;
        // Deterministic: CountdownEvent(2) ensures at least 2 ticks before removal (like Barrier(2) in Python test_hook_race.py:49)
        using var started = new CountdownEvent(2);
        Action onTick = () => { Interlocked.Increment(ref ticks); try { started.Signal(); } catch { } };
        ticker.AddCoro(onTick, TimeSpan.FromMilliseconds(30));
        bool gotTicks = await Task.Run(() => started.Wait(1000));
        Assert.True(gotTicks, $"Expected >=2 ticks before remove, got {ticks}");
        ticker.RemoveCoro(onTick, TimeSpan.FromMilliseconds(30));
        int afterRemove = Volatile.Read(ref ticks);
        await Task.Delay(200);
        ticker.Stop();
        pool.Stop();
        // Allow at most one in-flight tick after removal
        Assert.InRange(ticks, afterRemove, afterRemove + 1);
    }

    [Fact]
    public async Task Ticker_Pending_Dedup_PreventsOverlap()
    {
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 1000);
        var ticker = new AsyncTicker(pool);
        int concurrent = 0;
        int maxConcurrent = 0;
        int ticks = 0;
        object lk = new();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task SlowTick()
        {
            int c = Interlocked.Increment(ref concurrent);
            lock (lk) maxConcurrent = Math.Max(maxConcurrent, c);
            if (Interlocked.Increment(ref ticks) >= 2) tcs.TrySetResult(true);
            await Task.Delay(80);
            Interlocked.Decrement(ref concurrent);
        }
        ticker.AddCoro(SlowTick, TimeSpan.FromMilliseconds(20));
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000)) == tcs.Task;
        ticker.Stop();
        pool.Stop();
        Assert.True(completed, $"SlowTick only executed {ticks} times in 1s, expected >=2");
        Assert.Equal(1, maxConcurrent);
        Assert.True(ticks >= 2);
    }

    [Fact]
    public void Pool_Stop_DiscardsAfterStop()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 10);
        pool.Stop();
        Assert.False(pool.AddTask(() => { }));
    }
}
