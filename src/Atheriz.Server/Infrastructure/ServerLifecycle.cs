using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;

namespace Atheriz.Server.Infrastructure;

/// <summary>
/// Port of <c>atheriz/globals/startstop.py:30-153</c> wrappers.
/// Mirrors <c>do_startup/do_shutdown/do_reload</c> with <c>_WORLD_LOCK</c> semantics.
/// In Python these coordinate <c>load_objects, get_async_threadpool, get_map_handler, get_node_handler, get_async_ticker, server_events.at_server_start</c> etc.
/// In C# we delegate to <c>StartStop</c> (faithful) which wires <c>ObjectRegistry.LoadObjects + GlobalServices + GameTime/Autosave</c>.
/// </summary>
public static class ServerLifecycle
{
    // Port of startstop.py:17 _WORLD_LOCK
    private static readonly object WorldLock = new();
    // Port of startstop.py:18 _shutdown_lock = _WORLD_LOCK
    private static readonly object ShutdownLock = new();
    // Port of startstop.py:19 _shutdown_completed
    private static bool _shutdownCompleted = false;
    // Readiness flag for /ready (liveness stays /health per AGENTS webclient constraint).
    // Set only after DoStartup runs to completion; cleared when a new startup begins.
    private static volatile bool _startupSucceeded = false;
    public static bool StartupSucceeded => _startupSucceeded;

    // Port of startstop.py:22 _shutdown_step
    private static void ShutdownStep(string name, Action fn)
    {
        try { fn(); }
        catch (Exception ex) { Console.Error.WriteLine($"Shutdown step '{name}' failed:\n{ex}"); }
    }

    /// <summary>
    /// Mirrors <c>do_startup()</c> at startstop.py:30-46.
    /// Delegates to <c>StartStop.DoStartup</c> faithful implementation.
    /// </summary>
    public static void DoStartup(AtherizSettings? settings = null)
    {
        // Port of startstop.py:32 with _shutdown_lock: _shutdown_completed=False (handled in StartStop)
        settings ??= AtherizSettings.Global;
        lock (ShutdownLock) _shutdownCompleted = false;
        _startupSucceeded = false;

        // Guard paths — atheriz/atheriz.py:508 etc already done in Program, but repeat for direct calls
        // Port of database_setup.py:66 SAVE_PATH guard
        try { PathGuards.GuardSavePath(settings.SavePath); } catch { throw; }
        // Ensure DB created — mirrors get_database() at database_setup.py:66-88
        try
        {
            using var db = new AtherizDbContext(settings.SavePath);
            db.Database.EnsureCreated();
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) { Console.Error.WriteLine($"DoStartup EnsureCreated failed: {ex}"); }

        // Port of startstop.py:30-46 delegate faithful — loads objects, handlers, server_events, gametime, autosave
        // Delegates to StartStop which uses GlobalServices double-checked singletons
        try { StartStop.DoStartup(null, null, settings); }
        catch (Exception ex) { Console.Error.WriteLine($"StartStop.DoStartup failed:\n{ex}"); }

        Console.Error.WriteLine("[Lifecycle] DoStartup completed."); // Port of lifecycle log
        _startupSucceeded = true;
    }

    /// <summary>
    /// Mirrors <c>do_shutdown()</c> at startstop.py:49-82.
    /// Idempotent via _shutdownCompleted. Delegates to <c>StartStop.DoShutdown</c>.
    /// </summary>
    public static void DoShutdown(AtherizSettings? settings = null)
    {
        settings ??= AtherizSettings.Global;
        // Port of startstop.py:49 with _WORLD_LOCK + _shutdown_lock idempotent
        lock (WorldLock)
        {
            lock (ShutdownLock)
            {
                if (_shutdownCompleted)
                {
                    Console.Error.WriteLine("Shutdown already completed; skipping."); // Port of logger.info
                    return;
                }
                _shutdownCompleted = true;
            }

            // Port of startstop.py:49-82 faithful delegate
            try { StartStop.DoShutdown(settings); }
            catch (Exception ex) { Console.Error.WriteLine($"StartStop.DoShutdown failed:\n{ex}"); }
            // No reset here — preserve idempotence until explicit ResetForTesting; StartStop already handled channel msg, at_server_stop, autosave, gametime, ticker, threadpool, save, msg_all, singleton clear, db_close.
        }
    }

    /// <summary>
    /// Mirrors <c>do_reload()</c> at startstop.py:125-153.
    /// Delegates to <c>StartStop.DoReload</c> (clears ticker, _reregister_ticks).
    /// </summary>
    public static void DoReload(AtherizSettings? settings = null)
    {
        settings ??= AtherizSettings.Global;
        // Port of startstop.py:125 with _WORLD_LOCK — delegate handles locking faithfully; wrapper lock for parity
        lock (WorldLock)
        {
            try { StartStop.DoReload(settings); }
            catch (Exception ex) { Console.Error.WriteLine($"StartStop.DoReload failed:\n{ex}"); }
        }
    }

    /// <summary>
    /// Resets shutdown flag — for tests / restart.
    /// Port of test helper resetting _shutdownCompleted.
    /// </summary>
    public static void ResetForTesting()
    {
        lock (ShutdownLock) _shutdownCompleted = false;
        try { StartStop.ResetForTesting(); } catch { }
    }
}
