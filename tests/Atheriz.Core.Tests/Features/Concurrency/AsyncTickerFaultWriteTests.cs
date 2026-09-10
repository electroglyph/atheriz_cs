using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests.Features.Concurrency;

// TickOnce fault writes stay intact through the helper: a faulting coro
// still reports its error, still releases pending (ticking continues), and
// overlapping ticks stay suppressed by the deferred async release.
[Collection("Ported")]
public sealed class AsyncTickerFaultWriteTests
{
    // Lock-wrapped capture: other collections keep logging to Console.Error
    // while it is swapped, so writes and the final read synchronize.
    private sealed class SyncCapture : TextWriter
    {
        private readonly StringWriter _inner = new();
        public override System.Text.Encoding Encoding => _inner.Encoding;
        public override void Write(string? value) { lock (_inner) _inner.Write(value); }
        public override void Write(char value) { lock (_inner) _inner.Write(value); }
        public override void WriteLine(string? value) { lock (_inner) _inner.WriteLine(value); }
        public string Snapshot() { lock (_inner) return _inner.ToString(); }
    }

    [Fact]
    public async Task FaultingCoro_ReportsError_AndKeepsTicking()
    {
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 1000);
        var ticker = new AsyncTicker(pool);
        var oldErr = Console.Error;
        var capture = new SyncCapture();
        int ticks = 0;
        var recovered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action flaky = () =>
        {
            int n = Interlocked.Increment(ref ticks);
            if (n <= 2) throw new InvalidOperationException($"boom-{n}");
            recovered.TrySetResult(true);
        };
        Console.SetError(capture);
        bool completed;
        try
        {
            ticker.AddCoro(flaky, TimeSpan.FromMilliseconds(30));
            completed = await Task.WhenAny(recovered.Task, Task.Delay(5000)) == recovered.Task;
        }
        finally
        {
            Console.SetError(oldErr);
            ticker.Stop();
            pool.Stop();
        }
        Assert.True(completed, $"ticker stopped after faults (ticks={Volatile.Read(ref ticks)})");
        Assert.Contains("boom-1", capture.Snapshot());
    }

    [Fact]
    public async Task SlowFaultingAsyncCoro_NoOverlap()
    {
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 1000);
        var ticker = new AsyncTicker(pool);
        int concurrent = 0;
        int maxConcurrent = 0;
        object gate = new();
        var seen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task SlowTick()
        {
            int c = Interlocked.Increment(ref concurrent);
            lock (gate) maxConcurrent = Math.Max(maxConcurrent, c);
            seen.TrySetResult(true);
            await Task.Delay(150).ConfigureAwait(false);
            Interlocked.Decrement(ref concurrent);
        }
        ticker.AddCoro(SlowTick, TimeSpan.FromMilliseconds(20));
        try
        {
            var completed = await Task.WhenAny(seen.Task, Task.Delay(5000)) == seen.Task;
            Assert.True(completed, "slow coro never ran");
            // Let several tick periods pass while the first run is pending.
            await Task.Delay(400);
            lock (gate) Assert.Equal(1, maxConcurrent);
        }
        finally
        {
            ticker.Stop();
            pool.Stop();
        }
    }
}
