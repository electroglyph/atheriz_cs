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
            try { pid = int.Parse(File.ReadAllText(pidPath, System.Text.Encoding.UTF8).Trim()); isRunning = pid != null && Infrastructure.PidFile.IsServerProcess(pid.Value); } catch { }
        }
        if (!force)
        {
            Console.WriteLine("WARNING: This will delete ALL game data. This action cannot be undone.");
            if (isRunning) Console.WriteLine("The server is currently running and will be stopped.");
            Console.Write("Are you sure you want to continue? [y/N] ");
            var resp = Console.ReadLine();
            if (!string.Equals(resp, "y", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("Aborted."); return; }
        }
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            var listeners = props.GetActiveTcpListeners();
            var watchPorts = new List<int> { port };
            if (settings.TelnetEnabled) watchPorts.Add(settings.TelnetPort);
            foreach (var ep in listeners)
            {
                if (watchPorts.Contains(ep.Port))
                {
                    // Our own server holds these ports; it is stopped below. Anything else aborts.
                    if (isRunning && pid != null && Infrastructure.PidFile.IsProcessListeningOnPort(pid.Value, ep.Port)) continue;
                    Console.WriteLine($"Port {ep.Port} still listening; abort");
                    return;
                }
            }
        }
        catch { }

        if (isRunning && pid != null)
        {
            Console.WriteLine("Stopping server...");
            await StopHandler.HandleStopAsync(a);
            Console.Write($"Waiting for server (PID {pid}) to stop...");
            await ProcessHelper.WaitForPidExitAsync(pid.Value);
            Console.WriteLine(" Done.");
            await Task.Delay(500);
        }

        try { Atheriz.Core.Persistence.AtherizDbContext.CloseDatabase(); } catch { }
        try { Atheriz.Core.Persistence.AtherizDbContextFactory.CloseDatabase(); } catch { }

        Console.WriteLine("Deleting game data...");
        try { Atheriz.Core.Utils.PathGuards.GuardWipePath(savePath, force); } catch (Exception ex) { Console.WriteLine(ex.Message); return; }
        try
        {
            if (Directory.Exists(savePath)) Directory.Delete(savePath, recursive: true);
        }
        catch (Exception ex) { Console.WriteLine($"Failed to delete save: {ex.Message}"); }
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(savePath); } catch (Exception ex) { Console.WriteLine(ex.Message); return; }
        Directory.CreateDirectory(savePath);
        Atheriz.Core.Utils.FsUtil.TryChmod0700(savePath);

        try { Atheriz.Core.Persistence.AtherizDbContext.ReopenDatabase(); } catch { }
        try { Atheriz.Core.Persistence.AtherizDbContextFactory.ReopenDatabase(); } catch { }

        Console.WriteLine("Setting up new world...");
        try
        {
            // Port of atheriz.py reset: local initial_setup.do_setup() with no superuser (limbo world only).
            Atheriz.Core.InitialSetup.DoSetup(savePath);
            Console.WriteLine("Success! New world created.");
        }
        catch (Exception ex) { Console.WriteLine($"Setup failed: {ex.Message}"); return; }

        // Port of atheriz.py:1629 reset always daemonizes after setup.
        var resetSpawnArgs = new List<string> { "--port", port.ToString() };
        if (host != null) { resetSpawnArgs.Add("--host"); resetSpawnArgs.Add(host); }
        await DaemonSpawner.SpawnDaemonAsync(resetSpawnArgs.ToArray(), Directory.GetCurrentDirectory());
    }
}
