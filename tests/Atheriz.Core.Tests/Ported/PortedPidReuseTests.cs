// Port of atheriz/tests/test_pid_reuse.py:1
using System.Diagnostics;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedPidReuseTests
{
    [Fact]
    public void RejectsCurrentTestHost()
    {
        // The gate trusts only this engine's server shapes: the test host
        // (dotnet host, test-assembly command line) is refused, so `stop`
        // can never terminate a test run on pid reuse.
        using var env = GlobalTestEnv.Enter();
        Assert.False(PidFile.IsServerProcess(Environment.ProcessId));
    }

    [Fact]
    public void RejectsNonexistentPid()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.False(PidFile.IsServerProcess(int.MaxValue));
    }

    [Fact]
    public void RejectsLiveUnrelatedPid()
    {
        // A live process that is not a server fails the gate: no bare
        // process-name trust remains.
        using var env = GlobalTestEnv.Enter();
        using var sleeper = Process.Start(new ProcessStartInfo
        {
            FileName = "sleep",
            Arguments = "30",
            UseShellExecute = false,
        });
        Assert.NotNull(sleeper);
        try
        {
            Assert.False(PidFile.IsServerProcess(sleeper!.Id));
        }
        finally { try { sleeper.Kill(); } catch { } }
    }
}
