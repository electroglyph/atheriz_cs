using System.Diagnostics;

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
                await ProcessHelper.WaitForPidExitAsync(oldPid);
                Console.WriteLine(" Done.");
            }
            catch { }
        }
        else await Task.Delay(500);

        // Wait for the old server to release the port before spawning the
        // replacement, so the new bind does not race the old listener.
        if (!await WaitForPortFreeAsync(portVal, 100))
            Console.WriteLine($"Warning: port {portVal} still listening after stop; the new server may fail to bind.");

        if (fg) { Console.WriteLine($"Restart took {sw.Elapsed.TotalMilliseconds:F2}ms"); return true; }
        var spawnArgs = new List<string>();
        if (port != null) { spawnArgs.Add("--port"); spawnArgs.Add(port.ToString()!); }
        if (host != null) { spawnArgs.Add("--host"); spawnArgs.Add(host); }
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
