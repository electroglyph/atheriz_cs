using Atheriz.Core.Concurrency;
using Atheriz.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace Atheriz.Core.Globals;

/// <summary>
/// Keeps _autosave_started flag, start_autosave using AsyncTicker and save_objects etc.
/// Persistence via EF Core JSON (replaces dill handling) — delegates to ObjectRegistry/NodeHandler/MapHandler/GameTime.
/// </summary>
public static class Autosave
{
    private static readonly Lock _lock = new();
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
        settings ??= Volatile.Read(ref _cachedSettings) ?? AtherizSettings.Global;

        // Snapshot the caches without resolving: handler construction can
        // throw (no database yet) and must land in the section report, not
        // escape the tick. The writer resolves nulls inside each guarded
        // section, matching the old per-section tolerance.
        MapHandler? cachedMap;
        NodeHandler? cachedNodes;
        GameTime? cachedTime;
        lock (_lock)
        {
            cachedMap = mapHandler ?? _cachedMap;
            cachedNodes = nodeHandler ?? _cachedNodes;
            cachedTime = gameTime ?? _cachedTime;
        }

        // One atomic checkpoint for all sections (see CheckpointWriter): the
        // journal, transaction, and hold-time logging live there; only the
        // user-facing report stays here. Message shapes are unchanged.
        var failures = CheckpointWriter.WriteCheckpoint(
            settings, "autosave",
            map: cachedMap,
            nodes: cachedNodes,
            time: cachedTime,
            includeTime: settings.TimeSystemEnabled);

        if (failures.Count > 0)
        {
            var names = string.Join(", ", failures.Select(f => f.Name));
            AtherizLogger.LogErrorRobust($"Autosave failed for: {names}");
            try { var ch = GlobalServices.GetServerChannel(); if (ch is not null) ch.Msg($"Autosave failed for: {names}"); } catch (Exception) { }
        }
        else
        {
            AtherizLogger.LogInformationRobust("Autosave completed.");
            try { var ch = GlobalServices.GetServerChannel(); if (ch is not null) ch.Msg("Autosave completed."); } catch (Exception) { }
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
            if (_autosaveStarted && _globalTicker is not null) return;
            if (_autosaveStarted) return;
            if (_globalTicker is null)
                _globalTicker = new AsyncTicker();
            ticker = _globalTicker;
        }
        StartAutosave(ticker!, settings);
    }

    public static void StopAutosave()
    {
        AsyncTicker? ticker;
        lock (_lock) { ticker = _globalTicker ?? _startedTicker; }
        if (ticker is not null)
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
            if (interval is null)
            {
                try { AtherizLogger.LogWarning("Autosave was started but no registered interval is known; the tick cannot be removed."); } catch { Console.Error.WriteLine("Autosave was started but no registered interval is known; the tick cannot be removed."); }
                try
                {
                    var fallback = _cachedSettings is not null ? IntervalSeconds(_cachedSettings) : 0;
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
        AsyncTicker? started = null;
        double? interval = null;
        lock (_lock)
        {
            _autosaveStarted = false;
            interval = _registeredInterval;
            _registeredInterval = null;
            _cachedSettings = null;
            _cachedMap = null;
            _cachedNodes = null;
            _cachedTime = null;
            started = _startedTicker;
            _startedTicker = null;
            gt = _globalTicker;
            _globalTicker = null;
        }
        if (started is not null && interval is not null)
        {
            try { started.RemoveCoro(AutosaveTick, interval.Value); } catch (Exception) { }
        }
        if (gt is not null)
        {
            try { gt.Clear(); } catch (Exception) { }
            try { gt.Stop(); } catch (Exception) { }
        }
    }
}
