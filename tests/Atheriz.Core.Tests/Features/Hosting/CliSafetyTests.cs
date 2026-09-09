using System.Diagnostics;
using Atheriz.Server.Cli;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// CLI surface safety: non-zero exits for failures, a pre-dispatch gate for
// bare --host, and a command-line gate behind the python/atheriz name gate so
// a stale pid reused by an unrelated process can never pass the stop gates.
[Collection("Ported")]
public class CliSafetyTests
{
    [Fact]
    public void HasBareHost_FlagsOnlyValuelessHost()
    {
        Assert.True(ArgumentParser.HasBareHost(new[] { "start", "--host" }));
        Assert.True(ArgumentParser.HasBareHost(new[] { "--host=" }));
        Assert.False(ArgumentParser.HasBareHost(new[] { "start", "--host", "0.0.0.0" }));
        Assert.False(ArgumentParser.HasBareHost(new[] { "start", "--host=0.0.0.0" }));
        Assert.False(ArgumentParser.HasBareHost(new[] { "start" }));
        Assert.False(ArgumentParser.HasBareHost(new[] { "create", "mygame" }));
    }

    [Fact]
    public void IsServerProcess_RefusesForeignProcesses()
    {
        // The identity gate trusts only this engine's shapes. The test host
        // itself (dotnet host, test-assembly cmdline) and a dead pid fail it.
        Assert.False(PidFile.IsServerProcess(Environment.ProcessId));
        Assert.False(PidFile.IsServerProcess(int.MaxValue));
    }

    [Fact]
    public void IsServerProcess_NoNamePrefixTrust()
    {
        // Structural pin: no bare process-name prefix trust remains — a stale
        // pid reused by an unrelated process must never pass the stop gates.
        var src = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Infrastructure/PidFile.cs");
        Assert.DoesNotContain("lower.StartsWith(\"python\")", src);
        Assert.DoesNotContain("lower.StartsWith(\"atheriz\")", src);
    }

    [Fact]
    public void Start_InvalidHost_ExitsNonZeroWithoutPidFile()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dll = "/home/anon/atheriz-cs/src/Atheriz.Server/bin/Debug/net10.0/Atheriz.Server.dll";
        if (!File.Exists(dll)) return;
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        // Game-folder markers so the relative save/secret guards pass and the
        // child actually reaches the host validation under test.
        File.WriteAllText(Path.Combine(dir, "mygame.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(dir, "GameSettings.cs"), "// marker");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = dir,
            };
            psi.ArgumentList.Add(dll);
            psi.ArgumentList.Add("start");
            psi.ArgumentList.Add("--host");
            psi.ArgumentList.Add("bad host!");
            psi.Environment["DOTNET_NOLOGO"] = "1";
            using var proc = new Process { StartInfo = psi };
            proc.Start();
            var exited = proc.WaitForExit(60000);
            var output = proc.StandardOutput.ReadToEnd() + "\n" + proc.StandardError.ReadToEnd();
            if (!exited) { try { proc.Kill(entireProcessTree: true); } catch { } }
            Assert.True(exited, "start with invalid host hung");
            Assert.Contains("Invalid --host value", output);
            Assert.NotEqual(0, proc.ExitCode);
            Assert.False(File.Exists(Path.Combine(dir, "save", "server.pid")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
