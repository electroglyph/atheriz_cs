using System.Collections.Concurrent;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Features.Concurrency;

// The singleton getter hands every racing caller the same instance.
[Collection("Ported")]
public class SingletonConcurrencyTests
{
    [Fact]
    public void ParallelGetOrCreate_ReturnsSingleInstance()
    {
        // GetOrCreateSingleton (GlobalServices.cs:35-48) double-checks under the
        // upgradeable lock, so barrier-released callers all share one CmdSet.
        const int callers = 16;
        var results = new CmdSet[callers];
        using var barrier = new Barrier(callers + 1);
        var errors = new ConcurrentQueue<Exception>();
        var threads = Enumerable.Range(0, callers).Select(i =>
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    results[i] = GlobalServices.GetLoggedInCmdSet();
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            })
            { IsBackground = true };
            return thread;
        }).ToList();
        threads.ForEach(t => t.Start());
        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
        foreach (var thread in threads)
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "caller did not finish; possible deadlock");
        Assert.Empty(errors);
        Assert.NotNull(results[0]);
        for (int i = 1; i < callers; i++)
            Assert.Same(results[0]!, results[i]!);
    }
}
