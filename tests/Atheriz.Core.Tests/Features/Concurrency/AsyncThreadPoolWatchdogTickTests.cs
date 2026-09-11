using System.Reflection;
using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests.Features.Concurrency;

// WatchdogTick reads queue size and limit under one lock so the saturated
// check observes a consistent pair; saturated sets the marker, idle clears it.
[Collection("Ported")]
public class AsyncThreadPoolWatchdogTickTests
{
    private static void InvokeTick(AsyncThreadPool pool)
    {
        var method = typeof(AsyncThreadPool).GetMethod("WatchdogTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(pool, new object?[] { null });
    }

    private static double? ReadSaturatedSince(AsyncThreadPool pool)
    {
        var field = typeof(AsyncThreadPool).GetField("_saturatedSince", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (double?)field.GetValue(pool);
    }

    [Fact]
    public void WatchdogTick_SaturatedThenIdle_SetsAndClearsMarker()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var gate = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        try
        {
            Assert.True(pool.AddTask(() => { started.Set(); gate.Wait(TimeSpan.FromSeconds(10)); }));
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(pool.AddTask(() => { }));
            Assert.True(Ported.PortedHelpers.WaitFor(() => pool.QueueCount == 1, 3000));

            var ex = Record.Exception(() => InvokeTick(pool));
            Assert.Null(ex);
            Assert.NotNull(ReadSaturatedSince(pool));

            gate.Set();
            Assert.True(Ported.PortedHelpers.WaitFor(() => pool.QueueCount == 0 && pool.Busy == 0, 5000));
            ex = Record.Exception(() => InvokeTick(pool));
            Assert.Null(ex);
            Assert.Null(ReadSaturatedSince(pool));
        }
        finally
        {
            gate.Set();
            pool.Stop(wait: false);
            pool.Dispose();
        }
    }

    [Fact]
    public void WatchdogTick_IdlePool_LeavesMarkerClear()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        try
        {
            var ex = Record.Exception(() => InvokeTick(pool));
            Assert.Null(ex);
            Assert.Null(ReadSaturatedSince(pool));
        }
        finally
        {
            pool.Stop(wait: false);
            pool.Dispose();
        }
    }
}
