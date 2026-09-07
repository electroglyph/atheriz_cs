using System.Collections.Concurrent;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrent starts register background work exactly once.
[Collection("Ported")]
public class PoolInitTests
{
    [Fact]
    public void ParallelGameTimeStart_RegistersSingleTick()
    {
        // Concurrent Start (GameTime.cs:374-394) must register OnTick once: the
        // Started guard plus the start lock forbid double registration.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = true, TimeUpdateSeconds = 3600 };
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        var gameTime = new GameTime(settings, autoLoad: false);
        const int starters = 8;
        using var barrier = new Barrier(starters + 1);
        var errors = new ConcurrentQueue<Exception>();
        var threads = Enumerable.Range(0, starters).Select(_ =>
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    gameTime.Start(ticker);
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            })
            { IsBackground = true };
            return thread;
        }).ToList();
        try
        {
            threads.ForEach(t => t.Start());
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
            foreach (var thread in threads)
                Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "starter did not finish; possible deadlock");
            Assert.Empty(errors);
            Assert.True(gameTime.Started);
            var slot = ticker.GetSlot(settings.TimeUpdateSeconds);
            Assert.NotNull(slot);
            Assert.Equal(1, slot!.Coros.Count);
        }
        finally
        {
            try
            {
                gameTime.Stop(ticker);
            }
            catch
            {
            }
            try
            {
                ticker.Stop();
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void ParallelAutosaveStart_RegistersSingleTick()
    {
        // Concurrent StartAutosave (Autosave.cs:129-146) registers the tick once.
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = false, AutosaveMinutes = 5 };
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        const int starters = 8;
        using var barrier = new Barrier(starters + 1);
        var errors = new ConcurrentQueue<Exception>();
        var threads = Enumerable.Range(0, starters).Select(_ =>
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    Autosave.StartAutosave(ticker, settings);
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            })
            { IsBackground = true };
            return thread;
        }).ToList();
        try
        {
            threads.ForEach(t => t.Start());
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
            foreach (var thread in threads)
                Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "starter did not finish; possible deadlock");
            Assert.Empty(errors);
            Assert.True(Autosave.AutosaveStarted);
            var slot = ticker.GetSlot(5 * 60.0);
            Assert.NotNull(slot);
            Assert.Equal(1, slot!.Coros.Count);
        }
        finally
        {
            try
            {
                Autosave.StopAutosave(ticker);
            }
            catch
            {
            }
            Autosave.ResetForTesting();
            try
            {
                ticker.Stop();
            }
            catch
            {
            }
        }
    }
}
