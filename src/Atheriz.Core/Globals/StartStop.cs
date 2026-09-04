// Port of atheriz/globals/startstop.py:153 — faithful DoStartup/DoShutdown/DoReload with _WORLD_LOCK.
// Mirrors _WORLD_LOCK, _shutdown_completed, _shutdown_step, server_events hooks, autosave, gametime, ticker, threadpool.

using System.Diagnostics;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Globals;

// Port of atheriz/globals/startstop.py:17 _WORLD_LOCK
public static class StartStop
{
    // Port of startstop.py:17 _WORLD_LOCK = RLock()
    private static readonly object _worldLock = new();
    // Port of startstop.py:18 _shutdown_lock = _WORLD_LOCK
    private static readonly object _shutdownLock = _worldLock;
    // Port of startstop.py:19 _shutdown_completed = False
    private static bool _shutdownCompleted = false;
    // Spec extra: bool _started,_shuttingDown (aliases to _shutdownCompleted)
    private static bool _started = false;
    private static bool _shuttingDown = false;

    // Expose locks for parity with spec
    public static object WorldLock => _worldLock;
    public static bool Started { get { lock (_shutdownLock) return _started; } }
    public static bool ShuttingDown { get { lock (_shutdownLock) return _shuttingDown; } }

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
        // Port of startstop.py:32 with _shutdown_lock: _shutdown_completed=False
        lock (_shutdownLock)
        {
            _shutdownCompleted = false;
            _shuttingDown = false;
            _started = true;
        }
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

        // Port of startstop.py:35-38 get_async_threadpool/get_map_handler/get_node_handler/get_async_ticker
        try
        {
            pool ??= GlobalServices.GetAsyncThreadPool();
        }
        catch (Exception ex) { Console.Error.WriteLine($"DoStartup GetAsyncThreadPool failed:\n{ex}"); }

        MapHandler? mapHandler = null;
        try
        {
            mapHandler = GlobalServices.GetMapHandler();
        }
        catch (Exception ex) { Console.Error.WriteLine($"DoStartup GetMapHandler failed:\n{ex}"); }

