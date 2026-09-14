using Atheriz.Core.Persistence;

namespace Atheriz.Core.Tests.Features.Concurrency;

// An async lease holder that re-enters must nest instead of deadlocking on
// its own permit, while a forked flow is still refused fail-fast.
[Collection("Ported")]
public class WriteGateAsyncTests
{
    [Fact]
    public async Task EnterAsync_NestedTake_NestsWithoutBlocking()
    {
        using var outer = await DbWriteGate.EnterAsync();
        var nestedTask = DbWriteGate.EnterAsync();
        Assert.True(nestedTask.Wait(TimeSpan.FromSeconds(5)));
        using var nested = nestedTask.Result;
        Assert.Equal(0, DbWriteGate.Semaphore.CurrentCount);
    }

    [Fact]
    public async Task EnterAsync_ForkedFlowWhileHeld_Refuses()
    {
        using var outer = await DbWriteGate.EnterAsync();
        var ex = await Record.ExceptionAsync(() => Task.Run(() => DbWriteGate.EnterAsync()));
        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public async Task EnterAsync_SequentialLeases_TakeNormalPath()
    {
        // A stale per-flow token from a released lease must not fake-nest a
        // later lease: the permit count must drop while held.
        using (await DbWriteGate.EnterAsync()) { }
        Assert.Equal(1, DbWriteGate.Semaphore.CurrentCount);
        using (await DbWriteGate.EnterAsync())
        {
            Assert.Equal(0, DbWriteGate.Semaphore.CurrentCount);
        }
        Assert.Equal(1, DbWriteGate.Semaphore.CurrentCount);
    }
}
