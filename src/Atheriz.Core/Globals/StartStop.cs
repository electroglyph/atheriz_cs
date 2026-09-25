// faithful DoStartup/DoShutdown/DoReload with _WORLD_LOCK.
// Mirrors _WORLD_LOCK, _shutdown_completed, _shutdown_step, server_events hooks, autosave, gametime, ticker, threadpool.

using System.Diagnostics;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Persistence;
using Atheriz.Core.Plugins;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Globals;

public static class StartStop
{
    // _shutdown_lock to the same lock — C# keeps the single name .
    private static readonly object _worldLock = new();
    private static bool _shutdownCompleted = false;
    // Spec extra: bool _started,_shuttingDown (aliases to _shutdownCompleted)
    private static bool _started = false;
    private static bool _shuttingDown = false;

    // Expose locks for parity with spec
    public static object WorldLock => _worldLock;
    public static bool Started { get { lock (_worldLock) return _started; } }
    public static bool ShuttingDown { get { lock (_worldLock) return _shuttingDown; } }

    private static void ShutdownStep(string name, Action fn)
    {
        try { fn(); }
        catch (Exception ex)
        {
            AtherizLogger.LogError($"Shutdown step '{name}' failed:\n{ex}");
        }
    }

    public static void DoStartup(AsyncThreadPool? pool = null, AsyncTicker? ticker = null, AtherizSettings? settings = null)
    {
        settings ??= AtherizSettings.Global;
        // Unlike Python (flag-only hold), the whole load phase holds _worldLock
        // — outermost in the global order, same as DoShutdown/DoReload — so a
        // concurrent shutdown/reload cannot clear singletons mid-boot .
        lock (_worldLock)
        {
            _shutdownCompleted = false;
            _shuttingDown = false;
            _started = true;
            // Engine-owned world subtypes (seeded dashboard, wanderers) must
            // be registered before rows convert, or they reload as their base
            // kind without their overrides.
            InitialSetup.RegisterPersistedSubtypes();
            // Boot-time game load BEFORE the world loads: game assemblies
            // register their own persisted subtypes (and IGameSetup entry) on
            // load, so rows convert directly to game types. Loading the world
            // first would convert every game row to its base kind with a loud
            // "Unknown __object_type" log and only patch it up afterwards.
            // Never throws: failures log and boot continues engine-only.
            try
            {
                Atheriz.Core.Plugins.PluginReloader.LoadGameAssembliesAtBoot(settings);
            }
            catch (Exception ex) { AtherizLogger.LogError($"DoStartup game load failed:\n{ex}"); }
            try
            {
                // The ticker must exist before rows convert: ResolveRelations
                // registers each loaded object's AtTick through TryGetTicker,
                // which stays null until the global is first created. Loading
                // first leaves every object unticked with no error on fresh
                // boots, and nothing re-registers them later.
                ticker ??= GlobalServices.GetAsyncTicker();
            }
            catch (Exception ex) { AtherizLogger.LogError($"DoStartup GetAsyncTicker failed:\n{ex}"); }
            try
            {
                // Use savePath overload which handles DB EnsureCreated
                ObjectRegistry.LoadObjects(settings.SavePath);
            }
            catch (Exception ex)
            {
                AtherizLogger.LogError($"DoStartup LoadObjects failed:\n{ex}");
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
                AtherizLogger.LogError($"DoStartup checkpoint check failed:\n{ex}");
            }

            try
            {
                pool ??= GlobalServices.GetAsyncThreadPool();
            }
            catch (Exception ex) { AtherizLogger.LogError($"DoStartup GetAsyncThreadPool failed:\n{ex}"); }

            MapHandler? mapHandler = null;
            try
            {
                mapHandler = GlobalServices.GetMapHandler(settings);
            }
            catch (Exception ex) { AtherizLogger.LogError($"DoStartup GetMapHandler failed:\n{ex}"); }

            NodeHandler? nodeHandler = null;
            try
            {
                nodeHandler = GlobalServices.GetNodeHandler(settings);
            }
            catch (Exception ex) { AtherizLogger.LogError($"DoStartup GetNodeHandler failed:\n{ex}"); }

            try
            {
                // Try game-folder server_events first, fallback to Core stub
                TryInvokeServerEvent("AtServerStart");
            }
            catch (Exception ex) { AtherizLogger.LogError($"at_server_start failed:\n{ex}"); }

            if (settings.TimeSystemEnabled)
            {
                try
                {
                    var gt = GlobalServices.GetGameTime(settings);
// ticker is singleton; GameTime.Start expects ticker
                    if (ticker is not null)
                        gt.Start(ticker);
                    else
                        gt.Start();
                }
                catch (Exception ex) { AtherizLogger.LogError($"GameTime start failed:\n{ex}"); }
            }

            try
            {
                if (settings.AutosaveMinutes != 0)
                {
                    var t = ticker ?? GlobalServices.GetAsyncTicker();
                    var gt = settings.TimeSystemEnabled ? GlobalServices.GetGameTime(settings) : null;
// use ticker overload with handlers
                    Autosave.StartAutosave(t, settings, mapHandler, nodeHandler, gt);
                }
            }
            catch (Exception ex) { AtherizLogger.LogError($"start_autosave failed:\n{ex}"); }
        }
    }

