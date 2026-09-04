// Port of atheriz/tests/test_shutdown_once.py:1
using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedShutdownOnceTests
{
    [Fact] public void SecondShutdownCall_IsNoop()
    {
        using var env = GlobalTestEnv.Enter();
        StartStop.ResetForTesting();
        var ticker = GlobalServices.GetAsyncTicker();
        var pool = GlobalServices.GetAsyncThreadPool();
        // First shutdown
        StartStop.DoShutdown(ticker: ticker, pool: pool);
        // Second should be noop — no exception and _shutdownCompleted true
        var ex = Record.Exception(() => StartStop.DoShutdown(ticker: ticker, pool: pool));
        Assert.Null(ex);
        Assert.True(StartStop.ShuttingDown); // at least no double run
        StartStop.ResetForTesting();
    }
}
