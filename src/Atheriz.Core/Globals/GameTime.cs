using System.Text.Json;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Globals;

/// <summary>
/// Faithful port of <c>atheriz/globals/time.py:GameTime</c>.
/// Keeps public fields, locks, IsDirty/Save/Load semantics, tick logic, SunUp,
/// alarms with ? wildcard. Persistence via EF Core JSON in gametime id 0 (GameTimeRow).
/// Replaces dill blobs with JSON.
/// </summary>
public class GameTime
{
    // Audit P2-10: GameTime previously SupportsRecursion exposed as public Lock; now hidden behind private _lock with SupportsRecursion for re-entrant test paths (Ticks setter inside Lock)
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    public ReaderWriterLockSlim SyncRoot => _lock;
    // Compat: keep public Lock for Ported tests (now delegates to private _lock); new code should use SyncRoot/ReadScope/WriteScope
    public ReaderWriterLockSlim Lock => _lock;
    public IDisposable ReadScope() { _lock.EnterReadLock(); return new LockScope(_lock, false); }
    public IDisposable WriteScope() { _lock.EnterWriteLock(); return new LockScope(_lock, true); }
    private sealed class LockScope : IDisposable
    {
        private readonly ReaderWriterLockSlim _rw;
        private readonly bool _isWrite;
        public LockScope(ReaderWriterLockSlim rw, bool isWrite) { _rw = rw; _isWrite = isWrite; }
        public void Dispose() { if (_isWrite) _rw.ExitWriteLock(); else _rw.ExitReadLock(); }
    }
    private long _ticks;
    private readonly Dictionary<(string Hour, string Minute), List<AlarmEntry>> _alarms = new();
    public bool Started { get; private set; }

    [Obsolete("Use global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds()")]
    private static double MonotonicSeconds() => global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
    private readonly AtherizSettings _settings;
    private readonly AsyncTicker? _tickerOverride;
    private readonly AsyncThreadPool? _poolOverride;

    // injected settings/ticker/pool for testability; mirrors Python global getters
    public GameTime() : this(null, null, null, true) { }
    public GameTime(AtherizSettings? settings, bool autoLoad = true) : this(settings, null, null, autoLoad) { }
    public GameTime(AtherizSettings? settings, AsyncTicker? ticker, AsyncThreadPool? pool, bool autoLoad = true)
    {
        _settings = settings ?? AtherizSettings.Default;
        _tickerOverride = ticker;
        _poolOverride = pool;
        if (autoLoad) Load();
    }

    public long Ticks
    {
        get { using (ReadScope()) return _ticks; }
        set { using (WriteScope()) _ticks = value; }
    }

    // ----- persistence -----

    public virtual void Save() => Save(AtherizDbContextFactory.Create());
    public virtual void Save(AtherizDbContext db)
    {
        Dictionary<(string, string), List<AlarmEntry>> alarmsSnap;
        long ticksSnap;
        using (ReadScope())
        {
            ticksSnap = _ticks;
            alarmsSnap = _alarms.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        }

        var dto = new GameTimePersistDto { Ticks = ticksSnap };
        foreach (var kv in alarmsSnap)
        {
            var key = $"{kv.Key.Item1}|{kv.Key.Item2}";
            dto.Alarms[key] = kv.Value.Select(e => new AlarmDto
            {
                CallerId = e.CallerId,
                Repeat = e.Repeat,
                Data = e.Data == null ? null : new Dictionary<string, JsonElement>(e.Data)
            }).ToList();
        }
        var json = JsonSerializer.Serialize(dto, JsonOptions.Default);

        DbTransactionHelper.WithGateAndTransaction(db, ctx =>
        {
            DbTransactionHelper.UpsertJson(ctx.GameTime, () => ctx.GameTime.Find(0), () => new GameTimeRow { Id = 0 }, json);
        });
    }

