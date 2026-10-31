using System.Net.NetworkInformation;
using Atheriz.Core.Plugins;
using Atheriz.Server.Hosting;

namespace Atheriz.Server.Cli;

public static class ResetHandler
{
    /// <summary>
    /// Post-wipe template-fallback gate: true when falling back to the engine
    /// template world would be wrong — the game assembly failed to load, or it
    /// is no longer discoverable (deleted or rebuilt stale mid-reset). A clean
    /// load that simply provides no <c>IGameSetup</c> entry (every scaffolded
    /// game) returns false: the template world is its intended world.
    /// Fails closed: any discovery error refuses.
    /// </summary>
    public static bool TemplateFallbackRefused(AtherizSettings settings, int failedLoads)
    {
        if (failedLoads > 0) return true;
        var rediscovered = new List<string>();
        try { Atheriz.Core.Plugins.PluginReloader.DiscoverGameAssembly(settings, rediscovered); }
        catch { return true; }
        return rediscovered.Count == 0;
    }

    public static async Task<int> ResetAsync(
        int? portOverride,
        int? telnetOverride,
        bool foreground = true,
        Func<AtherizSettings, CancellationToken, Task<BuildCoordinator.BuildAllResult>>? buildAll = null)
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
        // Only a claim that still names one of our live servers counts as
        // running: a number the OS recycled onto an unrelated process must
        // not be stopped, nor must it block the wipe as "our listener".
        var runningClaim = Infrastructure.PidFile.TryReadClaim(pidPath);
        if (runningClaim is Infrastructure.PidClaim claim && Infrastructure.PidFile.ClaimNamesLiveServer(claim))
        {
            pid = claim.Pid;
            isRunning = true;
        }
        Console.WriteLine("WARNING: This will delete ALL game data. This action cannot be undone.");
        if (isRunning) Console.WriteLine("The server is currently running and will be stopped.");
        Console.Write("Are you sure you want to continue? [y/N] ");
        var resp = Console.ReadLine();
        if (!string.Equals(resp, "y", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("Aborted."); return 1; }
        // Build-first: a reset replaces the world with whatever setup runs
        // next, so it must run on fresh binaries — never refuse for a
        // missing build when the compiler could produce one. Engine before
        // game (same order as the per-game scripts). Any failure aborts
        // here: nothing is deleted and a running server is untouched.
        var build = await (buildAll ?? ((s, ct) => BuildCoordinator.EnsureAllBuiltAsync(s, cancellationToken: ct)))(
            settings, CancellationToken.None).ConfigureAwait(false);
        if (!build.Ok)
        {
            Console.Error.WriteLine($"{build.Summary} Nothing was deleted; the server (if running) was left untouched.");
            return 1;
        }
        // Re-check after building: a game project with no discoverable
        // assembly even now would hand back the engine template world, so
        // refuse before anything is deleted instead.
        if (!PluginReloader.WorldCanBeRebuilt(settings, out var gameProject))
        {
            Console.Error.WriteLine(
                $"No up-to-date build of {gameProject} was found even after building, so reset would recreate the world from the engine template. " +
                $"Fix the build and reset again. Nothing was deleted.");
            return 1;
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
        // it is fresh. On POSIX the recursive delete below takes the lock
        // file with it (open-file unlink works), so it is re-armed right
        // after the re-create. On Windows an open FileShare.None handle
        // blocks the recursive delete with a sharing violation, so the
        // sweep there skips the lock file and the hold is released only at
        // the re-arm below.
        // Guard before the wipe hold: TryAcquireWipeLock creates the save
        // leaf, so acquiring first would plant ./save/.wipe-lock in the
        // engine checkout (which is not a game folder) before the guard
        // below could refuse. The guard message is also the correct refusal.
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(savePath); } catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
        FileStream? wipeA = Infrastructure.PidFile.TryAcquireWipeLock(savePath);
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
        //
        // Same for a markerless save dir that is the conventional save
        // leaf of the current game folder: there is no world worth
        // protecting and no foreign dir at stake, so reset proceeds to a
        // fresh setup instead of taking the operator's confirmation and
        // then refusing. (Mirrors `new --overwrite`'s save-leaf wipe.)
        // Any other markerless target — notably a foreign dir outside a
        // game folder — still goes through the full gate and refuses.
        if (Directory.Exists(savePath))
        {
            try { Atheriz.Core.Utils.PathGuards.DenyRoot(savePath); } catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
            var cwdSave = Path.Combine(Path.GetFullPath(Directory.GetCurrentDirectory()), "save");
            var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            bool ownSaveLeaf = Atheriz.Core.Utils.GameUtils.IsInGameFolder()
                && string.Equals(Path.GetFullPath(savePath), cwdSave, pathComparison);
            if (!ownSaveLeaf || Atheriz.Core.Utils.PathGuards.HasWorldMarkers(savePath))
            {
                try { Atheriz.Core.Utils.PathGuards.GuardWipePath(savePath); } catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
            }
            else
            {
                Console.WriteLine("No initialized world found — creating a fresh one...");
            }
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Windows cannot unlink an open FileShare.None handle, so
                    // sweep everything except the held lock file. The hold
                    // stays acquired through the wipe (no barge gap); the
                    // lock file itself is released and deleted at the re-arm
                    // below. Directories are removed deepest-first so a
                    // recursive delete never touches the open handle.
                    foreach (var f in Directory.GetFiles(savePath, "*", SearchOption.AllDirectories))
                    {
                        if (string.Equals(Path.GetFileName(f), Infrastructure.PidFile.WipeLockFileName, StringComparison.Ordinal))
                            continue;
                        File.Delete(f);
                    }
                    foreach (var d in Directory.GetDirectories(savePath, "*", SearchOption.AllDirectories).OrderByDescending(s => s.Length))
                    {
                        try { Directory.Delete(d, recursive: false); }
                        catch (IOException) { /* left for the wipe-incomplete check below */ }
                        catch (UnauthorizedAccessException) { /* left for the wipe-incomplete check below */ }
                    }
                }
                else
                {
                    Directory.Delete(savePath, recursive: true);
                }
            }
            catch (Exception ex) { Console.WriteLine($"Failed to delete save: {ex.Message}"); return 1; }
        }
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(savePath); } catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
        Directory.CreateDirectory(savePath);
        // Reopen before any setup write, still under the saver exclusion:
        // the closed-flag window ends here, not after setup.
        try { Atheriz.Core.Persistence.AtherizDbContextFactory.ReopenDatabase(); } catch { }
        } // end saver-exclusion: savers resume past the reopen
        // Re-arm the wipe hold (the POSIX delete above took it; on Windows
        // the sweep skipped it) before any setup write; abort loudly if
        // anything barged the micro-gap. The old lock file is deleted first
        // — freshness alone would refuse ourselves.
        try { wipeA?.Dispose(); } catch { }
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
            int failedLoads = 0;
            bool loadCrashed = false;
            try { Atheriz.Core.Plugins.PluginReloader.LoadGameAssembliesAtBoot(settings, out game, out failedLoads); }
            catch (Exception ex) { Console.Error.WriteLine($"[reset] Game load failed ({ex.Message}); using template setup."); loadCrashed = true; }
            // A null setup is the normal shape for template-derived games
            // (the scaffold ships no IGameSetup entry): only refuse the
            // template world when the load actually failed or the assembly
            // is gone. Refusing on a clean load would wipe the world and
            // leave the operator with nothing.
            if (game is null && gameProject is not null && (loadCrashed || TemplateFallbackRefused(settings, failedLoads)))
            {
                Console.Error.WriteLine(
                    $"The world was wiped but {gameProject} could not be loaded; refusing to create the engine template world. " +
                    "Keep the server stopped, fix the build, and reset again.");
                Infrastructure.PidFile.ReleaseWipeLock(wipeB, savePath);
                return 1;
            }
            if (game is null && gameProject is not null)
            {
                Console.Error.WriteLine(
                    $"[reset] {gameProject} provides no world setup entry; creating the engine template world.");
            }

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
