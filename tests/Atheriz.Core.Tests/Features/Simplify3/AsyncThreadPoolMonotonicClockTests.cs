using System.Reflection;
using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Queue-full and after-stop discard throttles use the monotonic clock with
// the same 10s window; ticker cadence fires at the configured interval.
[Collection("Ported")]
public class AsyncThreadPoolMonotonicClockTests
{
    private static double ReadFullLogSeconds(AsyncThreadPool pool)
    {
        var field = typeof(AsyncThreadPool).GetField("_lastFullLogSeconds", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (double)field.GetValue(pool)!;
    }

    private static void WriteFullLogSeconds(AsyncThreadPool pool, double value)
    {
        var field = typeof(AsyncThreadPool).GetField("_lastFullLogSeconds", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(pool, value);
    }

    [Fact]
    public void AddTask_QueueFullThrottle_SuppressesRepeatWithinWindow()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 1, reliefLimit: 0);
        var gate = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        try
        {
            Assert.True(pool.AddTask(() => { started.Set(); gate.Wait(TimeSpan.FromSeconds(10)); }));
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(pool.AddTask(() => { }));
            Assert.True(Ported.PortedHelpers.WaitFor(() => pool.QueueCount == 1, 3000));

            Assert.False(pool.AddTask(() => { }));
            double first = ReadFullLogSeconds(pool);
            Assert.NotEqual(double.NegativeInfinity, first);

            Assert.False(pool.AddTask(() => { }));
            double second = ReadFullLogSeconds(pool);
            Assert.Equal(first, second);

            WriteFullLogSeconds(pool, Atheriz.Core.Utils.TimeProvider.MonotonicSeconds() - 11.0);
            Assert.False(pool.AddTask(() => { }));
            double third = ReadFullLogSeconds(pool);
            Assert.True(third > second);
        }
        finally
        {
            gate.Set();
            pool.Stop(wait: false);
            pool.Dispose();
        }
    }

    [Fact]
    public void AddTask_AfterStop_ThrottleSuppressesRepeatDiscardLogs()
    {
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 10, reliefLimit: 0);
        try
        {
            pool.Stop(wait: false, timeout: TimeSpan.FromSeconds(2));
            Assert.False(pool.AddTask(() => { }));
            double first = ReadFullLogSeconds(pool);
            Assert.NotEqual(double.NegativeInfinity, first);
            Assert.False(pool.AddTask(() => { }));
            Assert.Equal(first, ReadFullLogSeconds(pool));
        }
        finally
        {
            pool.Dispose();
        }
    }

    [Fact]
    public void Ticker_Cadence_FiresAtConfiguredInterval()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        try
        {
            using var fired = new CountdownEvent(3);
            ticker.AddCoro(() => { try { fired.Signal(); } catch { } }, TimeSpan.FromMilliseconds(50));
            Assert.True(fired.Wait(TimeSpan.FromSeconds(5)), "ticker did not fire 3 times at 50ms cadence");
        }
        finally
        {
            try { ticker.Stop(); } catch { }
            try { ticker.Clear(); } catch { }
            pool.Dispose();
        }
    }
}
