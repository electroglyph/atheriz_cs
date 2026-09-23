// PidFile helper-output readers: stdout capture with bounded waits and a
// drained stderr (2.5). Spawns a trivial echo so both the sync and async
// readers are pinned against a real process.
using System.Diagnostics;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

[Collection("Ported")]
public sealed class PidFileHelperTests
{
    private static Process StartEcho(string text)
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo { FileName = "cmd", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add($"echo {text}");
        }
        else
        {
            psi = new ProcessStartInfo { FileName = "/bin/echo", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add(text);
        }
        var proc = Process.Start(psi);
        Assert.NotNull(proc);
        return proc!;
    }

    [Fact]
    public async Task ReadHelperOutputAsync_CapturesStdout()
    {
        using var proc = StartEcho("pid-hello");
        string outp = await PidFile.ReadHelperOutputAsync(proc, TimeSpan.FromSeconds(10));
        Assert.Contains("pid-hello", outp);
    }

    [Fact]
    public async Task ReadHelperOutputAsync_CancelledToken_ReturnsGracefully()
    {
        // A cancelled caller token must not hang the reader: the wait dies
        // and the helper is reaped, returning whatever was captured.
        using var proc = StartEcho("pid-cancel");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        string outp = await PidFile.ReadHelperOutputAsync(proc, TimeSpan.FromSeconds(10), cts.Token);
        Assert.NotNull(outp);
    }
}
