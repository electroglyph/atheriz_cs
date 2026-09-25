using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Persistence;

namespace Atheriz.Server.Infrastructure;

/// <summary>
/// Mirrors <c>do_startup/do_shutdown/do_reload</c> with <c>_WORLD_LOCK</c> semantics.
/// In Python these coordinate <c>load_objects, get_async_threadpool, get_map_handler, get_node_handler, get_async_ticker, server_events.at_server_start</c> etc.
/// In C# we delegate to <c>StartStop</c> (faithful) which wires <c>ObjectRegistry.LoadObjects + GlobalServices + GameTime/Autosave</c>.
/// </summary>
public static class ServerLifecycle
{
    // Single _WORLD_LOCK (startstop.py:17-18): admin shutdown/reload and
    // in-game reload mutually exclude on StartStop.WorldLock directly
    // (Monitor is re-entrant; nesting is safe). the alias is gone —
    // one name, one lock.
    private static bool _shutdownCompleted = false;
    // Startup generation: DoStartup runs long and unlocked, so a
    // shutdown completing mid-flight (or a second startup) must invalidate
    // the trailing `_startupSucceeded = true`. The commit at the end only
    // lands for the latest generation with no shutdown since.
    private static long _startupGen;
    // Readiness flag for /ready (liveness stays /health per AGENTS webclient constraint).
    // Set only after DoStartup runs to completion; cleared when a new startup begins.
    private static volatile bool _startupSucceeded = false;
    public static bool StartupSucceeded => _startupSucceeded;

    /// <summary>
    /// Mirrors <c>do_startup()</c> at startstop.py:30-46.
    /// Delegates to <c>StartStop.DoStartup</c> faithful implementation.
    /// </summary>
    public static void DoStartup(AtherizSettings? settings = null)
    {
        settings ??= AtherizSettings.Global;
        long gen;
        lock (StartStop.WorldLock) { _shutdownCompleted = false; gen = ++_startupGen; }
        _startupSucceeded = false;

        // Guard paths — atheriz/atheriz.py:508 etc already done in Program, but repeat for direct calls
        Atheriz.Core.Utils.PathGuards.GuardSavePath(settings.SavePath);
        // Ensure DB created — mirrors get_database() at database_setup.py:66-88
        try
        {
            using var db = new AtherizDbContext(settings.SavePath);
            db.Database.EnsureCreated();
        }
        catch (Exception ex) { AtherizLogger.LogError($"DoStartup EnsureCreated failed: {ex}"); _startupSucceeded = false; throw; }

// loads objects, handlers, server_events, gametime, autosave
        // Delegates to StartStop which uses GlobalServices double-checked singletons.
        // Readiness reports success ONLY when startup actually succeeded.
        try { StartStop.DoStartup(null, null, settings); }
        catch (Exception ex)
        {
            AtherizLogger.LogError($"StartStop.DoStartup failed:\n{ex}");
            _startupSucceeded = false;
            throw;
        }

        AtherizLogger.LogInformation("[Lifecycle] DoStartup completed.");
        // Generation commit: a shutdown that completed mid-flight, or
        // a newer startup, leaves success false — /ready must not report ok
        // on a shut-down world.
        lock (StartStop.WorldLock)
        {
            _startupSucceeded = gen == Volatile.Read(ref _startupGen) && !_shutdownCompleted;
        }
    }

    /// <summary>
    /// Mirrors <c>do_shutdown()</c> at startstop.py:49-82.
    /// Idempotent via _shutdownCompleted. Delegates to <c>StartStop.DoShutdown</c>.
    /// </summary>
    public static void DoShutdown(AtherizSettings? settings = null)
    {
        settings ??= AtherizSettings.Global;
        // (single hold; Monitor re-entrancy makes the old nested lock redundant).
        // Shutdown in progress => not ready: /ready must stop reporting ok.
        lock (StartStop.WorldLock)
        {
            if (_shutdownCompleted)
            {
                AtherizLogger.LogInformation("Shutdown already completed; skipping.");
                return;
            }
            _shutdownCompleted = true;
            _startupSucceeded = false;

            try { StartStop.DoShutdown(settings); }
            catch (Exception ex) { AtherizLogger.LogError($"StartStop.DoShutdown failed:\n{ex}"); }
            // No reset here — preserve idempotence until explicit Reset; StartStop already handled channel msg, at_server_stop, autosave, gametime, ticker, threadpool, save, msg_all, singleton clear, db_close.
        }
    }

    /// <summary>
    /// Mirrors <c>do_reload()</c> at startstop.py:125-153.
    /// Delegates to <c>StartStop.DoReload</c> (clears ticker, _reregister_ticks).
    /// </summary>
    public static void DoReload(AtherizSettings? settings = null)
    {
        settings ??= AtherizSettings.Global;
// delegate handles locking faithfully; wrapper lock for parity
        lock (StartStop.WorldLock)
        {
            try { StartStop.DoReload(settings); }
            catch (Exception ex) { AtherizLogger.LogError($"StartStop.DoReload failed:\n{ex}"); }
        }
    }

    /// <summary>
    /// Resets shutdown flag — for tests / restart.
    /// </summary>
    public static void Reset()
    {
        lock (StartStop.WorldLock) { _shutdownCompleted = false; _startupGen++; }
        _startupSucceeded = false;
        try { StartStop.Reset(); } catch { }
    }
}
