using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Simplify;

// Shared dot-wait cadence (Delay(100) + Write(".")); per-caller exit probes
// keep their own catch semantics. Native (libc) P/Invoke is banned
// repo-wide — see NoNativeLibcTests.
[Collection("Ported")]
public class DotWaitNativeSplitTests
{
    [Fact]
    public async Task WaitForPidExit_DeadPid_ReportsExited()
    {
        Assert.True(await ProcessHelper.WaitForPidExitAsync(int.MaxValue));
    }

    [Fact]
    public void DotWaits_ShareOneCadenceCore()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ProcessHelper.cs");
        Assert.Equal(1, SourceScan.Count(src, "internal static async Task WaitUntilAsync("));
        Assert.Contains("WaitUntilAsync(() =>", src);
    }
}
