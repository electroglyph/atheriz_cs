using System.Collections.Concurrent;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Both Start overloads share one registration core: concurrent mixed-overload
// starts register OnTick exactly once, and a repeat start is a no-op.
[Collection("Ported")]
public class GameTimeStartCoreTests
{
    [Fact]
    public void Start_ConcurrentMixedOverloads_RegistersOnTickOnce()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = true, TimeUpdateSeconds = 3600 };
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        var gameTime = new GameTime(settings, ticker, pool, autoLoad: false);
        const int starters = 8;
        using var barrier = new Barrier(starters + 1);
        var errors = new ConcurrentQueue<Exception>();
        var threads = Enumerable.Range(0, starters).Select(i =>
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    if (i % 2 == 0) gameTime.Start();
                    else gameTime.Start(ticker);
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
            Assert.Single(slot!.Coros);
        }
        finally
        {
            try { gameTime.Stop(ticker); } catch { }
            try { ticker.Stop(); } catch { }
        }
    }

    [Fact]
    public void Start_SecondStartWhileStarted_DoesNotDuplicateRegistration()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath, TimeSystemEnabled = true, TimeUpdateSeconds = 3601 };
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100, reliefLimit: 0);
        var ticker = new AsyncTicker(pool);
        var gameTime = new GameTime(settings, ticker, pool, autoLoad: false);
        try
        {
            gameTime.Start();
            gameTime.Start(ticker);
            gameTime.Start();
            Assert.True(gameTime.Started);
            var slot = ticker.GetSlot(settings.TimeUpdateSeconds);
            Assert.NotNull(slot);
            Assert.Single(slot!.Coros);
        }
        finally
        {
            try { gameTime.Stop(ticker); } catch { }
            try { ticker.Stop(); } catch { }
        }
    }
}
