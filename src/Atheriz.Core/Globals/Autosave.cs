using Atheriz.Core.Concurrency;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Atheriz.Core.Globals;

/// <summary>
/// Port of <c>atheriz/globals/autosave.py</c> (94 LOC).
/// Keeps _autosave_started flag, start_autosave using AsyncTicker and save_objects etc.
/// Persistence via EF Core JSON (replaces dill handling) — delegates to ObjectRegistry/NodeHandler/MapHandler/GameTime.
/// </summary>
public static class Autosave
{
    private static readonly object _lock = new();
    private static bool _autosaveStarted = false;
    private static double? _registeredInterval = null;

    // Optional cached handlers / settings for autosave_tick without args
    private static AtherizSettings? _cachedSettings;
    private static MapHandler? _cachedMap;
    private static NodeHandler? _cachedNodes;
    private static GameTime? _cachedTime;
    private static AsyncTicker? _globalTicker;
    // Ticker an explicit-ticker StartAutosave ran on (Python always uses the
    // global ticker, so stop needs no handle; here the parameterless Stop must
    // still reach a coro registered on a caller-supplied ticker).
    private static AsyncTicker? _startedTicker;

    public static bool AutosaveStarted
    {
        get { lock (_lock) return _autosaveStarted; }
    }

    private static double IntervalSeconds(AtherizSettings s) => s.AutosaveMinutes * 60.0;

    /// <summary>
    /// Mirrors <c>autosave_tick</c>: saves objects, map, node, time.
    /// Failures are collected and logged; channel msg stubbed.
    /// </summary>
    public static void AutosaveTick()
    {
        AtherizSettings? settings;
        MapHandler? map;
        NodeHandler? nodes;
        GameTime? time;
        lock (_lock)
        {
            settings = _cachedSettings;
            map = _cachedMap;
            nodes = _cachedNodes;
            time = _cachedTime;
        }
        AutosaveTick(settings, map, nodes, time);
    }