    // Shared shutdown/reload announce: GetServerChannel → null-check → Msg → swallow.
    // Message stays byte-identical per caller; empty catches preserved.
    private static void AnnounceChannel(string message)
    {
        try
        {
            var channel = GlobalServices.GetServerChannel();
            if (channel is not null)
            {
                try { channel.Msg(message); } catch (Exception) { }
            }
        }
        catch (Exception) { }
    }

    // Shared game-time stop: ticker ?? TryGetTicker(), then Stop(t) or Stop().
    private static void StopGameTime(AsyncTicker? ticker)
    {
        try
        {
            var gt = TryGetGameTime();
            var t = ticker ?? TryGetTicker();
            if (gt is not null && t is not null) gt.Stop(t);
            else if (gt is not null) gt.Stop();
        }
        catch (Exception) { }
    }

    public static void DoShutdown(AtherizSettings? settings = null, AsyncThreadPool? pool = null, AsyncTicker? ticker = null)
    {
        settings ??= AtherizSettings.Global;
        lock (_worldLock)
        {
            if (_shutdownCompleted)
            {
                AtherizLogger.LogInformation("Shutdown already completed; skipping.");
                return;
            }
            _shutdownCompleted = true;
            _shuttingDown = true;

            AnnounceChannel("Server is shutting down!");

            AtherizLogger.LogInformation("Starting shutdown sequence...");

            ShutdownStep("at_server_stop", () => TryInvokeServerEvent("AtServerStop"));

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

            if (settings.TimeSystemEnabled)
            {
                ShutdownStep("game_time_stop", () => StopGameTime(ticker));
            }

            ShutdownStep("ticker_stop", () =>
            {
                var t = ticker ?? TryGetTicker();
                t?.Stop();
            });

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

            // — last, after ticker/pool stops and saves, exactly as upstream.
            // Delivery is synchronous socket writes (no pool/ticker needed),
            // so the warning still reaches clients in this position.
            ShutdownStep("msg_all", () =>
            {
                try
                {
// broadcast to all connected PCs or fallback to channel
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
                    AtherizLogger.LogInformation(msg);
                }
                catch (Exception) { }
            });

            AtherizLogger.LogInformation("Shutdown sequence completed.");

            ShutdownStep("clear_singletons", () =>
            {
                try
                {
                    // Faithful: clear only those three per Python, via GlobalServices helper
                    GlobalServices.ClearForShutdown();
                }
                catch (Exception) { }
            });

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

    private static void ReregisterTicks(AsyncTicker ticker)
    {
        // One choke point for the registry and node-grid sweeps below:
        // same try-get → fallback seconds → clamp → AddCoro → per-target
        // LogError. The two enumerations stay as-is; only the per-target
        // body is shared. Error text stays byte-identical via the label.
        void RegisterTick(GameObject go, string label)
        {
            var atTick = TryGetAtTick(go);
            if (atTick is null) return;
            double seconds = 1.0;
            try { seconds = go.TickSeconds; } catch { seconds = 1.0; }
            if (seconds <= 0) seconds = 1.0;
            try { ticker.AddCoro(atTick, seconds); }
            catch (Exception ex) { Atheriz.Core.AtherizLogger.LogError($"Failed to re-register tick for {label} {go.Id}:\n{ex}"); }
        }
        //
        // Gather-then-register: the registry sweep used to register every
        // tickable inline and the grid sweep re-registered tickable nodes
        // with no dedupe (nodes are AddObject'd — NodeHandler.AddNode), so
        // one tickable node got two delegates and ticked twice per interval.
        // Grid nodes already gathered are skipped by instance (not snapshot
        // equality), mirroring the PluginReloader twin; the shared delegate
        // sweep runs first so repeat reloads evict stale delegates instead
        // of stacking new ones.
        List<(GameObject Go, string Label)> tickables = [];
        try
        {
            foreach (var obj in ObjectRegistry.FilterBy(o => o.IsTickable))
            {
                tickables.Add((obj, "object"));
            }
        }
        catch (Exception ex) { Atheriz.Core.AtherizLogger.LogError($"Tick re-registration failed (objects):\n{ex}"); }

        try
        {
            var nh = TryGetNodeHandler();
            if (nh is not null)
            {
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
                            if (!tickables.Any(t => ReferenceEquals(t.Go, node))) tickables.Add((node, "node"));
                        }
                    }
                }
            }
        }
        catch (Exception ex) { Atheriz.Core.AtherizLogger.LogError($"Node tick re-registration failed:\n{ex}"); }

        PluginReloader.RemoveTickDelegatesFor(ticker, tickables.Select(t => t.Go).ToList());
        foreach (var (go, label) in tickables)
        {
            RegisterTick(go, label);
        }
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

