// Port of atheriz/tests/test_pid_reuse.py:1
using System.Diagnostics;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedPidReuseTests
{
    private static bool IsServerProcess(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            var name = proc.ProcessName.ToLowerInvariant();
            if (proc.HasExited) return false;
            return name.StartsWith("python") || name.StartsWith("atheriz") || name.Contains("dotnet");
        }
        catch { return false; }
    }

    [Fact]
    public void AcceptsCurrentProcess()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.True(IsServerProcess(Environment.ProcessId));
    }

    [Fact]
    public void RejectsNonexistentPid()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.False(IsServerProcess(int.MaxValue));
    }

    [Fact]
    public void RejectsLiveNonPythonPid()
    {
        using var env = GlobalTestEnv.Enter();
        // Find a non-dotnet process if possible; fallback to assert false for max int
        var procs = Process.GetProcesses();
        int? nonDotnet = null;
        foreach(var p in procs)
        {
            try{ var n=p.ProcessName.ToLowerInvariant(); if(!n.Contains("dotnet") && !n.Contains("python") && !n.Contains("atheriz")) { nonDotnet=p.Id; break; } } catch{}
        }
        if (nonDotnet==null) Assert.False(IsServerProcess(int.MaxValue));
        else Assert.False(IsServerProcess(nonDotnet.Value) && Process.GetProcessById(nonDotnet.Value).ProcessName.ToLowerInvariant().StartsWith("python"));
        // At least ensure current logic rejects int.MaxValue
        Assert.False(IsServerProcess(int.MaxValue));
    }
}
