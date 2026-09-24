using Atheriz.Server.Hosting;

namespace Atheriz.Server.Cli;

public static class RestartHandler
{
    // Stop the old server, then run the replacement in this process.
    // Returns the process exit code.
    public static async Task<int> RestartAsync(int? port, string? host, int? telnetPort, bool foreground = true)
    {
        var sw = Stopwatch.StartNew();
        _ = await StopHandler.StopAsync(port).ConfigureAwait(false);
        // The stop above ran against cached settings; re-read so the waits
        // below see post-stop configuration, not a stale cache.
        StopHandler.InvalidateEffectiveSettings();
        int portVal = port ?? StopHandler.EffectiveSettingsValue.WebserverPort;
        var savePath2 = StopHandler.EffectiveSettingsValue.SavePath;
        var pidPath2 = Path.Combine(savePath2, "server.pid");
        if (Infrastructure.PidFile.TryReadPid(pidPath2) is int oldPid)
        {
            bool exited = await ProcessHelper.WaitForPidExitAsync(oldPid).ConfigureAwait(false);
            if (!exited)
            {
                // Grace expired and the old server is stuck: escalate, but
                // only after re-verifying the pid still names our server —
                // it may have been recycled mid-wait.
                bool ours = false;
                try { ours = Infrastructure.PidFile.TryReadPid(pidPath2) == oldPid && Infrastructure.PidFile.IsServerProcess(oldPid); } catch { }
                if (ours)
                {
                    try { using var stuck = Process.GetProcessById(oldPid); try { stuck.Kill(entireProcessTree: false); } catch { } } catch { }
                    exited = await ProcessHelper.WaitForPidExitAsync(oldPid).ConfigureAwait(false);
                }
                if (!exited && ours)
                {
                    Console.WriteLine($"Old server (PID {oldPid}) did not stop; aborting restart.");
                    return 1;
                }
            }
        }
        else await Task.Delay(500).ConfigureAwait(false);

        // Wait for the old server to release the port before binding the
        // replacement, so the new bind does not race the old listener.
        if (!await WaitForPortFreeAsync(portVal, TimeSpan.FromSeconds(10)).ConfigureAwait(false))
        {
            Console.WriteLine($"Error: port {portVal} still listening after stop; aborting restart.");
            return 1;
        }

        Console.WriteLine($"Restart took {sw.Elapsed.TotalMilliseconds:F2}ms");
        if (!foreground) return await DaemonSpawner.SpawnStart(Directory.GetCurrentDirectory(), port, host, telnetPort).ConfigureAwait(false);
        return await ServerHost.RunForegroundAsync(port, host, telnetPort).ConfigureAwait(false);
    }

    // Bounded quiet poll for a port to reach a state: same socket-probe
    // count, same 100 ms cadence — only the desired state differs.
    internal static async Task<bool> WaitForPortStateAsync(int port, TimeSpan timeout, bool wantUp)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout && Infrastructure.PidFile.IsPortListening(port) != wantUp)
            await Task.Delay(100).ConfigureAwait(false);
        return Infrastructure.PidFile.IsPortListening(port) == wantUp;
    }

    // Bounded poll for a port to become free (old listener released).
    internal static Task<bool> WaitForPortFreeAsync(int port, TimeSpan timeout)
        => WaitForPortStateAsync(port, timeout, wantUp: false);
}
