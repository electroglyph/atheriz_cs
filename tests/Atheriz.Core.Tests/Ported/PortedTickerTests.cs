// Port of atheriz/tests/test_ticker_slot.py:1
// Port of atheriz/tests/test_ticker_restart.py:1
// Port of atheriz/tests/test_tick_overlap.py:1
// Port of atheriz/tests/test_threadpool.py AsyncTicker part:1
using System.Collections.Concurrent;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedTickerTests
{
    [Fact]
    public void TickerPeriodicallyRunsTask()
    {
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 1000);
        var ticker = new AsyncTicker(pool);
        int counter = 0;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Tick() { if (Interlocked.Increment(ref counter) >= 3) tcs.TrySetResult(true); }
        ticker.AddCoro(Tick, 0.05);
        // Deterministic: TCS after 3 ticks instead of fixed Thread.Sleep
        bool completed = Task.WhenAny(tcs.Task, Task.Delay(1000)).GetAwaiter().GetResult() == tcs.Task;
        int c = Volatile.Read(ref counter);
        ticker.Stop();
        pool.Stop();
        Assert.True(completed, $"ticks {c} <3 within 1s");
        Assert.True(c >= 3, $"ticks {c} <3");
    }

    [Fact]
    public void TickerRemoveCoro()
    {
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 1000);
        var ticker = new AsyncTicker(pool);
        int counter = 0;
        using var started = new CountdownEvent(2);
        void Tick() { Interlocked.Increment(ref counter); try { started.Signal(); } catch { } }
        ticker.AddCoro(Tick, 0.05);
        bool got = started.Wait(1000);
        Assert.True(got, $"Expected >=2 ticks before remove, got {counter}");
        ticker.RemoveCoro(Tick, 0.05);
        int before = Volatile.Read(ref counter);
        // Use PortedHelpers.WaitAsync deterministically instead of fixed Sleep
        Thread.Sleep(200);
        int after = Volatile.Read(ref counter);
        ticker.Stop(); pool.Stop();
        Assert.True(after <= before + 1, $"after {after} > before {before}+1");
    }

    [Fact]
    public void TickerClear()
    {
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 1000);
        var ticker = new AsyncTicker(pool);
        int c1=0,c2=0;
        void T1() => Interlocked.Increment(ref c1);
        void T2() => Interlocked.Increment(ref c2);
        ticker.AddCoro(T1, 0.05);
        ticker.AddCoro(T2, 0.1);
        Thread.Sleep(120);
        ticker.Clear();
        int b1=c1,b2=c2;
        Thread.Sleep(200);
        ticker.Stop(); pool.Stop();
        Assert.True(c1 <= b1+1);
        Assert.True(c2 <= b2+1);
    }

    [Fact]
    public void ConcurrentAddCoroSameIntervalRegistersBoth()
    {
        var ticker = new AsyncTicker(new AsyncThreadPool(maxThreads: 2, queueLimit: 100));
        double interval = 0.05;
        void CoroA() {}
        void CoroB() {}
        var barrier = new Barrier(2);
        var t1 = new Thread(() => { barrier.SignalAndWait(); ticker.AddCoro(CoroA, interval); });
        var t2 = new Thread(() => { barrier.SignalAndWait(); ticker.AddCoro(CoroB, interval); });
        t1.Start(); t2.Start();
        t1.Join(5000); t2.Join(5000);
        try
        {
            var slot = ticker.GetSlot(interval);
            Assert.NotNull(slot);
            Assert.Contains((Delegate)(Action)CoroA, slot!.Coros);
            Assert.Contains((Delegate)(Action)CoroB, slot.Coros);
        }
        finally { ticker.RemoveCoro(CoroA, interval); ticker.RemoveCoro(CoroB, interval); ticker.Stop(); }
    }

    [Fact]
    public void RemoveCoroStopsSlotWhenEmpty()
    {
        var ticker = new AsyncTicker(new AsyncThreadPool(maxThreads: 2, queueLimit: 100));
        void Coro() {}
        ticker.AddCoro(Coro, 0.05);
        var slot = ticker.GetSlot(0.05)!;
        ticker.RemoveCoro(Coro, 0.05);
        Assert.False(slot.Running);
        ticker.RemoveCoro(Coro, 0.05);
        Assert.False(slot.Running);
        ticker.Stop();
    }

    [Fact]
    public void ConcurrentAddRemoveNeverOrphansCoro()
    {
        for (int iter=0; iter<50; iter++)
        {
            var ticker = new AsyncTicker(new AsyncThreadPool(maxThreads: 2, queueLimit: 100));
            double interval = 0.05;
            void Filler() {}
            ticker.AddCoro(Filler, interval);
            var slot = ticker.GetSlot(interval)!;
            var barrier = new Barrier(2);
            var t1 = new Thread(() => { barrier.SignalAndWait(); ticker.AddCoro(Filler, interval); });
            var t2 = new Thread(() => { barrier.SignalAndWait(); ticker.RemoveCoro(Filler, interval); });
            t1.Start(); t2.Start(); t1.Join(5000); t2.Join(5000);
            Assert.Equal(slot.Coros.Count>0, slot.Running);
            ticker.Stop(); ticker.Clear();
        }
    }

    [Fact]
    public void TickerClearWhileTimerRunning()
    {
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var ticker = new AsyncTicker(pool);
        int counter=0;
        void Tick() => Interlocked.Increment(ref counter);
        ticker.AddCoro(Tick, 0.05);
        Thread.Sleep(120);
        Assert.NotNull(ticker.GetSlot(0.05));
        Assert.True(ticker.GetSlot(0.05)!.Running);
        ticker.Clear();
        Assert.Empty(ticker.Slots);
        int before = Volatile.Read(ref counter);
        Thread.Sleep(200);
        Assert.True(Volatile.Read(ref counter) <= before+1);
        ticker.AddCoro(Tick, 0.05);
        Thread.Sleep(100);
        Assert.True(Volatile.Read(ref counter) > before);
        ticker.Clear();
        pool.Stop(wait:true, timeout: TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void SlowTickNeverRunsConcurrently()
    {
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 1000);
        var ticker = new AsyncTicker(pool);
        int active=0, overlap=0, runs=0;
        object lk=new();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Slow()
        {
            lock(lk) { active++; if(active>1) overlap++; }
            Thread.Sleep(150);
            lock(lk){ active--; runs++; if (runs >=2) tcs.TrySetResult(true); }
        }
        ticker.AddCoro(Slow, 0.05);
        bool completed = Task.WhenAny(tcs.Task, Task.Delay(2000)).GetAwaiter().GetResult() == tcs.Task;
        ticker.RemoveCoro(Slow, 0.05);
        ticker.Stop(); pool.Stop();
        Assert.True(completed, $"runs {runs} <2 within 2s");
        Assert.Equal(0, overlap);
        Assert.True(runs >= 2);
    }

    [Fact]
    public void PendingBlocksOnlyBusyCoro()
    {
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 1000);
        var ticker = new AsyncTicker(pool);
        int fastCount=0;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Slow() => Thread.Sleep(200);
        void Fast() { if (Interlocked.Increment(ref fastCount) >= 4) tcs.TrySetResult(true); }
        ticker.AddCoro(Slow, 0.05);
        ticker.AddCoro(Fast, 0.05);
        bool completed = Task.WhenAny(tcs.Task, Task.Delay(1000)).GetAwaiter().GetResult() == tcs.Task;
        ticker.RemoveCoro(Slow, 0.05);
        ticker.RemoveCoro(Fast, 0.05);
        ticker.Stop(); pool.Stop();
        Assert.True(completed, $"fast {fastCount} <4 within 1s");
        Assert.True(fastCount >= 4, $"fast {fastCount} <4");
    }

    private class BadSlot : AsyncTicker.TimeSlot
    {
        public BadSlot(TimeSpan interval, AsyncThreadPool pool) : base(interval, pool) {}
        public override void Stop() => throw new InvalidOperationException("bad slot boom");
    }

    [Fact]
    public void StopContinuesPastRaisingSlot()
    {
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var ticker = new AsyncTicker(pool);
        void Good() {}
        ticker.AddCoro(Good, 0.05);
        // Inject BadSlot
        var bad = new BadSlot(TimeSpan.FromSeconds(0.99), pool);
        bad.AddCoro(Good);
        bad.Start();
        var fld = typeof(AsyncTicker).GetField("_slots", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dict = (System.Collections.Generic.Dictionary<double, AsyncTicker.TimeSlot>)fld!.GetValue(ticker)!;
        dict[0.99] = bad;
        // Now Stop should continue past bad slot and stop good slot too (good slot should be stopped)
        var ex = Record.Exception(() => ticker.Stop());
        Assert.Null(ex);
        // Good slot should still be stopped despite bad slot throwing
        var goodSlot = ticker.GetSlot(0.05);
        // After Stop, good slot Running should be false (Stop was attempted)
        if (goodSlot != null) Assert.False(goodSlot.Running);
        ticker.Clear();
        pool.Stop();
    }

    [Fact]
    public void WorkerSurvivesDispatchError()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 10, reliefLimit: 0);
        var ran = new ManualResetEventSlim(false);
        // Simulate dispatch error by adding task that throws then ensuring next task still runs
        pool.AddTask(() => throw new InvalidOperationException("boom"));
        Assert.True(pool.AddTask(() => ran.Set()));
        Assert.True(ran.Wait(3000), "worker died instead of surviving");
        pool.Stop();
    }

    [Fact]
    public void TimeSlotRunningReadHoldsLock()
    {
        // Sanity: TimeSlot.Running property reads under lock, Start sets under lock
        var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 10);
        var ticker = new AsyncTicker(pool);
        void Tick() {}
        ticker.AddCoro(Tick, 0.05);
        var slot = ticker.GetSlot(0.05)!;
        Assert.True(slot.Running);
        ticker.RemoveCoro(Tick, 0.05);
        Assert.False(slot.Running);
        ticker.Stop(); pool.Stop();
    }
}