    public void Load() => Load(AtherizDbContextFactory.Create());
    public void Load(AtherizDbContext db)
    {
        try
        {
            db.Database.EnsureCreated();
            var row = db.GameTime.AsNoTracking().FirstOrDefault(r => r.Id == 0);
            if (row == null)
            {
                if (TryLoadLegacyFile()) return;
                _lock.EnterWriteLock();
                try { _ticks = 0; _alarms.Clear(); }
                finally { _lock.ExitWriteLock(); }
                return;
            }
            GameTimePersistDto? dto;
            try { dto = JsonSerializer.Deserialize<GameTimePersistDto>(row.Data, JsonOptions.Default); }
            catch
            {
                _lock.EnterWriteLock();
                try { _ticks = 0; _alarms.Clear(); }
                finally { _lock.ExitWriteLock(); }
                return;
            }
            if (dto == null)
            {
                _lock.EnterWriteLock();
                try { _ticks = 0; _alarms.Clear(); }
                finally { _lock.ExitWriteLock(); }
                return;
            }
            _lock.EnterWriteLock();
            try
            {
                _ticks = dto.Ticks;
                _alarms.Clear();
                foreach (var kv in dto.Alarms)
                {
                    var parts = kv.Key.Split('|');
                    if (parts.Length != 2) continue;
                    var key = (parts[0], parts[1]);
                    var list = new List<AlarmEntry>();
                    foreach (var a in kv.Value)
                    {
                        // validate data is dict or null — already typed
                        list.Add(new AlarmEntry { CallerId = a.CallerId, Repeat = a.Repeat, Data = a.Data });
                    }
                    if (list.Count > 0) _alarms[key] = list;
                }
            }
            finally { _lock.ExitWriteLock(); }
        }
        catch
        {
            _lock.EnterWriteLock();
            try { _ticks = 0; _alarms.Clear(); }
            finally { _lock.ExitWriteLock(); }
        }
    }

