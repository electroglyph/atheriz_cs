// Port of atheriz/globals/startstop.py:153 — faithful DoStartup/DoShutdown/DoReload with _WORLD_LOCK.
// Mirrors _WORLD_LOCK, _shutdown_completed, _shutdown_step, server_events hooks, autosave, gametime, ticker, threadpool.

using System.Diagnostics;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Globals;

// Port of atheriz/globals/startstop.py:17 _WORLD_LOCK
public static class StartStop
{
    // Port of startstop.py:17 _WORLD_LOCK = RLock(); startstop.py:18 aliases
    // _shutdown_lock to the same lock — C# keeps the single name .
    private static readonly object _worldLock = new();
    // Port of startstop.py:19 _shutdown_completed = False
    private static bool _shutdownCompleted = false;
    // Spec extra: bool _started,_shuttingDown (aliases to _shutdownCompleted)
    private static bool _started = false;
    private static bool _shuttingDown = false;

    // Expose locks for parity with spec
    public static object WorldLock => _worldLock;
    public static bool Started { get { lock (_worldLock) return _started; } }
    public static bool ShuttingDown { get { lock (_worldLock) return _shuttingDown; } }

    // Port of startstop.py:22 _shutdown_step(name,fn)
    private static void ShutdownStep(string name, Action fn)
    {
        try { fn(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Shutdown step '{name}' failed:\n{ex}");
        }
    }

    // Port of startstop.py:30-46 do_startup
    public static void DoStartup(AsyncThreadPool? pool = null, AsyncTicker? ticker = null, AtherizSettings? settings = null)
    {
        settings ??= AtherizSettings.Global;
        // Port of startstop.py:32 with _shutdown_lock: _shutdown_completed=False.
        // Unlike Python (flag-only hold), the whole load phase holds _worldLock
        // — outermost in the global order, same as DoShutdown/DoReload — so a
        // concurrent shutdown/reload cannot clear singletons mid-boot .
        lock (_worldLock)
        {
            _shutdownCompleted = false;
            _shuttingDown = false;
            _started = true;
            // Port of startstop.py:34 load_objects()
            try
            {
                // Port of objects.load_objects via ObjectRegistry.LoadObjects
                // Use savePath overload which handles DB EnsureCreated
                ObjectRegistry.LoadObjects(settings.SavePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"DoStartup LoadObjects failed:\n{ex}");
            }

            // Crash-consistency check: a dirty journal means the
            // previous checkpoint died between tables — the world may be torn.
            // Boot continues (availability), but the torn state is surfaced loudly.
            try
            {
                if (Persistence.CheckpointJournal.IsDirty(settings.SavePath))
                {
                    var msg = "Torn checkpoint detected: previous save did not complete; world tables may be inconsistent.";
                    try { AtherizLogger.LogError(msg); } catch { Console.Error.WriteLine(msg); }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"DoStartup checkpoint check failed:\n{ex}");
            }

            // Port of startstop.py:35-38 get_async_threadpool/get_map_handler/get_node_handler/get_async_ticker
            try
            {
                pool ??= GlobalServices.GetAsyncThreadPool();
            }
            catch (Exception ex) { Console.Error.WriteLine($"DoStartup GetAsyncThreadPool failed:\n{ex}"); }

            MapHandler? mapHandler = null;
            try
            {
                mapHandler = GlobalServices.GetMapHandler(settings);
            }
            catch (Exception ex) { Console.Error.WriteLine($"DoStartup GetMapHandler failed:\n{ex}"); }

            NodeHandler? nodeHandler = null;
            try
            {
                nodeHandler = GlobalServices.GetNodeHandler(settings);
            }
            catch (Exception ex) { Console.Error.WriteLine($"DoStartup GetNodeHandler failed:\n{ex}"); }

            try
            {
                ticker ??= GlobalServices.GetAsyncTicker();
            }
            catch (Exception ex) { Console.Error.WriteLine($"DoStartup GetAsyncTicker failed:\n{ex}"); }

            // Port of startstop.py:39-42 server_events.at_server_start()
            try
            {
                // Try game-folder server_events first, fallback to Core stub
                TryInvokeServerEvent("AtServerStart");
            }
            catch (Exception ex) { Console.Error.WriteLine($"at_server_start failed:\n{ex}"); }

            // Port of startstop.py:44-45 if TIME_SYSTEM_ENABLED: get_game_time().start()
            if (settings.TimeSystemEnabled)
            {
                try
                {
                    var gt = GlobalServices.GetGameTime(settings);
                    // Port of get_game_time().start() — ticker is singleton; GameTime.Start expects ticker
                    if (ticker is not null)
                        gt.Start(ticker);
                    else
                        gt.Start();
                }
                catch (Exception ex) { Console.Error.WriteLine($"GameTime start failed:\n{ex}"); }
            }

            // Port of startstop.py:46 start_autosave()
            try
            {
                if (settings.AutosaveMinutes != 0)
                {
                    var t = ticker ?? GlobalServices.GetAsyncTicker();
                    var gt = settings.TimeSystemEnabled ? GlobalServices.GetGameTime(settings) : null;
                    // Port of autosave.start_autosave — use ticker overload with handlers
                    Autosave.StartAutosave(t, settings, mapHandler, nodeHandler, gt);
                }
            }
            catch (Exception ex) { Console.Error.WriteLine($"start_autosave failed:\n{ex}"); }
        }
    }

    // Port of startstop.py:49-82 do_shutdown
    public static void DoShutdown(AtherizSettings? settings = null, AsyncThreadPool? pool = null, AsyncTicker? ticker = null)
    {
        settings ??= AtherizSettings.Global;
        lock (_worldLock)
        {
            if (_shutdownCompleted)
            {
                Console.Error.WriteLine("Shutdown already completed; skipping."); // Port of logger.info
                return;
            }
            _shutdownCompleted = true;
            _shuttingDown = true;

            // Port of startstop.py:57 channel = get_server_channel(); if channel: channel.msg("Server is shutting down!")
            try
            {
                var channel = GlobalServices.GetServerChannel();
                if (channel is not null)
                {
                    try { channel.Msg("Server is shutting down!"); } catch (Exception) { }
                }
            }
            catch (Exception) { }

            Console.Error.WriteLine("Starting shutdown sequence..."); // Port of logger.info

            // Port of startstop.py:62-65 at_server_stop
            ShutdownStep("at_server_stop", () => TryInvokeServerEvent("AtServerStop"));

            // Port of startstop.py:66 stop_autosave
            ShutdownStep("stop_autosave", () =>
            {
                try
                {
                    var t = ticker ?? TryGetTicker();
                    if (t is not null) Autosave.StopAutosave(t);
                    else Autosave.Reset(); // fallback placeholder
                }
                catch (Exception) { }
            });

            // Port of startstop.py:67-68 if TIME_SYSTEM_ENABLED: get_game_time().stop
            if (settings.TimeSystemEnabled)
            {
                ShutdownStep("game_time_stop", () =>
                {
                    try
                    {
                        var gt = TryGetGameTime();
                        var t = ticker ?? TryGetTicker();
                        if (gt is not null && t is not null) gt.Stop(t);
                        else if (gt is not null) gt.Stop();
                    }
                    catch (Exception) { }
                });
            }

            // Port of startstop.py:69 ticker_stop
            ShutdownStep("ticker_stop", () =>
            {
                var t = ticker ?? TryGetTicker();
                t?.Stop();
            });

            // Port of startstop.py:70 threadpool_stop get_async_threadpool().stop(True,10)
            ShutdownStep("threadpool_stop", () =>
            {
                var p = pool ?? TryGetPool();
                if (p is not null) p.Stop(wait: true, timeout: TimeSpan.FromSeconds(10));
            });

            if (settings.AutosaveOnShutdown)
                SaveWorld(settings);

            // truncate the WAL after the final save so -wal/-shm
            // files cannot grow unbounded across restarts. Best-effort and
            // post-save by design (a checkpoint before the save would be
            // immediately re-dirtied).
            ShutdownStep("wal_checkpoint", () =>
            {
                try { using var ctx = AtherizDbContextFactory.CreateForSettings(settings); ctx.CheckpointWal(); }
                catch (Exception) { }
            });

            // Port of startstop.py:75 msg_all("Server is shutting down NOW!")
            // — last, after ticker/pool stops and saves, exactly as upstream.
            // Delivery is synchronous socket writes (no pool/ticker needed),
            // so the warning still reaches clients in this position.
            ShutdownStep("msg_all", () =>
            {
                try
                {
                    // Port of utils.msg_all — broadcast to all connected PCs or fallback to channel
                    var msg = "Server is shutting down NOW!";
                    // Use ConnectionManager broadcast if available
                    try
                    {
                        var cm = TryGetConnectionManager();
                        cm?.Broadcast(msg);
                    }
                    catch (Exception) { }
                    // Also try ObjectRegistry filter as utils.msg_all does
                    try
                    {
                        foreach (var obj in ObjectRegistry.FilterBy(o => o.IsPc && o.IsConnected))
                            try { obj.Msg(msg); } catch (Exception) { }
                    }
                    catch (Exception) { }
                    Console.Error.WriteLine(msg);
                }
                catch (Exception) { }
            });

            Console.Error.WriteLine("Shutdown sequence completed."); // Port of logger.info

            // Port of startstop.py:77-81 with _SINGLETON_LOCK: _ASYNC_THREAD_POOL=None etc
            ShutdownStep("clear_singletons", () =>
            {
                try
                {
                    // Faithful: clear only those three per Python, via GlobalServices helper
                    GlobalServices.ClearForShutdown();
                }
                catch (Exception) { }
            });

            // Port of startstop.py:82 db_close get_database().close
            ShutdownStep("db_close", () =>
            {
                try
                {
                    // In C# AtherizDbContext is per-call, not singleton; ensure gate released
                    // Simulate get_database().close by disposing a factory context
                    using var db = new AtherizDbContext(settings.SavePath);
                    try { db.Database.CloseConnection(); } catch (Exception) { }
                }
                catch (Exception) { }
            });

            _started = false;
        }
    }

    // Port of startstop.py:85-122 _reregister_ticks
    private static void ReregisterTicks(AsyncTicker ticker)
    {
        // Port of startstop.py:94-102 for obj in filter_by(_is_tickable): ticker.add_coro(at_tick, _tick_seconds)
        try
        {
            foreach (var obj in ObjectRegistry.FilterBy(o => o.IsTickable))
            {
                var atTick = TryGetAtTick(obj);
                if (atTick is null) continue;
                double seconds = 1.0;
                try { seconds = obj.TickSeconds; } catch { seconds = 1.0; }
                if (seconds <= 0) seconds = 1.0;
                try { ticker.AddCoro(atTick, seconds); }
                catch (Exception ex) { Atheriz.Core.AtherizLogger.LogError($"Failed to re-register tick for object {obj.Id}:\n{ex}"); }
            }
        }
        catch (Exception ex) { Atheriz.Core.AtherizLogger.LogError($"Tick re-registration failed (objects):\n{ex}"); }

        // Port of startstop.py:103-122 node handler grids
        try
        {
            var nh = TryGetNodeHandler();
            if (nh is null) return;
            List<NodeArea> areas;
            // GetAreas snapshots under its own read lock; wrapping it in
            // another read here only works via lock recursion.
            areas = nh.GetAreas();
            foreach (var area in areas)
            {
                List<NodeGrid> grids;
                area.Lock.EnterReadLock();
                try { grids = area.Grids.Values.ToList(); }
                finally { area.Lock.ExitReadLock(); }
                foreach (var grid in grids)
                {
                    List<Node> nodes;
                    grid.Lock.EnterReadLock();
                    try { nodes = grid.Nodes.Values.Where(n => n.IsTickable).ToList(); }
                    finally { grid.Lock.ExitReadLock(); }
                    foreach (var node in nodes)
                    {
                        var atTick = TryGetAtTick(node);
                        if (atTick is null) continue;
                        double seconds = 1.0;
                        try { seconds = node.TickSeconds; } catch { seconds = 1.0; }
                        if (seconds <= 0) seconds = 1.0;
                        try { ticker.AddCoro(atTick, seconds); }
                        catch (Exception ex) { Atheriz.Core.AtherizLogger.LogError($"Failed to re-register tick for node {node.Id}:\n{ex}"); }
                    }
                }
            }
        }
        catch (Exception ex) { Atheriz.Core.AtherizLogger.LogError($"Node tick re-registration failed:\n{ex}"); }
    }

    private static Action? TryGetAtTick(object obj)
    {
        // Typed dispatch (replaces GetMethod("AtTick") reflection): every
        // tickable is a GameObject (Node overrides AtTick). Tick faults stay
        // silent per-tick, matching the old Invoke catch-swallow.
        if (obj is Atheriz.Core.Objects.GameObject go)
            return () => { try { go.AtTick(); } catch (Exception ex) { AtherizLogger.LogWarning($"AtTick failed on '{go.Name}': {ex.Message}"); } };
        return null;
    }

    // Port of startstop.py:125-153 do_reload
    public static void DoReload(AtherizSettings? settings = null, AsyncTicker? ticker = null)
    {
        settings ??= AtherizSettings.Global;
        lock (_worldLock)
        {
            // Port of startstop.py:127 channel msg
            try
            {
                var ch = GlobalServices.GetServerChannel();
                if (ch is not null) try { ch.Msg("Server is reloading..."); } catch (Exception) { }
            }
            catch (Exception) { }

            Console.Error.WriteLine("Starting reload sequence..."); // Port of logger.info

            // Port of startstop.py:131-137 single at_server_reload() call (was invoked twice; fixed).
            ShutdownStep("at_server_reload", () => TryInvokeServerEvent("AtServerReload"));

            // Port of startstop.py:138-139 if TIME_SYSTEM_ENABLED: get_game_time().stop()
            if (settings.TimeSystemEnabled)
            {
                ShutdownStep("game_time_stop", () =>
                {
                    try
                    {
                        var gt = TryGetGameTime();
                        var t = ticker ?? TryGetTicker();
                        if (gt is not null && t is not null) gt.Stop(t);
                        else if (gt is not null) gt.Stop();
                    }
                    catch (Exception) { }
                });
            }

            // Port of startstop.py:140 stop_autosave()
            ShutdownStep("stop_autosave", () =>
            {
                try
                {
                    var t = ticker ?? TryGetTicker();
                    if (t is not null) Autosave.StopAutosave(t);
                }
                catch (Exception) { }
            });

            // Port of startstop.py:141 get_async_ticker().clear()
            ShutdownStep("ticker_clear", () =>
            {
                var t = ticker ?? TryGetTicker() ?? GlobalServices.GetAsyncTicker();
                t.Clear();
            });

            // Port of startstop.py:142 _reregister_ticks()
            ShutdownStep("reregister_ticks", () =>
            {
                var t = ticker ?? TryGetTicker() ?? GlobalServices.GetAsyncTicker();
                ReregisterTicks(t);
            });

            // Port of startstop.py:143-144 if TIME_SYSTEM_ENABLED: get_game_time().start()
            if (settings.TimeSystemEnabled)
            {
                ShutdownStep("game_time_start", () =>
                {
                    try
                    {
                        // Boot from the passed settings like DoStartup does — the
                        // ambient global would give the wrong SavePath/cadence on
                        // an explicit-settings reload.
                        var gt = GlobalServices.GetGameTime(settings);
                        var t = ticker ?? TryGetTicker() ?? GlobalServices.GetAsyncTicker();
                        gt.Start(t);
                    }
                    catch (Exception ex) { Console.Error.WriteLine($"game_time start failed:\n{ex}"); }
                });
            }

            if (settings.AutosaveOnReload)
                SaveWorld(settings);

            // Port of startstop.py:149 start_autosave()
            ShutdownStep("start_autosave", () =>
            {
                try
                {
                    if (settings.AutosaveMinutes != 0)
                    {
                        var t = ticker ?? TryGetTicker() ?? GlobalServices.GetAsyncTicker();
                        var mh = TryGetMapHandler();
                        var nh = TryGetNodeHandler();
                        var gt = settings.TimeSystemEnabled ? TryGetGameTime() : null;
                        Autosave.StartAutosave(t, settings, mh, nh, gt);
                    }
                }
                catch (Exception) { }
            });

            // Port of startstop.py:150-152 channel msg reloaded
            try
            {
                var ch = GlobalServices.GetServerChannel();
                if (ch is not null) try { ch.Msg("Server reloaded"); } catch (Exception) { }
            }
            catch (Exception) { }

            Console.Error.WriteLine("Reload sequence completed."); // Port of logger.info
        }
    }

    // Port of startstop.py:71-74 AUTOSAVE_ON_SHUTDOWN / AUTOSAVE_ON_RELOAD — shared SaveWorld helper
    // Faithful: uses ShutdownStep per save, mirroring Python _shutdown_step
    private static void SaveWorld(AtherizSettings settings)
    {
        // Crash-consistency journal: see AutosaveTick.
        Persistence.CheckpointJournal.MarkDirty(settings.SavePath);
        bool ok = true;
        // One transaction for all three groups. WithGateAndTransaction joins an
        // ambient transaction instead of opening its own, so opening one here
        // makes the checkpoint atomic: a crash (or a failing group) rolls back
        // objects+map+node together instead of tearing between groups. The gate
        // is held for the whole checkpoint because the inner saves skip their
        // own gate take once the ambient transaction exists — without this a
        // concurrent tick could interleave mid-checkpoint.
        // Bounded take: on timeout the checkpoint still runs (old shape, journal
        // still detects) rather than skipping the shutdown save entirely.
        bool atomic = DbWriteGate.TryEnter(TimeSpan.FromSeconds(30));
        if (!atomic)
            Console.Error.WriteLine("checkpoint gate busy; saving without atomic transaction (journal still detects).");
        try
        {
            using var db = new AtherizDbContext(settings.SavePath);
            db.Database.EnsureCreated();
            if (atomic)
            {
                using var tx = db.Database.BeginTransaction();
                RunCheckpointSteps(db, ref ok);
                if (ok) tx.Commit();
                // !ok: dispose uncommitted = rollback; the journal stays dirty.
            }
            else RunCheckpointSteps(db, ref ok);
        }
        catch (Exception ex) { ok = false; Console.Error.WriteLine($"checkpoint context failed:\n{ex}"); }
        finally { if (atomic) DbWriteGate.Exit(); }
        if (ok) Persistence.CheckpointJournal.MarkClean(settings.SavePath);
    }

    // The three save groups shared by the atomic and fallback shapes above.
    // A step retry reuses the SAME context: a separate context's writes would
    // hit SQLITE_BUSY against the open outer transaction (and break the very
    // atomicity the checkpoint exists for). The tracker is cleared first — a
    // failed SaveChanges may have left it poisoned (mirrors
    // WithGateAndTransaction's own retry hygiene); the save re-fetches
    // everything via Find. The primary path still commits once.
    private static void RunCheckpointSteps(AtherizDbContext db, ref bool ok)
    {
        bool localOk = true;
        ShutdownStep("save_objects", () =>
            {
                try { ObjectRegistry.SaveObjects(db); }
                catch (Exception ex) { localOk = false; Console.Error.WriteLine($"save_objects failed:\n{ex}"); }
            });
            ShutdownStep("map_save", () =>
            {
                try
                {
                    var mh = GlobalServices.GetMapHandler();
                    mh.Save(db);
                }
                catch
                {
                    try
                    {
                        var mh = GlobalServices.GetMapHandler();
                        db.ChangeTracker.Clear();
                        mh.Save(db);
                    }
                    catch { localOk = false; }
                }
            });
            ShutdownStep("node_save", () =>
            {
                try
                {
                    var nh = GlobalServices.GetNodeHandler();
                    nh.Save(db);
                }
                catch
                {
                    try
                    {
                        var nh = GlobalServices.GetNodeHandler();
                        db.ChangeTracker.Clear();
                        nh.Save(db);
                    }
                    catch { localOk = false; }
                }
            });
        if (!localOk) ok = false;
    }

    // Helpers to avoid creating singletons unnecessarily during shutdown

    private static AsyncTicker? TryGetTicker()
    {
        try { return GlobalServices.TryGetTicker(); } catch { return null; }
    }
    private static AsyncThreadPool? TryGetPool()
    {
        try { return GlobalServices.TryGetPool(); } catch { return null; }
    }
    private static GameTime? TryGetGameTime()
    {
        try { return GlobalServices.TryGetGameTime(); } catch { return null; }
    }
    private static MapHandler? TryGetMapHandler()
    {
        try { return GlobalServices.TryGetMapHandler(); } catch { return null; }
    }
    private static NodeHandler? TryGetNodeHandler()
    {
        try { return GlobalServices.TryGetNodeHandler(); } catch { return null; }
    }
    private static ConnectionManager? TryGetConnectionManager()
    {
        try { return GlobalServices.TryGetConnectionManager(); } catch { return null; }
    }

    // Game-side server-event handlers registered explicitly by game/plugin
    // assemblies (replaces the assembly scan for server_events/ServerEvents types).
    private static readonly Dictionary<string, List<Action>> _gameServerEventHandlers = new(StringComparer.Ordinal);
    private static readonly object _gameServerEventLock = new();
    /// <summary>Registers a game-side handler invoked after the core server event.</summary>
    public static void RegisterGameServerEvent(string methodName, Action handler)
    {
        if (string.IsNullOrEmpty(methodName)) throw new ArgumentException("Server event name required.", nameof(methodName));
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gameServerEventLock)
        {
            if (!_gameServerEventHandlers.TryGetValue(methodName, out var list))
                _gameServerEventHandlers[methodName] = list = [];
            list.Add(handler);
        }
    }
    private static void TryInvokeServerEvent(string methodName)
    {
        // Concrete core dispatch — Port of atheriz/server_events.py:8 (no string lookup).
        try
        {
            switch (methodName)
            {
                case "AtServerStart": Atheriz.Core.ServerEvents.AtServerStart(); break;
                case "AtServerStop": Atheriz.Core.ServerEvents.AtServerStop(); break;
                case "AtServerReload": Atheriz.Core.ServerEvents.AtServerReload(); break;
                default: break;
            }
        }
        catch (Exception) { }
        // Game-side handlers registered via RegisterGameServerEvent.
        List<Action>? handlers = null;
        lock (_gameServerEventLock) { if (_gameServerEventHandlers.TryGetValue(methodName, out var list)) handlers = new List<Action>(list); }
        if (handlers is not null)
        {
            foreach (var h in handlers)
            {
                try { h(); } catch (Exception ex) { AtherizLogger.LogWarning($"Reload handler failed: {ex.Message}"); }
            }
        }
    }

    // For tests — mirrors ServerLifecycle.Reset and Python _shutdown_completed reset
    public static void Reset()
    {
        lock (_worldLock)
        {
            _shutdownCompleted = false;
            _shuttingDown = false;
            _started = false;
        }
        try { Autosave.Reset(); } catch (Exception) { }
        try { GlobalServices.Reset(); } catch (Exception) { }
        try { MapEdit.Reset(); } catch (Exception) { }
    }

}
