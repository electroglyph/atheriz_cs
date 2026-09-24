using Atheriz.Core.Commands;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// The dispatch lag gate snapshots the LagCheck delegate once: a concurrent
// swap between the null check and the invoke must neither throw nor run a
// gate the dispatcher never selected.
[Collection("Ported")]
public sealed class DispatchLagSnapshotTests
{
    [Fact]
    public void Dispatch_LagCheck_InvokedExactlyOnce()
    {
        CommandRegistry.Reset();
        var puppet = new GameObject { Name = "Hero" };
        int calls = 0;
        CommandDispatcher.LagCheck = _ => { Interlocked.Increment(ref calls); return false; };
        try
        {
            var job = CommandDispatcher.DispatchLoggedIn(puppet, "look", immediate: true);
            Assert.NotNull(job);
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            CommandDispatcher.LagCheck = null;
            CommandRegistry.Reset();
        }
    }

    [Fact]
    public async Task Dispatch_LagCheckConcurrentSwap_NeverThrows()
    {
        CommandRegistry.Reset();
        var puppet = new GameObject { Name = "Hero" };
        Func<IMessageTarget, bool> allow = _ => false;
        Func<IMessageTarget, bool> deny = _ => true;
        CommandDispatcher.LagCheck = allow;
        using var cts = new CancellationTokenSource();
        var swappers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            // Cycle through allow/deny/null: a null landing between the
            // check and the invoke throws under a double read.
            Func<IMessageTarget, bool>?[] cycle = [allow, deny, null];
            int i = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                CommandDispatcher.LagCheck = cycle[i++ % cycle.Length];
            }
        })).ToList();
        try
        {
            for (int i = 0; i < 3000; i++)
            {
                var ex = Record.Exception(() =>
                    CommandDispatcher.DispatchLoggedIn(puppet, "look", immediate: true));
                Assert.Null(ex);
            }
        }
        finally
        {
            cts.Cancel();
            var swapAll = Task.WhenAll(swappers);
            var swapWinner = await Task.WhenAny(swapAll, Task.Delay(TimeSpan.FromSeconds(30)));
            Assert.True(swapWinner == swapAll, "swappers did not stop");
            CommandDispatcher.LagCheck = null;
            CommandRegistry.Reset();
        }
    }
}
