using System.Reflection;
using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Stop(false) must kill the watchdog timer without joining workers.
// The timer Change+Dispose is non-blocking; joins stay behind the wait gate.
[Collection("Ported")]
public class AsyncThreadPoolWatchdogDisposeTests
{
    private static Timer? ReadTimer(AsyncThreadPool pool)
    {
        var field = typeof(AsyncThreadPool).GetField("_watchdogTimer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Timer?)field.GetValue(pool);
    }

    [Fact]
    public void Stop_WaitFalse_DisposesWatchdogTimer()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        Assert.NotNull(ReadTimer(pool));
        pool.Stop(wait: false, timeout: TimeSpan.FromSeconds(2));
        Assert.Null(ReadTimer(pool));
        pool.Dispose();
        Assert.Null(ReadTimer(pool));
    }

    [Fact]
    public void Stop_DoubleStopFalse_SecondEarlyReturnKeepsTimerDead()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        pool.Stop(wait: false, timeout: TimeSpan.FromSeconds(2));
        Assert.Null(ReadTimer(pool));
        var ex = Record.Exception(() => pool.Stop(wait: false, timeout: TimeSpan.FromSeconds(2)));
        Assert.Null(ex);
        Assert.Null(ReadTimer(pool));
        pool.Dispose();
    }

    [Fact]
    public void Stop_WaitFalse_ReturnsPromptlyWithBlockedWorkers()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var gate = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        Assert.True(pool.AddTask(() => { started.Set(); gate.Wait(TimeSpan.FromSeconds(10)); }));
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        pool.Stop(wait: false, timeout: TimeSpan.FromSeconds(2));
        sw.Stop();
        try
        {
            Assert.Null(ReadTimer(pool));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Stop(false) blocked {sw.Elapsed}");
        }
        finally
        {
            gate.Set();
            Assert.True(Ported.PortedHelpers.WaitFor(() => pool.QueueCount == 0, 5000));
            pool.Dispose();
        }
    }
}