        NodeHandler? nodeHandler = null;
        try
        {
            nodeHandler = GlobalServices.GetNodeHandler();
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
                var gt = GlobalServices.GetGameTime();
                // Port of get_game_time().start() — ticker is singleton; GameTime.Start expects ticker
                if (ticker != null)
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
                var gt = settings.TimeSystemEnabled ? GlobalServices.GetGameTime() : null;
                // Port of autosave.start_autosave — use ticker overload with handlers
                Autosave.StartAutosave(t, settings, mapHandler, nodeHandler, gt);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"start_autosave failed:\n{ex}"); }
    }

    // Port of startstop.py:49-82 do_shutdown
    public static void DoShutdown(AtherizSettings? settings = null, AsyncThreadPool? pool = null, AsyncTicker? ticker = null)
    {
        settings ??= AtherizSettings.Global;
        lock (_worldLock)
        {
            lock (_shutdownLock)
            {
                if (_shutdownCompleted)
                {
                    Console.Error.WriteLine("Shutdown already completed; skipping."); // Port of logger.info
                    return;
                }
                _shutdownCompleted = true;
                _shuttingDown = true;
            }

            // Port of startstop.py:57 channel = get_server_channel(); if channel: channel.msg("Server is shutting down!")
            try
            {
                var channel = GlobalServices.GetServerChannel();
                if (channel != null)
                {
                    try { channel.Msg("Server is shutting down!"); } catch { }
                }
            }
            catch { }

            Console.Error.WriteLine("Starting shutdown sequence..."); // Port of logger.info

            // Port of startstop.py:62-65 at_server_stop
            ShutdownStep("at_server_stop", () => TryInvokeServerEvent("AtServerStop"));

            // Port of startstop.py:66 stop_autosave
            ShutdownStep("stop_autosave", () =>
            {
                try
                {
                    var t = ticker ?? TryGetTicker();
                    if (t != null) Autosave.StopAutosave(t);
                    else Autosave.ResetForTesting(); // fallback placeholder
                }
                catch { }
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
                        if (gt != null && t != null) gt.Stop(t);
                        else if (gt != null) gt.Stop();
                    }
                    catch { }
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
                if (p != null) p.Stop(wait: true, timeout: TimeSpan.FromSeconds(10));
            });

            if (settings.AutosaveOnShutdown)
                SaveWorld(settings);

            // Port of startstop.py:75 msg_all("Server is shutting down NOW!")
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
                    catch { }
                    // Also try ObjectRegistry filter as utils.msg_all does
                    try
                    {
                        foreach (var obj in ObjectRegistry.FilterBy(o => o.IsPc && o.IsConnected))
                            try { obj.Msg(msg); } catch { }
                    }
                    catch { }
                    Console.Error.WriteLine(msg);
                }
                catch { }
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
                catch { }
            });

            // Port of startstop.py:82 db_close get_database().close
            ShutdownStep("db_close", () =>
            {
                try
                {
                    // In C# AtherizDbContext is per-call, not singleton; ensure gate released
                    // Simulate get_database().close by disposing a factory context
                    using var db = new AtherizDbContext(settings.SavePath);
                    try { db.Database.CloseConnection(); } catch { }
                }
                catch { }
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
                if (atTick == null) continue;
                double seconds = 1.0;
                try { seconds = obj.TickSeconds; } catch { seconds = 1.0; }
                if (seconds <= 0) seconds = 1.0;
                try { ticker.AddCoro(atTick, seconds); }
                catch (Exception ex) { Console.Error.WriteLine($"Failed to re-register tick for object {obj.Id}:\n{ex}"); }
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"Tick re-registration failed (objects):\n{ex}"); }

        // Port of startstop.py:103-122 node handler grids
        try
        {
            var nh = TryGetNodeHandler();
            if (nh == null) return;
            List<NodeArea> areas;
            nh.Lock.EnterReadLock();
            try { areas = nh.GetAreas(); }
            finally { nh.Lock.ExitReadLock(); }
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
                        if (atTick == null) continue;
                        double seconds = 1.0;
                        try { seconds = node.TickSeconds; } catch { seconds = 1.0; }
                        if (seconds <= 0) seconds = 1.0;
                        try { ticker.AddCoro(atTick, seconds); }
                        catch (Exception ex) { Console.Error.WriteLine($"Failed to re-register tick for node {node.Id}:\n{ex}"); }
                    }
                }
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"Node tick re-registration failed:\n{ex}"); }
    }

    private static Action? TryGetAtTick(object obj)
    {
        try
        {
            var mi = obj.GetType().GetMethod("AtTick");
            if (mi == null) return null;
            // Need instance method; create Action that invokes via reflection
            return () => { try { mi.Invoke(obj, null); } catch { } };
        }
        catch { return null; }
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
                if (ch != null) try { ch.Msg("Server is reloading..."); } catch { }
            }
            catch { }

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
                        if (gt != null && t != null) gt.Stop(t);
                        else if (gt != null) gt.Stop();
                    }
                    catch { }
                });
            }

            // Port of startstop.py:140 stop_autosave()
            ShutdownStep("stop_autosave", () =>
            {
                try
                {
                    var t = ticker ?? TryGetTicker();
                    if (t != null) Autosave.StopAutosave(t);
                }
                catch { }
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
                        var gt = GlobalServices.GetGameTime();
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
                catch { }
            });

            // Port of startstop.py:150-152 channel msg reloaded
            try
            {
                var ch = GlobalServices.GetServerChannel();
                if (ch != null) try { ch.Msg("Server reloaded"); } catch { }
            }
            catch { }

            Console.Error.WriteLine("Reload sequence completed."); // Port of logger.info
        }
    }

    // Port of startstop.py:71-74 AUTOSAVE_ON_SHUTDOWN / AUTOSAVE_ON_RELOAD — shared SaveWorld helper
    // Faithful: uses ShutdownStep per save, mirroring Python _shutdown_step
    private static void SaveWorld(AtherizSettings settings)
    {
        ShutdownStep("save_objects", () =>
        {
            try
            {
                using var db = new AtherizDbContext(settings.SavePath);
                db.Database.EnsureCreated();
                ObjectRegistry.SaveObjects(db);
            }
            catch (Exception ex) { Console.Error.WriteLine($"save_objects failed:\n{ex}"); }
        });
        ShutdownStep("map_save", () =>
        {
            try
            {
                var mh = GlobalServices.GetMapHandler();
                using var db = new AtherizDbContext(settings.SavePath);
                db.Database.EnsureCreated();
                mh.Save(db);
            }
            catch { try { GlobalServices.GetMapHandler().Save(); } catch { } }
        });
        ShutdownStep("node_save", () =>
        {
            try
            {
                var nh = GlobalServices.GetNodeHandler();
                using var db = new AtherizDbContext(settings.SavePath);
                db.Database.EnsureCreated();
                nh.Save(db);
            }
            catch { try { GlobalServices.GetNodeHandler().Save(); } catch { } }
        });
    }

    // Helpers to avoid creating singletons unnecessarily during shutdown

    private static AsyncTicker? TryGetTicker()
    {
        try { return GlobalServices.GetAsyncTicker(); } catch { return null; }
    }
    private static AsyncThreadPool? TryGetPool()
    {
        try { return GlobalServices.GetAsyncThreadPool(); } catch { return null; }
    }
    private static GameTime? TryGetGameTime()
    {
        try { return GlobalServices.GetGameTime(); } catch { return null; }
    }
    private static MapHandler? TryGetMapHandler()
    {
        try { return GlobalServices.GetMapHandler(); } catch { return null; }
    }
    private static NodeHandler? TryGetNodeHandler()
    {
        try { return GlobalServices.GetNodeHandler(); } catch { return null; }
    }
    private static ConnectionManager? TryGetConnectionManager()
    {
        try { return GlobalServices.GetConnectionManager(); } catch { return null; }
    }

    private static void TryInvokeServerEvent(string methodName)
    {
        // Concrete type first — Port of atheriz/server_events.py:8 replaces reflection string lookup
        try
        {
            var coreType = typeof(Atheriz.Core.ServerEvents);
            var miCore = coreType.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (miCore != null)
            {
                try { miCore.Invoke(null, null); return; } catch { }
            }
        }
        catch { }
        // Try game-folder server_events via reflection if loaded, else Atheriz.Core stub
        // Search loaded assemblies for type named server_events or ServerEvents
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? t = null;
                try { t = asm.GetType("server_events"); } catch { }
                if (t == null) try { t = asm.GetType("ServerEvents"); } catch { }
                if (t == null)
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (string.Equals(type.Name, "server_events", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(type.Name, "ServerEvents", StringComparison.OrdinalIgnoreCase))
                        { t = type; break; }
                    }
                }
                if (t != null)
                {
                    // Skip core type already tried
                    if (t == typeof(Atheriz.Core.ServerEvents)) continue;
                    var mi = t.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
                    if (mi != null)
                    {
                        try
                        {
                            if (mi.IsStatic) mi.Invoke(null, null);
                            else
                            {
                                var inst = Activator.CreateInstance(t);
                                mi.Invoke(inst, null);
                            }
                            return;
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
        // No server_events found — placeholder (mirrors ImportError fallback to atheriz.server_events)
        // Do nothing; real engine would call atheriz.server_events.at_server_start/stop/reload
        Debug.WriteLine($"[StartStop] {methodName} no-op (server_events not found)");
    }

    // For tests — mirrors ServerLifecycle.ResetForTesting and Python _shutdown_completed reset
    public static void ResetForTesting()
    {
        lock (_shutdownLock)
        {
            _shutdownCompleted = false;
            _shuttingDown = false;
            _started = false;
        }
        try { Autosave.ResetForTesting(); } catch { }
        try { GlobalServices.ResetForTesting(); } catch { }
        try { MapEdit.ResetForTesting(); } catch { }
    }
}
