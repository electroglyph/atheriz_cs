using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;
using TimeProvider = Atheriz.Core.Utils.TimeProvider;

namespace Atheriz.Core.Tests.Features.Concurrency;

// AsyncTicker TimeSlot duplicate accessors stay consistent.
[Collection("Ported")]
public class AsyncTickerTests
{
    [Fact]
    public void Ticker_DupAccessors_Agree()
    {
        // Behavior pin: TimeSlot.running/Coros duplicates must stay
        // consistent until the aliases are removed.
        var pool = new AsyncThreadPool(maxThreads: 1);
        try
        {
            var slot = new AsyncTicker.TimeSlot(TimeSpan.FromSeconds(1), pool);
            Assert.Equal(slot.Running, slot.running);
            Assert.Equal(slot.Coros.Count, slot.CorosSnapshot.Count);
        }
        finally { pool.Stop(wait: false); }
    }

    private sealed class TickProbe : GameObject { }

    private static int CountCoros(AsyncTicker ticker) => ticker.Slots.Values.Sum(s => s.Coros.Count);

    [Fact]
    public async Task TickerSlot_StuckAsyncCoro_PendingHoldExpiresAndTicksAgain()
    {
        // A never-completing async coro must not pin its slot forever — the
        // pending hold expires and the slot ticks the coro again.
        using var env = GlobalTestEnv.Enter();
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        try
        {
            var interval = TimeSpan.FromMilliseconds(50);
            int ticks = 0;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<Task> stuck = async () => { Interlocked.Increment(ref ticks); await gate.Task; };
            ticker.AddCoro(stuck, interval);
            var slot = ticker.GetSlot(interval.TotalSeconds);
            Assert.NotNull(slot);
            slot!.PendingHoldTimeout = TimeSpan.FromMilliseconds(200);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (Volatile.Read(ref ticks) < 2 && sw.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(20);
            Assert.True(Volatile.Read(ref ticks) >= 2, "stuck coro was never re-ticked; pending hold never expired");
        }
        finally { ticker.Clear(); pool.Stop(); }
    }
}
