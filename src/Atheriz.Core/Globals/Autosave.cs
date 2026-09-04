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
        settings ??= _cachedSettings ?? AtherizSettings.Default;
        var failures = new List<string>();

        // objects
        try
        {
            using var db = AtherizDbContextFactory.Create();
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
            var mh = mapHandler ?? _cachedMap ?? GlobalServices.GetMapHandler();
            mh.Save();
        }
        catch (Exception ex)
        {
            failures.Add("map");
            try { AtherizLogger.LogError($"Autosave failed for map:\n{ex}"); } catch { Console.Error.WriteLine($"Autosave failed for map:\n{ex}"); }
        }

        // node — Port of autosave.py:27 get_node_handler().save() singleton reuse
        try
        {
            var nh = nodeHandler ?? _cachedNodes ?? GlobalServices.GetNodeHandler();
            nh.Save();
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
                gt.Save();
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
            try { var ch = GlobalServices.GetServerChannel(); if (ch != null) ch.Msg($"Autosave failed for: {string.Join(", ", failures)}"); } catch { }
        }
        else
        {
            try { AtherizLogger.LogInformation("Autosave completed."); } catch { Console.Error.WriteLine("Autosave completed."); }
            try { var ch = GlobalServices.GetServerChannel(); if (ch != null) ch.Msg("Autosave completed."); } catch { }
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
            settings ??= AtherizSettings.Default;
            if (settings.AutosaveMinutes == 0 || _autosaveStarted) return;
            double interval = IntervalSeconds(settings);
            ticker.AddCoro(AutosaveTick, interval);
            _registeredInterval = interval;
            _autosaveStarted = true;
            _cachedSettings = settings;
            _cachedMap = mapHandler;
            _cachedNodes = nodeHandler;
            _cachedTime = gameTime;
        }
        try { AtherizLogger.LogInformation($"Autosave enabled: every {(settings ?? AtherizSettings.Default).AutosaveMinutes} minutes."); } catch { Console.Error.WriteLine($"Autosave enabled: every {(settings ?? AtherizSettings.Default).AutosaveMinutes} minutes."); }
    }

    /// <summary>
    /// Parameterless global ticker overload (creates ticker if needed) — convenience.
    /// </summary>
    public static void StartAutosave(AtherizSettings? settings = null)
    {
        var ticker = new AsyncTicker();
        StartAutosave(ticker, settings);
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
                catch { }
                try
                {
                    // scan ticker slots for orphaned coro
                    foreach (var kv in ticker.Slots.ToList())
                    {
                        var slot = kv.Value;
                        // TimeSlot.RemoveCoro requires interval key; we try both fallback and slot interval
                        try { slot.RemoveCoro((Action)AutosaveTick); } catch { }
                    }
                }
                catch { }
                _registeredInterval = null;
            }
            else
            {
                ticker.RemoveCoro(AutosaveTick, interval.Value);
                _registeredInterval = null;
            }
            _autosaveStarted = false;
            // keep cached handlers until next start
        }
    }

    /// <summary>For tests: reset static state.</summary>
    public static void ResetForTesting()
    {
        lock (_lock)
        {
            _autosaveStarted = false;
            _registeredInterval = null;
            _cachedSettings = null;
            _cachedMap = null;
            _cachedNodes = null;
            _cachedTime = null;
        }
    }
}
