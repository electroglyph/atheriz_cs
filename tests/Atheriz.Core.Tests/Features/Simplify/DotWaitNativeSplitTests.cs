using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Simplify;

// Shared dot-wait cadence (Delay(100) + Write(".")); per-caller exit probes
// keep their own catch semantics. Directory-sync P/Invokes live beside their
// only caller (PidFile.DirSync); kill stays with RequestTerminate.
[Collection("Ported")]
public class DotWaitNativeSplitTests
{
    [Fact]
    public async Task WaitForPidExit_DeadPid_ReportsExited()
    {
        Assert.True(await ProcessHelper.WaitForPidExitAsync(int.MaxValue));
    }

    [Fact]
    public void NativeMethods_SplitByCaller()
    {
        var helper = SourceScan.Read("src", "Atheriz.Server", "Cli", "ProcessHelper.cs");
        var helperNative = SourceScan.Region(helper, "internal static class NativeMethods");
        Assert.Contains("kill(", helperNative);
        Assert.DoesNotContain("fsync", helperNative);
        Assert.DoesNotContain("extern int open(", helperNative);
        var pid = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        var pidNative = SourceScan.Region(pid, "private static class NativeMethods");
        Assert.Contains("fsync", pidNative);
        Assert.Contains("extern int open(", pidNative);
        Assert.DoesNotContain("kill(", pidNative);
    }

    [Fact]
    public void DotWaits_ShareOneCadenceCore()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ProcessHelper.cs");
        Assert.Equal(1, SourceScan.Count(src, "internal static async Task WaitUntilAsync("));
        Assert.Contains("WaitUntilAsync(() =>", src);
    }
}
