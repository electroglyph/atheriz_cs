using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Tests.Features.Concurrency;

// AddCoro/RemoveCoro overload cores: Action and Func<Task> forms share one
// slot per interval (double- and TimeSpan-keyed forms are identical), and
// removing the last coro stops the slot.
[Collection("Ported")]
public sealed class AsyncTickerCoroRegistrationTests
{
    [Fact]
    public void MixedOverloads_ShareOneSlot()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var ticker = new AsyncTicker(pool);
        try
        {
            Action action = () => { };
            Func<Task> asyncFn = () => Task.CompletedTask;
            ticker.AddCoro(action, 0.05);
            ticker.AddCoro(asyncFn, 0.05);
            Assert.Single(ticker.Slots);
            var slot = ticker.GetSlot(0.05);
            Assert.NotNull(slot);
            Assert.Equal(2, slot!.CoroCount);
            Assert.True(slot.ContainsCoro(action));
            Assert.True(slot.ContainsCoro(asyncFn));
        }
        finally
        {
            ticker.Stop();
            pool.Stop();
        }
    }

    [Fact]
    public void DoubleAndTimeSpanIntervals_MapToSameSlot()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var ticker = new AsyncTicker(pool);
        try
        {
            Action a = () => { };
            Action b = () => { };
            ticker.AddCoro(a, 0.05);
            ticker.AddCoro(b, TimeSpan.FromSeconds(0.05));
            Assert.Single(ticker.Slots);
            Assert.Equal(2, ticker.GetSlot(0.05)!.CoroCount);
        }
        finally
        {
            ticker.Stop();
            pool.Stop();
        }
    }

    [Fact]
    public void RemoveCoro_RemovesOnlyTarget_EmptiedSlotStops()
    {
        using var pool = new AsyncThreadPool(maxThreads: 2, queueLimit: 100);
        var ticker = new AsyncTicker(pool);
        try
        {
            Action keep = () => { };
            Func<Task> drop = () => Task.CompletedTask;
            ticker.AddCoro(keep, TimeSpan.FromMilliseconds(50));
            ticker.AddCoro(drop, TimeSpan.FromMilliseconds(50));
            ticker.RemoveCoro(drop, TimeSpan.FromMilliseconds(50));
            var slot = ticker.GetSlot(TimeSpan.FromMilliseconds(50).TotalSeconds);
            Assert.NotNull(slot);
            Assert.Equal(1, slot!.CoroCount);
            Assert.True(slot.Running);
            ticker.RemoveCoro(keep, TimeSpan.FromMilliseconds(50));
            Assert.Equal(0, slot.CoroCount);
            Assert.False(slot.Running);
        }
        finally
        {
            ticker.Stop();
            pool.Stop();
        }
    }
}
