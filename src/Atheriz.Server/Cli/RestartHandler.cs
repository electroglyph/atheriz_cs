
namespace Atheriz.Server.Cli;

public static class RestartHandler
{
    // Port of atheriz.py:1112-1161 restart: stop, wait for old PID, then start again.
    // Returns true when --foreground was given (caller falls through to foreground start).
    public static async Task<bool> HandleRestartAsync(string[] a)
    {
        var port = ArgumentParser.ParsePort(a);
        var host = ArgumentParser.ParseHost(a);
        var fg = ArgumentParser.HasFlag(a, "--foreground", "-f");
        var sw = Stopwatch.StartNew();
        await StopHandler.HandleStopAsync(a);
        // The stop above ran against cached settings; re-read so the spawn
        // below (and the port waits) see post-stop configuration, not a stale cache.
        StopHandler.InvalidateEffectiveSettings();
        int portVal = port ?? StopHandler.EffectiveSettingsValue.WebserverPort;
        var savePath2 = StopHandler.EffectiveSettingsValue.SavePath;
        var pidPath2 = Path.Combine(savePath2, "server.pid");
        if (File.Exists(pidPath2))
        {
            try
            {
                var oldPid = int.Parse(File.ReadAllText(pidPath2, System.Text.Encoding.UTF8).Trim());
                Console.Write($"Waiting for server (PID {oldPid}) to stop...");
                bool exited = await ProcessHelper.WaitForPidExitAsync(oldPid);
                if (!exited)
                {
                    // 5s grace expired and the old server is stuck: escalate to
                    // SIGKILL, but only after re-verifying the pid still names
                    // our server — it may have been recycled mid-wait.
                    bool ours = false;
                    try { ours = File.Exists(pidPath2) && int.Parse(File.ReadAllText(pidPath2, System.Text.Encoding.UTF8).Trim()) == oldPid && Infrastructure.PidFile.IsServerProcess(oldPid); } catch { }
                    if (ours)
                    {
                        Console.Write(" grace expired; force killing...");
                        try { using var stuck = Process.GetProcessById(oldPid); try { stuck.Kill(entireProcessTree: false); } catch { } } catch { }
                        exited = await ProcessHelper.WaitForPidExitAsync(oldPid);
                    }
                    if (!exited && ours)
                    {
                        Console.WriteLine($" old server (PID {oldPid}) did not stop; aborting restart.");
                        return false;
                    }
                }
                Console.WriteLine(" Done.");
            }
            catch { }
        }
        else await Task.Delay(500);

        // Wait for the old server to release the port before spawning the
        // replacement, so the new bind does not race the old listener. Abort
        // the spawn when the port never frees: the child would fail to bind
        // while the old server keeps running, after the operator was told a
        // restart happened.
        if (!await WaitForPortFreeAsync(portVal, 100))
        {
            Console.WriteLine($"Error: port {portVal} still listening after stop; aborting restart (not spawning).");
            return false;
        }

        if (fg) { Console.WriteLine($"Restart took {sw.Elapsed.TotalMilliseconds:F2}ms"); return true; }
        List<string> spawnArgs = [];
        if (port is not null) { spawnArgs.Add("--port"); spawnArgs.Add(port.ToString()!); }
        if (host is not null) { spawnArgs.Add("--host"); spawnArgs.Add(host); }
        // respawn preserves the CLI telnet-port override, else the
        // replacement silently binds the configured default instead.
        var telnetPort = ArgumentParser.ParseTelnetPort(a);
        if (telnetPort is not null) { spawnArgs.Add("--telnet-port"); spawnArgs.Add(telnetPort.ToString()!); }
        await DaemonSpawner.SpawnDaemonAsync(spawnArgs.ToArray(), Directory.GetCurrentDirectory());
        // Wait for the new server to come up on the port (bounded).
        if (!await WaitForPortUpAsync(portVal, 150)) Console.WriteLine($"Warning: port {portVal} not listening yet; check save/server.log.");
        Console.WriteLine($"Restart took {sw.Elapsed.TotalMilliseconds:F2}ms");
        return false;
    }

    // Bounded poll for a port to become free (old listener released).
    internal static async Task<bool> WaitForPortFreeAsync(int port, int tenths)
    {
        for (int i = 0; i < tenths && Infrastructure.PidFile.IsPortListening(port); i++)
            await Task.Delay(100);
        return !Infrastructure.PidFile.IsPortListening(port);
    }

    // Bounded poll for a port to come up (new server bound).
    internal static async Task<bool> WaitForPortUpAsync(int port, int tenths)
    {
        for (int i = 0; i < tenths && !Infrastructure.PidFile.IsPortListening(port); i++)
            await Task.Delay(100);
        return Infrastructure.PidFile.IsPortListening(port);
    }
}
