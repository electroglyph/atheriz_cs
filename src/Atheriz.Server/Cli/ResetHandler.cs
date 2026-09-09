using System.Net.NetworkInformation;

namespace Atheriz.Server.Cli;

public static class ResetHandler
{
    public static async Task HandleResetAsync(string[] a)
    {
        bool force = ArgumentParser.HasFlag(a, "--force", "-f") || ArgumentParser.HasFlag(a, "--yes", null) || ArgumentParser.HasFlag(a, "-y", null);
        var settings = StopHandler.EffectiveSettingsValue;
        var port = ArgumentParser.ParsePort(a) ?? settings.WebserverPort;
        var host = ArgumentParser.ParseHost(a);
        var savePath = settings.SavePath;
        var pidPath = Path.Combine(savePath, "server.pid");
        bool isRunning = false;
        int? pid = null;
        if (File.Exists(pidPath))
        {
            try { pid = int.Parse(File.ReadAllText(pidPath, System.Text.Encoding.UTF8).Trim()); isRunning = pid is not null && Infrastructure.PidFile.IsServerProcess(pid.Value); } catch { }
        }
        if (!force)
        {
            Console.WriteLine("WARNING: This will delete ALL game data. This action cannot be undone.");
            if (isRunning) Console.WriteLine("The server is currently running and will be stopped.");
            Console.Write("Are you sure you want to continue? [y/N] ");
            var resp = Console.ReadLine();
            if (!string.Equals(resp, "y", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("Aborted."); return; }
        }
        // Ports that must be free before the wipe: the CLI override plus the
        // configured ports, so a mismatched --port cannot blind the guard
        // to the running server's real listeners.
        var watchPorts = new List<int> { port };
        if (settings.WebserverPort != port) watchPorts.Add(settings.WebserverPort);
        if (settings.TelnetEnabled && !watchPorts.Contains(settings.TelnetPort)) watchPorts.Add(settings.TelnetPort);
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            var listeners = props.GetActiveTcpListeners();
            foreach (var ep in listeners)
            {
                if (watchPorts.Contains(ep.Port))
                {
                    // Our own server holds these ports; it is stopped below. Anything else aborts.
                    if (isRunning && pid is not null && Infrastructure.PidFile.IsProcessListeningOnPort(pid.Value, ep.Port)) continue;
                    Console.WriteLine($"Port {ep.Port} still listening; abort");
                    return;
                }
            }
        }
        catch { }

        if (isRunning && pid is not null)
        {
            Console.WriteLine("Stopping server...");
            await StopHandler.HandleStopAsync(a);
            Console.Write($"Waiting for server (PID {pid}) to stop...");
            bool stopped = await ProcessHelper.WaitForPidExitAsync(pid.Value);
            Console.WriteLine(" Done.");
            await Task.Delay(500);
            // Liveness re-check before the irreversible wipe: the stop above
            // may have missed (e.g. a --port override that doesn't match the
            // running server). Never delete live data — fail closed.
            if (!stopped || Infrastructure.PidFile.IsServerProcess(pid.Value))
            {
                try
                {
                    var props = IPGlobalProperties.GetIPGlobalProperties();
                    foreach (var ep in props.GetActiveTcpListeners())
                    {
                        if (watchPorts.Contains(ep.Port) && Infrastructure.PidFile.IsProcessListeningOnPort(pid.Value, ep.Port))
                        {
                            Console.WriteLine($"Port {ep.Port} still listening; abort");
                            return;
                        }
                    }
                }
                catch { }
                Console.WriteLine("Warning: Process still exists after kill.");
                return;
            }
        }

        try { Atheriz.Core.Persistence.AtherizDbContext.CloseDatabase(); } catch { }
        try { Atheriz.Core.Persistence.AtherizDbContextFactory.CloseDatabase(); } catch { }

        Console.WriteLine("Deleting game data...");
        // Nothing to wipe (fresh folder): skip the world-membership gate —
        // GuardWipePath demands markers precisely so a live/foreign dir is
        // never deleted, but an absent dir needs no protection.
        if (Directory.Exists(savePath))
        {
            try { Atheriz.Core.Utils.PathGuards.GuardWipePath(savePath, force); } catch (Exception ex) { Console.WriteLine(ex.Message); return; }
            try
            {
                Directory.Delete(savePath, recursive: true);
            }
            catch (Exception ex) { Console.WriteLine($"Failed to delete save: {ex.Message}"); return; }
        }
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(savePath); } catch (Exception ex) { Console.WriteLine(ex.Message); return; }
        Directory.CreateDirectory(savePath);
        Atheriz.Core.Utils.FsUtil.TryChmod0700(savePath);

        try { Atheriz.Core.Persistence.AtherizDbContext.ReopenDatabase(); } catch { }
        try { Atheriz.Core.Persistence.AtherizDbContextFactory.ReopenDatabase(); } catch { }

        Console.WriteLine("Setting up new world...");
        try
        {
            // Port of atheriz.py reset: local initial_setup.do_setup() with no superuser (limbo world only).
            // Explicit no-prompt creds : reset must never interactively
            // ask for a superuser mid-wipe; env creds still apply when set.
            Atheriz.Core.InitialSetup.DoSetup(savePath, prompt: false);
            Console.WriteLine("Success! New world created.");
        }
        catch (Exception ex) { Console.WriteLine($"Setup failed: {ex.Message}"); return; }

        // Port of atheriz.py:1629 reset always daemonizes after setup.
        var resetSpawnArgs = new List<string> { "--port", port.ToString() };
        if (host is not null) { resetSpawnArgs.Add("--host"); resetSpawnArgs.Add(host); }
        // respawn preserves the CLI telnet-port override, else the
        // replacement silently binds the configured default instead.
        var resetTelnetPort = ArgumentParser.ParseTelnetPort(a);
        if (resetTelnetPort is not null) { resetSpawnArgs.Add("--telnet-port"); resetSpawnArgs.Add(resetTelnetPort.ToString()!); }
        await DaemonSpawner.SpawnDaemonAsync(resetSpawnArgs.ToArray(), Directory.GetCurrentDirectory());
    }
}
