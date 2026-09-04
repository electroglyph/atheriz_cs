// Port of atheriz/tests/test_startstop.py:1 part2
using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedStartStopTestsPart2
{
    [Fact] public void DoShutdown_SkipsWhenNoChannel()
    {
        using var env = GlobalTestEnv.Enter();
        StartStop.ResetForTesting();
        var ex = Record.Exception(() => StartStop.DoShutdown());
        Assert.Null(ex);
        StartStop.ResetForTesting();
    }
    [Fact] public void DoReload_RunsAtServerReloadHook()
    {
        using var env = GlobalTestEnv.Enter();
        StartStop.ResetForTesting();
        var ex = Record.Exception(() => StartStop.DoReload());
        Assert.Null(ex);
        StartStop.ResetForTesting();
    }
}