    public static void AutosaveTick(AtherizSettings? settings, MapHandler? mapHandler = null, NodeHandler? nodeHandler = null, GameTime? gameTime = null)
    {
        settings ??= _cachedSettings ?? AtherizSettings.Global;
        var failures = new List<string>();

        // Crash-consistency journal: dirty before tables, clean
        // after all commit. A crash between tables leaves dirty behind.
        // Journal, objects, and all handler saves below honor explicit
        // settings (each section commits independently to the same DB).
        CheckpointJournal.MarkDirty(AtherizDbContextFactory.ResolveSavePath(settings));

        // objects
        try
        {
            using var db = AtherizDbContextFactory.CreateForSettings(settings);
            db.Database.EnsureCreated();
            ObjectRegistry.SaveObjects(db);
        }
        catch (Exception ex)
        {
            failures.Add("objects");
            try { AtherizLogger.LogError($"Autosave failed for objects:\n{ex}"); } catch { Console.Error.WriteLine($"Autosave failed for objects:\n{ex}"); }
        }

        // map — Port of autosave.py:26 get_map_handler().save() singleton reuse
        try
        {
            // volatile read — written under _lock by Start/Stop on
            // other threads; a torn read would save via a stale handler.
            var mh = mapHandler ?? Volatile.Read(ref _cachedMap) ?? GlobalServices.GetMapHandler();
            // A3-G-2: save into the explicit-settings DB, not the ambient one.
            // The parameterless Save() persists singleton state via the ambient
            // path — under explicit settings that tore the world (objects in
            // DB-A, handlers in DB-B). Each section keeps its own commit so a
            // single failing domain still doesn't block the others.
            using var dbMap = AtherizDbContextFactory.CreateForSettings(settings);
            mh.Save(dbMap);
        }
        catch (Exception ex)
        {
            failures.Add("map");
            try { AtherizLogger.LogError($"Autosave failed for map:\n{ex}"); } catch { Console.Error.WriteLine($"Autosave failed for map:\n{ex}"); }
        }

        // node — Port of autosave.py:27 get_node_handler().save() singleton reuse
        try
        {
            var nh = nodeHandler ?? Volatile.Read(ref _cachedNodes) ?? GlobalServices.GetNodeHandler();
            // A3-G-2: explicit-settings DB (see map section above).
            using var dbNode = AtherizDbContextFactory.CreateForSettings(settings);
            nh.Save(dbNode);
        }
        catch (Exception ex)
        {
            failures.Add("node");
            try { AtherizLogger.LogError($"Autosave failed for node:\n{ex}"); } catch { Console.Error.WriteLine($"Autosave failed for node:\n{ex}"); }
        }

        // time — Port of autosave.py:34-38 get_game_time().save() singleton reuse
        if (settings.TimeSystemEnabled)
        {
            try
            {
                var gt = gameTime ?? _cachedTime ?? GlobalServices.GetGameTime();
                // A3-G-2: explicit-settings DB (see map section above).
                using var dbTime = AtherizDbContextFactory.CreateForSettings(settings);
                gt.Save(dbTime);
            }
            catch (Exception ex)
            {
                failures.Add("time");
                try { AtherizLogger.LogError($"Autosave failed for time:\n{ex}"); } catch { Console.Error.WriteLine($"Autosave failed for time:\n{ex}"); }
            }
        }

        if (failures.Count > 0)
        {
            try { AtherizLogger.LogError($"Autosave failed for: {string.Join(", ", failures)}"); } catch { Console.Error.WriteLine($"Autosave failed for: {string.Join(", ", failures)}"); }
            try { var ch = GlobalServices.GetServerChannel(); if (ch != null) ch.Msg($"Autosave failed for: {string.Join(", ", failures)}"); } catch (Exception) { }
        }
        else
        {
            CheckpointJournal.MarkClean(AtherizDbContextFactory.ResolveSavePath(settings));
            try { AtherizLogger.LogInformation("Autosave completed."); } catch { Console.Error.WriteLine("Autosave completed."); }
            try { var ch = GlobalServices.GetServerChannel(); if (ch != null) ch.Msg("Autosave completed."); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Mirrors <c>start_autosave</c>: registers AutosaveTick with AsyncTicker at AUTOSAVE_MINUTES interval.
    /// </summary>
    public static void StartAutosave(AsyncTicker ticker, AtherizSettings? settings = null,
        MapHandler? mapHandler = null, NodeHandler? nodeHandler = null, GameTime? gameTime = null)
    {
        lock (_lock)
        {
            settings ??= AtherizSettings.Global;
            // Port of autosave.py `if not settings.AUTOSAVE_MINUTES`: falsy
            // covers 0 AND negatives — a negative interval must not register
            // a coro with a negative slot key.
            if (settings.AutosaveMinutes <= 0 || _autosaveStarted) return;
            double interval = IntervalSeconds(settings);
            ticker.AddCoro(AutosaveTick, interval);
            _registeredInterval = interval;
            _autosaveStarted = true;
            _startedTicker = ticker;
            _cachedSettings = settings;
            _cachedMap = mapHandler;
            _cachedNodes = nodeHandler;
            _cachedTime = gameTime;
        }
        try { AtherizLogger.LogInformation($"Autosave enabled: every {(settings ?? AtherizSettings.Global).AutosaveMinutes} minutes."); } catch { Console.Error.WriteLine($"Autosave enabled: every {(settings ?? AtherizSettings.Global).AutosaveMinutes} minutes."); }
    }

    /// <summary>
    /// Parameterless global ticker overload (creates ticker if needed) — convenience.
    /// Keeps the ticker handle so it can be stopped; repeated starts reuse
    /// the existing ticker instead of accumulating.
    /// </summary>
    public static void StartAutosave(AtherizSettings? settings = null)
    {
        AsyncTicker? ticker;
        lock (_lock)
        {
            if (_autosaveStarted && _globalTicker != null) return;
            if (_autosaveStarted) return;
            if (_globalTicker == null)
                _globalTicker = new AsyncTicker();
            ticker = _globalTicker;
        }
        StartAutosave(ticker!, settings);
    }

    public static void StopAutosave()
    {
        AsyncTicker? ticker;
        lock (_lock) { ticker = _globalTicker ?? _startedTicker; }
        if (ticker != null)
        {
            try { StopAutosave(ticker); } catch (Exception) { }
        }
    }

    public static void StopAutosave(AsyncTicker ticker)
    {
        lock (_lock)
        {
            if (!_autosaveStarted) return;
            double? interval = _registeredInterval;
            if (interval == null)
            {
                try { AtherizLogger.LogWarning("Autosave was started but no registered interval is known; the tick cannot be removed."); } catch { Console.Error.WriteLine("Autosave was started but no registered interval is known; the tick cannot be removed."); }
                try
                {
                    var fallback = _cachedSettings != null ? IntervalSeconds(_cachedSettings) : 0;
                    if (fallback != 0) ticker.RemoveCoro(AutosaveTick, fallback);
                }
                catch (Exception) { }
                try
                {
                    // scan ticker slots for orphaned coro
                    foreach (var kv in ticker.Slots.ToList())
                    {
                        var slot = kv.Value;
                        // TimeSlot.RemoveCoro requires interval key; we try both fallback and slot interval
                        try { slot.RemoveCoro((Action)AutosaveTick); } catch (Exception) { }
                    }
                }
                catch (Exception) { }
                _registeredInterval = null;
            }
            else
            {
                ticker.RemoveCoro(AutosaveTick, interval.Value);
                _registeredInterval = null;
            }
            _autosaveStarted = false;
            _startedTicker = null;
            // keep cached handlers until next start
        }
    }

    /// <summary>For tests: reset static state.</summary>
    public static void Reset()
    {
        AsyncTicker? gt = null;
        lock (_lock)
        {
            _autosaveStarted = false;
            _registeredInterval = null;
            _cachedSettings = null;
            _cachedMap = null;
            _cachedNodes = null;
            _cachedTime = null;
            _startedTicker = null;
            gt = _globalTicker;
            _globalTicker = null;
        }
        if (gt != null)
        {
            try { gt.Clear(); } catch (Exception) { }
            try { gt.Stop(); } catch (Exception) { }
        }
    }
}
