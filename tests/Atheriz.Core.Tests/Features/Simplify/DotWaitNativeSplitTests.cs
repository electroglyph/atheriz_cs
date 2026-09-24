using System.Diagnostics;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Simplify;

// Quiet bounded waits (WaitForExitAsync + pid polling); no dot progress
// output. Native (libc) P/Invoke is banned repo-wide — see
// NoNativeLibcTests.
[Collection("Ported")]
public class DotWaitNativeSplitTests
{
    [Fact]
    public async Task WaitForPidExit_DeadPid_ReportsExited()
    {
        Assert.True(await ProcessHelper.WaitForPidExitAsync(int.MaxValue));
    }

    [Fact]
    public async Task TerminateAsync_ExitedProcess_SettlesPromptly()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--version");
        using var p = Process.Start(psi);
        Assert.NotNull(p);
        Assert.True(p!.WaitForExit(15000));
        await ProcessHelper.TerminateAsync(p).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(p.HasExited);
    }

    [Fact]
    public void Waits_AreQuiet_NoDotProgress()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ProcessHelper.cs");
        Assert.DoesNotContain("Console.Write(\".\")", src);
    }
}
