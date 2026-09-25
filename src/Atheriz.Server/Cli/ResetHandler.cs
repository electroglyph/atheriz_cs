using System.Net.NetworkInformation;
using Atheriz.Server.Hosting;

namespace Atheriz.Server.Cli;

public static class ResetHandler
{
    public static async Task<int> ResetAsync(int? portOverride, int? telnetOverride, bool foreground = true)
    {
        // Reset always confirms: there is no --force skip. The prompt is the
        // operator guard; the port checks, server stop, and world-marker wipe
        // gate below are the non-interactive containment.
        var settings = StopHandler.EffectiveSettingsValue;
        var port = portOverride ?? settings.WebserverPort;
        var savePath = settings.SavePath;
        var pidPath = Path.Combine(savePath, "server.pid");
        bool isRunning = false;
        int? pid = null;
        if (Infrastructure.PidFile.TryReadPid(pidPath) is int readPid)
        {
            pid = readPid;
            isRunning = Infrastructure.PidFile.IsServerProcess(readPid);
        }
        Console.WriteLine("WARNING: This will delete ALL game data. This action cannot be undone.");
        if (isRunning) Console.WriteLine("The server is currently running and will be stopped.");
        Console.Write("Are you sure you want to continue? [y/N] ");
        var resp = Console.ReadLine();
        if (!string.Equals(resp, "y", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("Aborted."); return 1; }
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
                    if (isRunning && pid is not null && Infrastructure.PidFile.IsServerProcess(pid.Value)) continue;
                    Console.WriteLine($"Port {ep.Port} still listening; abort");
                    return 1;
                }
            }
        }
        catch { }

        if (isRunning && pid is not null)
        {
            Console.WriteLine("Stopping server...");
            _ = await StopHandler.StopAsync(port).ConfigureAwait(false);
            bool stopped = await ProcessHelper.WaitForPidExitAsync(pid.Value).ConfigureAwait(false);
            await Task.Delay(500).ConfigureAwait(false);
            // Liveness re-check before the irreversible wipe: the stop above
            // may have missed (e.g. a --port override that doesn't match the
            // running server). Never delete live data — fail closed.
            if (!stopped || Infrastructure.PidFile.IsServerProcess(pid.Value))
            {
                Console.WriteLine("Warning: Process still exists after kill.");
                return 1;
            }
        }

        // Wipe hold: a starter claiming pid/DB between the re-check
        // above and the delete below would get its live database unlinked.
        // Hold the wipe lock from here through setup; starters refuse while
        // it is fresh. The recursive delete below takes the lock file with
        // it, so it is re-armed right after the re-create.
        var wipeA = Infrastructure.PidFile.TryAcquireWipeLock(savePath);
        if (wipeA is null) { Console.WriteLine("Another wipe is in progress; aborting reset."); return 1; }
        var absSave = Path.GetFullPath(savePath);

        // Saver exclusion across the close/wipe/reopen sandwich: the
        // close below flips the process-global closed flag, so a concurrent
        // checkpoint saver would throw "database is closed" mid-sandwich.
        // Holding the write gate parks savers instead of failing them; a
        // 30 s bound fails closed rather than hanging the CLI.
        using var wipeCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Atheriz.Core.Persistence.DbWriteGate.WriteHold? wipeGate = null;
        try { wipeGate = await Atheriz.Core.Persistence.DbWriteGate.EnterAsync(wipeCts.Token).ConfigureAwait(false); }
        catch (Exception ex) { Console.WriteLine($"Could not park savers for wipe: {ex.Message}"); return 1; }
        using (wipeGate)
        {
        try { Atheriz.Core.Persistence.AtherizDbContextFactory.CloseDatabase(); } catch { }

        Console.WriteLine($"Deleting game data at {absSave}...");
        // Nothing to wipe (fresh folder): skip the world-membership gate —
        // GuardWipePath demands markers precisely so a live/foreign dir is
        // never deleted, but an absent dir needs no protection.
        if (Directory.Exists(savePath))
        {
            try { Atheriz.Core.Utils.PathGuards.GuardWipePath(savePath); } catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
            try
            {
                Directory.Delete(savePath, recursive: true);
            }
            catch (Exception ex) { Console.WriteLine($"Failed to delete save: {ex.Message}"); return 1; }
        }
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(savePath); } catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
        Directory.CreateDirectory(savePath);
        // Reopen before any setup write, still under the saver exclusion:
        // the closed-flag window ends here, not after setup.
        try { Atheriz.Core.Persistence.AtherizDbContextFactory.ReopenDatabase(); } catch { }
        } // end saver-exclusion: savers resume past the reopen
        // Re-arm the wipe hold (the delete above took it) before any setup
        // write; abort loudly if anything barged the micro-gap. The old
        // lock file is deleted first — freshness alone would refuse
        // ourselves.
        try { wipeA.Dispose(); } catch { }
        try { File.Delete(Infrastructure.PidFile.WipeLockPath(savePath)); } catch { }
        FileStream? wipeB;
        wipeB = Infrastructure.PidFile.TryAcquireWipeLock(savePath);
        if (wipeB is null) { Console.WriteLine("Another wipe is in progress; aborting reset."); return 1; }
        using (wipeB)
        {
            Atheriz.Core.Utils.FsUtil.TryChmod0700(savePath);
            // Fail closed: never run setup over a half-wiped world — a failed
            // delete above (or a concurrent writer) must abort loudly instead of
            // layering a fresh world on top of surviving data.
            try
            {
                if (Directory.EnumerateFileSystemEntries(savePath).Any(f => Path.GetFileName(f) != Infrastructure.PidFile.WipeLockFileName))
                {
                    Console.WriteLine($"Wipe incomplete, entries remain under {absSave}; aborting before setup.");
                    return 1;
                }
            }
            catch (Exception ex) { Console.WriteLine($"Could not verify wipe of {absSave}: {ex.Message}"); return 1; }

            // (Database was reopened right after the re-create above, still
            // under the saver exclusion — setup below runs unparked.)

            // Load the game (if any) so setup below dispatches to it; best-effort
            // and never fatal — without a game the template setup still runs.
            Atheriz.Core.IGameSetup? game = null;
            try { Atheriz.Core.Plugins.PluginReloader.LoadGameAssembliesAtBoot(settings, out game); }
            catch (Exception ex) { Console.Error.WriteLine($"[reset] Game load failed ({ex.Message}); using template setup."); }

            Console.WriteLine("Setting up new world...");
            try
            {
                // Explicit no-prompt creds: reset must never interactively
                // ask for a superuser mid-wipe; env creds still apply when set.
                Atheriz.Core.InitialSetup.RunSetup(new Atheriz.Core.SetupOptions(savePath, Prompt: false), game);
                Console.WriteLine("Success! New world created.");
            }
            catch (Exception ex) { Console.WriteLine($"Setup failed: {ex.Message}"); return 1; }
            // Clean release: the replacement start below must not be
            // refused by our own just-finished wipe.
            Infrastructure.PidFile.ReleaseWipeLock(wipeB, savePath);
        }

        Console.WriteLine("Starting server...");
        if (!foreground) return await DaemonSpawner.SpawnStart(Directory.GetCurrentDirectory(), portOverride, null, telnetOverride).ConfigureAwait(false);
        return await ServerHost.RunForegroundAsync(port, null, telnetOverride).ConfigureAwait(false);
    }
}