    private bool TryLoadLegacyFile()
    {
        try
        {
            var path = Path.Combine(_settings.SavePath, "time");
            if (!File.Exists(path)) return false;
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            long ticks = 0;
            if (root.TryGetProperty("ticks", out var tp) && tp.ValueKind == JsonValueKind.Number) ticks = tp.GetInt64();
            var alarms = new Dictionary<(string, string), List<AlarmEntry>>();
            if (root.TryGetProperty("alarms", out var ap) && ap.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in ap.EnumerateObject())
                {
                    // key is Python tuple repr e.g. "('1', '2')" — attempt to parse via simple handling
                    string keyStr = prop.Name;
                    // try to parse as ("hour","minute") with quotes
                    string hour = "?", minute = "?";
                    try
                    {
                        // fallback: if key contains comma, strip parens and quotes
                        var cleaned = keyStr.Trim('(', ')', ' ');
                        var parts = cleaned.Split(',');
                        if (parts.Length == 2)
                        {
                            hour = parts[0].Trim().Trim('\'', '"', ' ');
                            minute = parts[1].Trim().Trim('\'', '"', ' ');
                        }
                        else continue;
                    }
                    catch { continue; }
                    if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                    var list = new List<AlarmEntry>();
                    foreach (var elem in prop.Value.EnumerateArray())
                    {
                        if (elem.ValueKind != JsonValueKind.Array) continue;
                        var arr = elem.EnumerateArray().ToArray();
                        if (arr.Length < 3) continue;
                        int id = arr[0].GetInt32();
                        bool repeat = arr[1].ValueKind == JsonValueKind.True;
                        Dictionary<string, JsonElement>? data = null;
                        if (arr[2].ValueKind == JsonValueKind.Object)
                        {
                            data = new Dictionary<string, JsonElement>();
                            foreach (var dprop in arr[2].EnumerateObject()) data[dprop.Name] = dprop.Value.Clone();
                        }
                        else if (arr[2].ValueKind != JsonValueKind.Null) continue; // skip non-dict
                        list.Add(new AlarmEntry { CallerId = id, Repeat = repeat, Data = data });
                    }
                    if (list.Count > 0) alarms[(hour, minute)] = list;
                }
            }
            _lock.EnterWriteLock();
            try { _ticks = ticks; _alarms.Clear(); foreach (var kv in alarms) _alarms[kv.Key] = kv.Value; }
            finally { _lock.ExitWriteLock(); }
            try { Save(); } catch { return false; }
            try { File.Delete(path); } catch { }
            return true;
        }
        catch { return false; }
    }

    // ----- alarm ops -----

    public sealed class AlarmEntry
    {
        public int CallerId { get; set; }
        public bool Repeat { get; set; }
        public Dictionary<string, JsonElement>? Data { get; set; }
    }

    private sealed class AlarmDto
    {
        public int CallerId { get; set; }
        public bool Repeat { get; set; }
        public Dictionary<string, JsonElement>? Data { get; set; }
    }

    private sealed class GameTimePersistDto
    {
        public long Ticks { get; set; }
        public Dictionary<string, List<AlarmDto>> Alarms { get; set; } = new();
    }

    public void AddAlarm(string hour, string minute, GameObject caller, bool repeat = false, Dictionary<string, JsonElement>? data = null)
    {
        if (caller == null) return;
        AddAlarm(hour, minute, caller.Id, repeat, data);
    }

    // Overload for validation test: data as object must throw if not dict/null
    public void AddAlarm(string hour, string minute, GameObject caller, bool repeat, object? data)
    {
        if (caller == null) return;
        if (data != null && data is not Dictionary<string, JsonElement> && data is not Dictionary<string, object>)
            throw new ArgumentException($"alarm data must be a dict or None, got {data.GetType().Name}");
        Dictionary<string, JsonElement>? dict = null;
        if (data is Dictionary<string, JsonElement> d) dict = d;
        AddAlarm(hour, minute, caller.Id, repeat, dict);
    }

    public void AddAlarm(string hour, string minute, int callerId, bool repeat = false, Dictionary<string, JsonElement>? data = null)
    {
        if (hour == null) throw new ArgumentNullException(nameof(hour));
        if (minute == null) throw new ArgumentNullException(nameof(minute));
        hour = hour.ToString();
        minute = minute.ToString();
        // validate data is dict or null — typed already
        _lock.EnterWriteLock();
        try
        {
            var key = (hour, minute);
            if (!_alarms.TryGetValue(key, out var list))
            {
                list = new List<AlarmEntry>();
                _alarms[key] = list;
            }
            list.Add(new AlarmEntry { CallerId = callerId, Repeat = repeat, Data = data });
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void AddAlarm(int hour, int minute, GameObject caller, bool repeat = false, Dictionary<string, JsonElement>? data = null)
        => AddAlarm(hour.ToString(), minute.ToString(), caller, repeat, data);

    public void RemoveAlarmsByCaller(int callerId)
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var list in _alarms.Values)
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i].CallerId == callerId) list.RemoveAt(i);
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void RemoveAlarmsByCaller(GameObject caller) => RemoveAlarmsByCaller(caller.Id);

    public void RemoveAlarm(string hour, string minute, int callerId)
    {
        hour = hour.ToString(); minute = minute.ToString();
        _lock.EnterWriteLock();
        try
        {
            if (_alarms.TryGetValue((hour, minute), out var list))
            {
                int idx = -1;
                for (int i = 0; i < list.Count; i++) if (list[i].CallerId == callerId) { idx = i; break; }
                if (idx >= 0) list.RemoveAt(idx);
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void RemoveAlarm(string hour, string minute, GameObject caller)
    {
        if (caller == null) return;
        RemoveAlarm(hour, minute, caller.Id);
    }

    public IReadOnlyDictionary<(string Hour, string Minute), List<AlarmEntry>> SnapshotAlarms()
    {
        using (ReadScope()) return _alarms.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
    }

    // ----- ticker -----

    public void Start()
    {
        if (Started) return;
        var ticker = _tickerOverride ?? GlobalServices.TryGetTicker() ?? new AsyncTicker(_poolOverride ?? new AsyncThreadPool());
        ticker.AddCoro(OnTick, _settings.TimeUpdateSeconds);
        Started = true;
    }
    public void Start(AsyncTicker ticker)
    {
        if (Started) return;
        ticker.AddCoro(OnTick, _settings.TimeUpdateSeconds);
        Started = true;
    }

    public void Stop()
    {
        if (_tickerOverride != null) { Stop(_tickerOverride); return; }
        var ticker = GlobalServices.TryGetTicker();
        if (ticker != null) ticker.RemoveCoro(OnTick, _settings.TimeUpdateSeconds);
        try { Save(); } catch { }
        Started = false;
    }
    public void Stop(AsyncTicker ticker)
    {
        ticker.RemoveCoro(OnTick, _settings.TimeUpdateSeconds);
        try { Save(); } catch { }
        Started = false;
    }

    public bool SunUp()
    {
        var t = GetTime();
        return t.Hour >= _settings.SunriseHour && t.Hour < _settings.SunsetHour;
    }

    public bool SunUpAlt(int hour) => hour >= _settings.SunriseHour && hour < _settings.SunsetHour;

    public void OnTick()
    {
        var before = GetTime();
        bool beforeSun = SunUpAlt(before.Hour);
        string beforePhase = before.MoonPhase;

        _lock.EnterWriteLock();
        try { _ticks++; }
        finally { _lock.ExitWriteLock(); }

        var after = GetTime();
        var callers = new List<((string Hour, string Minute) Key, int CallerId, bool Repeat, Dictionary<string, JsonElement>? Data)>();
        _lock.EnterReadLock();
        try
        {
            void Collect(string h, string m)
            {
                if (_alarms.TryGetValue((h, m), out var list))
                    foreach (var a in list) callers.Add(((h, m), a.CallerId, a.Repeat, a.Data));
            }
            Collect(after.Hour.ToString(), after.Minute.ToString());
            Collect("?", after.Minute.ToString());
            Collect(after.Hour.ToString(), "?");
            Collect("?", "?");
        }
        finally { _lock.ExitReadLock(); }

        if (callers.Count > 0)
        {
            var pool = _poolOverride ?? GlobalServices.TryGetPool() ?? new AsyncThreadPool();
            foreach (var (key, id, repeat, data) in callers)
            {
                if (!repeat) RemoveAlarm(key.Hour, key.Minute, id);
                var objs = ObjectRegistry.Get(id);
                if (objs.Count > 0)
                {
                    var target = objs[0];
                    Action? act = null;
                    var mi = target.GetType().GetMethod("AtAlarm");
                    if (mi != null)
                    {
                        var capturedData = data;
                        var capturedAfter = after;
                        act = () => { try { mi.Invoke(target, new object?[] { capturedAfter, capturedData }); } catch { } };
                    }
                    else
                    {
                        continue;
                    }
                    if (act != null)
                    {
                        if (!pool.AddTask(act, $"alarm:{id}"))
                            pool.Delay(0.05, act);
                    }
                }
            }
        }

        string afterPhase = after.MoonPhase;
        bool afterSun = SunUpAlt(after.Hour);
        if (beforePhase != afterPhase)
        {
            var recv = _settings.LunarReceiverLambda ?? (o => o.IsPc && o.IsConnected);
            foreach (var obj in ObjectRegistry.FilterBy(o => { try { return recv(o); } catch { return o.IsPc && o.IsConnected; } }))
            {
                var mi = obj.GetType().GetMethod("AtLunarEvent");
                if (mi != null) try { mi.Invoke(obj, new object?[] { $"A {afterPhase.ToLower()} moon rises." }); } catch { }
            }
        }
        if (beforeSun != afterSun)
        {
            string msg = afterSun ? _settings.SunriseMessage : _settings.SunsetMessage;
            var recv = _settings.SolarReceiverLambda ?? (o => o.IsPc && o.IsConnected);
            foreach (var obj in ObjectRegistry.FilterBy(o => { try { return recv(o); } catch { return o.IsPc && o.IsConnected; } }))
            {
                var mi = obj.GetType().GetMethod("AtSolarEvent");
                if (mi != null) try { mi.Invoke(obj, new object?[] { msg }); } catch { }
            }
        }
    }

    // ----- time computations -----

    public sealed class GameTimeInfo
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
        public int Second { get; set; }
        public string MoonPhase { get; set; } = "";
        public string Formatted { get; set; } = "";
        public string FormattedShort { get; set; } = "";
        public string Season { get; set; } = "";
        public int WeekOfSeason { get; set; }
        public long Ticks { get; set; }
    }

    public sealed class TimeSpanInfo
    {
        public int Years { get; set; }
        public int Months { get; set; }
        public int Weeks { get; set; }
        public int Days { get; set; }
        public int Hours { get; set; }
        public double Minutes { get; set; }
        public string Desc { get; set; } = "";
    }

    public GameTimeInfo GetTime()
    {
        long current;
        _lock.EnterReadLock();
        try { current = _ticks; }
        finally { _lock.ExitReadLock(); }

        double tickDurationSeconds = _settings.TickMinutes * _settings.SecondsPerMinute;
        double totalSeconds = current * tickDurationSeconds;
        long totalDays = (long)(totalSeconds / _settings.SecondsPerDay);

        double remainingSecondsInDay = totalSeconds % _settings.SecondsPerDay;
        if (remainingSecondsInDay < 0) remainingSecondsInDay += _settings.SecondsPerDay;
        int calcHour = (int)(remainingSecondsInDay / _settings.SecondsPerHour);
        double remainingInHour = remainingSecondsInDay % _settings.SecondsPerHour;
        int calcMinute = (int)(remainingInHour / _settings.SecondsPerMinute);
        int calcSecond = (int)(remainingInHour % _settings.SecondsPerMinute);

        long yearOffset = totalDays / _settings.DaysPerYear;
        long dayOfYear = totalDays % _settings.DaysPerYear;
        if (dayOfYear < 0) { dayOfYear += _settings.DaysPerYear; yearOffset--; }
        long calcMonth = dayOfYear / _settings.DaysPerMonth;
        long calcDay = dayOfYear % _settings.DaysPerMonth;
        long dayInLunar = totalDays % _settings.LunarCycleDays;
        if (dayInLunar < 0) dayInLunar += _settings.LunarCycleDays;
        string moonPhase;
        if (dayInLunar == 0) moonPhase = "new";
        else if (1 <= dayInLunar && dayInLunar <= 6) moonPhase = "waxing crescent";
        else if (dayInLunar == 7) moonPhase = "first quarter";
        else if (8 <= dayInLunar && dayInLunar <= 14) moonPhase = "waxing gibbous";
        else if (dayInLunar == 15) moonPhase = "full";
        else if (16 <= dayInLunar && dayInLunar <= 21) moonPhase = "waning gibbous";
        else if (dayInLunar == 22) moonPhase = "third quarter";
        else moonPhase = "waning crescent";

        int finalYear = _settings.StartYear + (int)yearOffset;
        int finalMonth = (int)calcMonth + 1;
        int finalDay = (int)calcDay + 1;

        string season;
        long dayInSeason = 0;
        if (3 <= finalMonth && finalMonth <= 5)
        {
            season = "spring";
            long start = (3 - 1) * _settings.DaysPerMonth;
            dayInSeason = dayOfYear - start;
        }
        else if (6 <= finalMonth && finalMonth <= 8)
        {
            season = "summer";
            long start = (6 - 1) * _settings.DaysPerMonth;
            dayInSeason = dayOfYear - start;
        }
        else if (9 <= finalMonth && finalMonth <= 11)
        {
            season = "autumn";
            long start = (9 - 1) * _settings.DaysPerMonth;
            dayInSeason = dayOfYear - start;
        }
        else
        {
            season = "winter";
            long winterStart = (12 - 1) * _settings.DaysPerMonth;
            if (finalMonth == 12) dayInSeason = dayOfYear - winterStart;
            else
            {
                long daysInWinterLastYear = _settings.DaysPerYear - winterStart;
                dayInSeason = daysInWinterLastYear + dayOfYear;
            }
        }

        int weekOfSeason = (int)(dayInSeason / _settings.DaysPerWeek) + 1;

        string ordinal(string dayStr, int d)
        {
            string suffix = "th";
            if (d < 11 || d > 13)
            {
                int last = d % 10;
                if (last == 1) suffix = "st";
                else if (last == 2) suffix = "nd";
                else if (last == 3) suffix = "rd";
            }
            return $"{d}{suffix}";
        }

        string formattedTime = $"{calcHour:00}:{calcMinute:00}:{calcSecond:00}";
        string monthName = Enum.IsDefined(typeof(Month), finalMonth) ? ((Month)finalMonth).ToString() : finalMonth.ToString();
        // Month enum mirrors settings.Month — use ToString for name
        string formattedDateTime = $"{formattedTime}, {ordinal("", finalDay)} of {monthName}, year {finalYear}\nWeek {weekOfSeason} of {season}\nMoon phase: {moonPhase}";
        string formattedShort = $"{formattedTime}, {ordinal("", finalDay)} of {monthName}, year {finalYear}";

        return new GameTimeInfo
        {
            Year = finalYear,
            Month = finalMonth,
            Day = finalDay,
            Hour = calcHour,
            Minute = calcMinute,
            Second = calcSecond,
            MoonPhase = moonPhase,
            Formatted = formattedDateTime,
            FormattedShort = formattedShort,
            Season = season,
            WeekOfSeason = weekOfSeason,
            Ticks = current,
        };
    }

    // Python Month enum duplicated for naming
    private enum Month { Ianuarius = 1, Februarius = 2, Martius = 3, Aprilis = 4, Maius = 5, Iunius = 6, Iulius = 7, Augustus = 8, September = 9, October = 10, November = 11, December = 12 }

    public TimeSpanInfo GetTimespan(long ticks)
    {
        if (ticks == 0)
            return new TimeSpanInfo { Years = 0, Months = 0, Weeks = 0, Days = 0, Hours = 0, Minutes = 0, Desc = "now" };

        string lastWord = "ago";
        if (ticks < 0) { lastWord = "in the future"; ticks = -ticks; }

        double leftover = ticks;
        double tickMinutes = _settings.TickMinutes;
        double tph = _settings.MinutesPerHour / tickMinutes;
        double tpd = tph * _settings.HoursPerDay;
        double tpw = tpd * _settings.DaysPerWeek;
        double tpmo = tpd * _settings.DaysPerMonth;
        double tpy = tpmo * _settings.MonthsPerYear;

        string formatted = "";
        int y = 0, m = 0, w = 0, d = 0, h = 0;

        if (leftover >= tpy)
        {
            y = (int)(leftover / tpy);
            formatted = y > 1 ? $"{y} years" : "1 year";
            leftover %= y * tpy;
        }
        if (leftover >= tpmo)
        {
            if (formatted != "") formatted += ", ";
            m = (int)(leftover / tpmo);
            formatted += m > 1 ? $"{m} months" : "1 month";
            leftover %= m * tpmo;
        }
        if (leftover >= tpw)
        {
            if (formatted != "") formatted += ", ";
            w = (int)(leftover / tpw);
            formatted += w > 1 ? $"{w} weeks" : "1 week";
            leftover %= w * tpw;
        }
        if (leftover >= tpd)
        {
            if (formatted != "") formatted += ", ";
            d = (int)(leftover / tpd);
            formatted += d > 1 ? $"{d} days" : "1 day";
            leftover %= d * tpd;
        }
        if (leftover >= tph)
        {
            if (formatted != "") formatted += ", ";
            h = (int)(leftover / tph);
            formatted += h > 1 ? $"{h} hours" : "1 hour";
            leftover %= h * tph;
        }
        if (leftover > 0)
        {
            if (formatted != "") formatted += ", ";
            double leftoverMinutes = leftover * tickMinutes;
            formatted += $"{leftoverMinutes:0} minutes";
        }
        int comma = formatted.LastIndexOf(',');
        string desc;
        if (comma > 0) desc = $"{formatted.Substring(0, comma)} and{formatted.Substring(comma + 1)} {lastWord}";
        else desc = $"{formatted} {lastWord}";

        return new TimeSpanInfo
        {
            Years = y, Months = m, Weeks = w, Days = d, Hours = h,
            Minutes = leftover * tickMinutes,
            Desc = desc
        };
    }

    public Dictionary<string, object> GetTimeDict()
    {
        var t = GetTime();
        return new Dictionary<string, object>
        {
            ["year"] = t.Year, ["month"] = t.Month, ["day"] = t.Day,
            ["hour"] = t.Hour, ["minute"] = t.Minute, ["second"] = t.Second,
            ["moon_phase"] = t.MoonPhase, ["formatted"] = t.Formatted,
            ["formatted_short"] = t.FormattedShort, ["season"] = t.Season,
            ["week_of_season"] = t.WeekOfSeason, ["ticks"] = t.Ticks,
        };
    }
}