    public static void DoReload(AtherizSettings? settings = null, AsyncTicker? ticker = null)
    {
        settings ??= AtherizSettings.Global;
        lock (_worldLock)
        {
            // Single ticker resolve: every step below used to resolve
            // `ticker ?? TryGetTicker() ?? GetAsyncTicker()` independently, so
            // a Reset/ClearForShutdown landing between two steps (or a
            // fault-restore swap inside GetAsyncTicker) cleared one ticker
            // while registering on a discarded/new one — ticks silently lost.
            // One local observes a single generation for the whole reload.
            var tick = ticker ?? TryGetTicker() ?? GlobalServices.GetAsyncTicker();
            AnnounceChannel("Server is reloading...");

            AtherizLogger.LogInformation("Starting reload sequence...");

            ShutdownStep("at_server_reload", () => TryInvokeServerEvent("AtServerReload"));

            if (settings.TimeSystemEnabled)
            {
                ShutdownStep("game_time_stop", () => StopGameTime(ticker));
            }

            ShutdownStep("stop_autosave", () =>
            {
                try
                {
                    Autosave.StopAutosave(tick);
                }
                catch (Exception) { }
            });

            ShutdownStep("ticker_clear", () =>
            {
                tick.Clear();
            });

            ShutdownStep("reregister_ticks", () =>
            {
                ReregisterTicks(tick);
            });

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
                        gt.Start(tick);
                    }
                    catch (Exception ex) { AtherizLogger.LogError($"game_time start failed:\n{ex}"); }
                });
            }

            if (settings.AutosaveOnReload)
                SaveWorld(settings);

            ShutdownStep("start_autosave", () =>
            {
                try
                {
                    if (settings.AutosaveMinutes != 0)
                    {
                        var mh = TryGetMapHandler();
                        var nh = TryGetNodeHandler();
                        var gt = settings.TimeSystemEnabled ? TryGetGameTime() : null;
                        Autosave.StartAutosave(tick, settings, mh, nh, gt);
                    }
                }
                catch (Exception) { }
            });

            AnnounceChannel("Server reloaded");

            AtherizLogger.LogInformation("Reload sequence completed.");
        }
    }

// shared SaveWorld helper
    // Faithful: uses ShutdownStep per save, mirroring Python _shutdown_step
    private static void SaveWorld(AtherizSettings settings)
    {
        // Crash-consistency journal: see AutosaveTick.
        string savePath = Persistence.AtherizDbContextFactory.ResolveSavePath(settings);
        Persistence.CheckpointJournal.MarkDirty(savePath);
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
            AtherizLogger.LogWarning("checkpoint gate busy; saving without atomic transaction (journal still detects).");
        try
        {
            using var db = new AtherizDbContext(savePath);
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
        catch (Exception ex) { ok = false; AtherizLogger.LogError($"checkpoint context failed:\n{ex}"); }
        finally { if (atomic) DbWriteGate.Exit(); }
        if (ok) Persistence.CheckpointJournal.MarkClean(savePath);
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
                catch (Exception ex) { localOk = false; AtherizLogger.LogError($"save_objects failed:\n{ex}"); }
            });
        // Twin retry for the map/node saves: fetch the handler, save, and on
        // failure re-fetch + clear the tracker + save once more (same
        // context, per the method comment above). save_objects is NOT
        // wrapped: ObjectRegistry.SaveObjects retries internally, so an outer
        // retry would only double-write — deliberate asymmetry, not omission.
        void ShutdownSaveStep<THandler>(string name, Func<THandler> getHandler, Action<AtherizDbContext, THandler> save)
        {
            ShutdownStep(name, () =>
            {
                try { save(db, getHandler()); }
                catch
                {
                    try
                    {
                        var h = getHandler();
                        db.ChangeTracker.Clear();
                        save(db, h);
                    }
                    catch { localOk = false; }
                }
            });
        }
        ShutdownSaveStep("map_save", () => GlobalServices.GetMapHandler(), (ctx, mh) => mh.Save(ctx));
        ShutdownSaveStep("node_save", () => GlobalServices.GetNodeHandler(), (ctx, nh) => nh.Save(ctx));
        if (!localOk) ok = false;
    }

    // Helpers to avoid creating singletons unnecessarily during shutdown

    // Thin forwards: GlobalServices.TryGet* already swallow (they only
    // Volatile.Read a snapshot), so no second armor layer here.
    private static AsyncTicker? TryGetTicker() => GlobalServices.TryGetTicker();
    private static AsyncThreadPool? TryGetPool() => GlobalServices.TryGetPool();
    private static GameTime? TryGetGameTime() => GlobalServices.TryGetGameTime();
    private static MapHandler? TryGetMapHandler() => GlobalServices.TryGetMapHandler();
    private static NodeHandler? TryGetNodeHandler() => GlobalServices.TryGetNodeHandler();
    private static ConnectionManager? TryGetConnectionManager() => GlobalServices.TryGetConnectionManager();

    // Game-side server-event handlers registered explicitly by game/plugin
    // assemblies (replaces the assembly scan for server_events/ServerEvents types).
    private static readonly Dictionary<string, List<Action>> _gameServerEventHandlers = new(StringComparer.Ordinal);
    private static readonly Lock _gameServerEventLock = new();
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
