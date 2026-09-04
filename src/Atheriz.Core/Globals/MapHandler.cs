using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Globals;

// ---------------------------------------------------------------------------
// LegendEntry — mirrors atheriz/globals/map.py:LegendEntry
// ---------------------------------------------------------------------------

/// <summary>
/// Port of <c>atheriz/globals/map.py:LegendEntry</c>.
/// Symbol/desc/coord/show/fg/bg faithful.
/// </summary>
public sealed class TupleCoordConverter : JsonConverter<(int X, int Y)?>
{
    public override (int X, int Y)? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read();
            if (reader.TokenType == JsonTokenType.EndArray) return null;
            int x = reader.GetInt32();
            reader.Read();
            int y = 0;
            if (reader.TokenType != JsonTokenType.EndArray) { y = reader.GetInt32(); reader.Read(); }
            // consume EndArray if not yet
            while (reader.TokenType != JsonTokenType.EndArray && reader.Read()) { }
            return (x, y);
        }
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            int? x=null, y=null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                string prop = reader.GetString()!;
                reader.Read();
                if (prop == "Item1" || prop == "X" || prop == "x") x = reader.GetInt32();
                else if (prop == "Item2" || prop == "Y" || prop == "y") y = reader.GetInt32();
                else reader.Skip();
            }
            if (x.HasValue && y.HasValue) return (x.Value, y.Value);
            return null;
        }
        return null;
    }
    public override void Write(Utf8JsonWriter writer, (int X, int Y)? value, JsonSerializerOptions options)
    {
        if (value == null) { writer.WriteNullValue(); return; }
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Value.X);
        writer.WriteNumberValue(value.Value.Y);
        writer.WriteEndArray();
    }
}

public sealed class LegendEntry : IEquatable<LegendEntry>
{
    public string? Symbol { get; set; }
    public string? Desc { get; set; }
    [JsonConverter(typeof(TupleCoordConverter))]
    public (int X, int Y)? Coord { get; set; }
    public bool Show { get; set; } = true;
    public double Fg { get; set; } = 170.0;
    public double? Bg { get; set; }

    public LegendEntry() { }
    public LegendEntry(string? symbol = null, string? desc = null, (int X, int Y)? coord = null)
    {
        Symbol = symbol;
        Desc = desc;
        Coord = coord;
        Show = true;
        Fg = 170.0;
        Bg = null;
    }

    public bool Equals(LegendEntry? other)
    {
        if (other is null) return false;
        return Symbol == other.Symbol && Desc == other.Desc && Coord == other.Coord
               && Show == other.Show && Fg.Equals(other.Fg) && Bg == other.Bg;
    }
    public override bool Equals(object? obj) => Equals(obj as LegendEntry);
    public override int GetHashCode() => HashCode.Combine(Symbol, Desc, Coord, Show, Fg, Bg);

    public Dictionary<string, object?> ToPayload()
    {
        return new Dictionary<string, object?>
        {
            ["symbol"] = Symbol,
            ["desc"] = Desc,
            ["coord"] = Coord is null ? null : new List<int> { Coord.Value.X, Coord.Value.Y },
            ["show"] = Show,
            ["fg"] = Fg,
            ["bg"] = Bg,
        };
    }

    public static LegendEntry FromPayload(Dictionary<string, JsonElement> data)
    {
        string? symbol = data.TryGetValue("symbol", out var se) && se.ValueKind != JsonValueKind.Null ? se.GetString() : null;
        string? desc = data.TryGetValue("desc", out var de) && de.ValueKind != JsonValueKind.Null ? de.GetString() : null;
        (int, int)? coord = null;
        if (data.TryGetValue("coord", out var ce) && ce.ValueKind == JsonValueKind.Array)
        {
            var arr = ce.EnumerateArray().Select(e => e.GetInt32()).ToArray();
            if (arr.Length >= 2) coord = (arr[0], arr[1]);
        }
        bool show = true;
        if (data.TryGetValue("show", out var sh) && (sh.ValueKind == JsonValueKind.True || sh.ValueKind == JsonValueKind.False))
            show = sh.GetBoolean();
        double fg = 170.0;
        if (data.TryGetValue("fg", out var fe) && fe.ValueKind == JsonValueKind.Number) fg = fe.GetDouble();
        double? bg = null;
        if (data.TryGetValue("bg", out var be) && be.ValueKind == JsonValueKind.Number) bg = be.GetDouble();

        var e = new LegendEntry(symbol, desc, coord);
        e.Show = show;
        e.Fg = fg;
        e.Bg = bg;
        return e;
    }

    public static LegendEntry FromPayload(JsonElement el)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject()) dict[p.Name] = p.Value;
        return FromPayload(dict);
    }
}

// ---------------------------------------------------------------------------
// MapInfo — mirrors atheriz/globals/map.py:MapInfo
// ---------------------------------------------------------------------------

/// <summary>
/// Port of <c>atheriz/globals/map.py:MapInfo</c>.
/// Keeps public fields, locks, IsDirty/MapChanged semantics, BFS stubs.
/// JSON persistence replaces dill.
/// </summary>
public class MapInfo
{
    public string Name { get; set; } = "unknown";
    public bool MapChanged { get; set; } = true;
    public bool IsModified { get; set; } = false; // for parity with Python getattr is_modified
    public Dictionary<(int X, int Y), string> PreGrid { get; } = new();
    public Dictionary<(int X, int Y), string> PostGrid { get; } = new();
    public List<LegendEntry> LegendEntries { get; } = new();
    public Dictionary<int, GameObject> Objects { get; } = new();
    public Dictionary<int, GameObject> Listeners { get; } = new();
    // Audit P2-10: hide public Lock, use NoRecursion (no re-entrant path; snapshots used)
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
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
    private int _batchUpdate = 0;
    private bool _legendSuppressed = false;

    // Settings reference for caps and placeholders
    public AtherizSettings Settings { get; set; } = new();

    public MapInfo() { }
    public MapInfo(string name, Dictionary<(int, int), string>? preGrid = null,
        Dictionary<(int, int), string>? postGrid = null, List<LegendEntry>? legendEntries = null,
        AtherizSettings? settings = null)
    {
        Name = name;
        Settings = settings ?? AtherizSettings.Default;
        if (preGrid != null) foreach (var kv in preGrid) PreGrid[kv.Key] = kv.Value;
        if (postGrid != null) foreach (var kv in postGrid) PostGrid[kv.Key] = kv.Value;
        if (legendEntries != null) LegendEntries.AddRange(legendEntries);
    }

    private bool IsOverLegendCap()
    {
        return (Objects.Count + LegendEntries.Count) > Settings.MaxObjectsPerLegend;
    }

    public bool Equals(MapInfo? other)
    {
        if (other is null) return false;
        if (Name != other.Name) return false;
        if (PreGrid.Count != other.PreGrid.Count || PostGrid.Count != other.PostGrid.Count) return false;
        if (!PreGrid.OrderBy(kv => kv.Key).SequenceEqual(other.PreGrid.OrderBy(kv => kv.Key))) return false;
        if (!PostGrid.OrderBy(kv => kv.Key).SequenceEqual(other.PostGrid.OrderBy(kv => kv.Key))) return false;
        if (LegendEntries.Count != other.LegendEntries.Count) return false;
        for (int i = 0; i < LegendEntries.Count; i++) if (!LegendEntries[i].Equals(other.LegendEntries[i])) return false;
        return true;
    }

    // --- grid helpers ---

    public void PlaceWalls((int X, int Y) coord, string ch)
    {
        using (WriteScope())
        {
            int cx = coord.X, cy = coord.Y;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var key = (cx + dx, cy + dy);
                    if (PreGrid.TryGetValue(key, out var existing) && existing == Settings.RoomPlaceholder)
                        continue;
                    PreGrid[key] = ch;
                }
            MapChanged = true;
            IsModified = true;
        }
    }

    public static (string Rendered, int MinX, int MaxY) RenderGrid(Dictionary<(int X, int Y), string> grid)
    {
        if (grid.Count == 0) return ("", 0, 0);
        int minX = grid.Keys.Min(k => k.X);
        int maxX = grid.Keys.Max(k => k.X);
        int minY = grid.Keys.Min(k => k.Y);
        int maxY = grid.Keys.Max(k => k.Y);
        var lines = new List<string>();
        for (int y = maxY; y >= minY; y--)
        {
            var row = new System.Text.StringBuilder();
            for (int x = minX; x <= maxX; x++)
                row.Append(grid.TryGetValue((x, y), out var v) ? v : " ");
            lines.Add(row.ToString());
        }
        return (string.Join("\n", lines), minX, maxY);
    }

    public static (bool N, bool S, bool E, bool W) GetDirs(Dictionary<(int X, int Y), string> grid, (int X, int Y) coord, List<string> chars)
    {
        bool n = false, s = false, e = false, w = false;
        int cx = coord.X, cy = coord.Y;
        if (grid.TryGetValue((cx, cy + 1), out var v) && chars.Contains(GameUtils.StripAnsi(v))) n = true;
        if (grid.TryGetValue((cx, cy - 1), out v) && chars.Contains(GameUtils.StripAnsi(v))) s = true;
        if (grid.TryGetValue((cx + 1, cy), out v) && chars.Contains(GameUtils.StripAnsi(v))) e = true;
        if (grid.TryGetValue((cx - 1, cy), out v) && chars.Contains(GameUtils.StripAnsi(v))) w = true;
        return (n, s, e, w);
    }

    public static string ResolveChar(bool n, bool s, bool e, bool w, string style)
    {
        if (style == "single")
        {
            if (n && s && e && w) return "┼";
            if (n && s && e) return "├";
            if (n && s && w) return "┤";
            if (n && e && w) return "┴";
            if (s && e && w) return "┬";
            if (n && e) return "└";
            if (n && w) return "┘";
            if (s && e) return "┌";
            if (s && w) return "┐";
            if (n && s) return "│";
            if (e && w) return "─";
            if (n || s) return "│";
            return "─";
        }
        if (style == "double")
        {
            if (n && s && e && w) return "╬";
            if (n && s && e) return "╠";
            if (n && s && w) return "╣";
            if (n && e && w) return "╩";
            if (s && e && w) return "╦";
            if (n && e) return "╚";
            if (n && w) return "╝";
            if (s && e) return "╔";
            if (s && w) return "╗";
            if (n && s) return "║";
            if (e && w) return "═";
            if (n || s) return "║";
            return "═";
        }
        if (style == "rounded")
        {
            if (n && s && e && w) return "┼";
            if (n && s && e) return "├";
            if (n && s && w) return "┤";
            if (n && e && w) return "┴";
            if (s && e && w) return "┬";
            if (n && e) return "╰";
            if (n && w) return "╯";
            if (s && e) return "╭";
            if (s && w) return "╮";
            if (n && s) return "│";
            if (e && w) return "─";
            if (n || s) return "│";
            return "─";
        }
        return "─";
    }

    public void PreRender()
    {
        Lock.EnterWriteLock();
        try
        {
            var placeholderStyles = new Dictionary<string, string>
            {
                [Settings.SingleWallPlaceholder] = "single",
                [Settings.DoubleWallPlaceholder] = "double",
                [Settings.RoundedWallPlaceholder] = "rounded",
                [Settings.PathPlaceholder] = "rounded",
                [Settings.RoadPlaceholder] = "double",
            };
            var allSymbols = new List<string>(Settings.AllSymbols);
            var rendered = new Dictionary<(int, int), string>(PreGrid);
            var original = new Dictionary<(int, int), string>(PreGrid);
            var toPlace = new Dictionary<(int, int), string>();
            foreach (var kv in rendered)
            {
                if (placeholderStyles.TryGetValue(kv.Value, out var style))
                {
                    var (n, s, e, w) = GetDirs(original, kv.Key, allSymbols);
                    toPlace[kv.Key] = ResolveChar(n, s, e, w, style);
                }
                else if (kv.Value == Settings.RoomPlaceholder)
                {
                    toPlace[kv.Key] = " ";
                }
            }
            foreach (var kv in toPlace) rendered[kv.Key] = kv.Value;
            PostGrid.Clear();
            foreach (var kv in rendered) PostGrid[kv.Key] = kv.Value;
        }
        finally { Lock.ExitWriteLock(); }
    }

    public void UpdateGrid((int X, int Y) coord, string newSymbol)
    {
        bool shouldRender;
        Lock.EnterWriteLock();
        try
        {
            PreGrid[coord] = newSymbol;
            MapChanged = true;
            IsModified = true;
            shouldRender = _batchUpdate == 0;
        }
        finally { Lock.ExitWriteLock(); }
        if (shouldRender) Render(true);
    }

    public IDisposable BatchUpdate()
    {
        Lock.EnterWriteLock();
        try
        {
            _batchUpdate++;
            if (PreGrid.Count == 0 && PostGrid.Count > 0)
            {
                foreach (var kv in PostGrid) PreGrid[kv.Key] = kv.Value;
            }
        }
        finally { Lock.ExitWriteLock(); }
        return new BatchScope(this);
    }

    private sealed class BatchScope : IDisposable
    {
        private readonly MapInfo _mi;
        private bool _disposed;
        public BatchScope(MapInfo mi) => _mi = mi;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            bool shouldRender;
            _mi.Lock.EnterWriteLock();
            try
            {
                _mi._batchUpdate--;
                shouldRender = _mi._batchUpdate == 0 && _mi.MapChanged;
            }
            finally { _mi.Lock.ExitWriteLock(); }
            if (shouldRender) _mi.Render(true);
        }
    }

    // --- map dispatch helpers (faithful to Python getattr duck-typing) ---
    private static bool TryGetLocationCoord(GameObject obj, out (int x, int y) coord)
    {
        coord = default;
        try
        {
            var loc = obj.Location;
            if (loc == null) return false;
            if (loc is Persistence.Dto.LocationRef.NullLocation) return false;
            if (loc is Persistence.Dto.LocationRef.CoordLocation cl)
            {
                coord = (cl.Coord.X, cl.Coord.Y);
                return true;
            }
            if (loc is Persistence.Dto.LocationRef.ObjectLocation ol)
            {
                var objs = Globals.ObjectRegistry.Get(ol.ObjectId);
                if (objs.Count > 0)
                {
                    var target = objs[0];
                    if (target is Node n) { coord = (n.Coord.X, n.Coord.Y); return true; }
                    var tloc = target.Location;
                    if (tloc is Persistence.Dto.LocationRef.CoordLocation tcl) { coord = (tcl.Coord.X, tcl.Coord.Y); return true; }
                }
                return false;
            }
        }
        catch { }
        return false;
    }

    private static bool GetMapEnabled(object listener)
    {
        try
        {
            var type = listener.GetType();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (f.Name.Equals("MapEnabled", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("map_enabled", StringComparison.OrdinalIgnoreCase))
                {
                    var v = f.GetValue(listener);
                    if (v is bool b) return b;
                }
            }
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (p.Name.Equals("MapEnabled", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("map_enabled", StringComparison.OrdinalIgnoreCase))
                {
                    var v = p.GetValue(listener);
                    if (v is bool b) return b;
                }
            }
            if (listener is GameObject go) return go.MapEnabled;
        }
        catch { }
        return true;
    }

    private static double? GetLastMapTime(object listener)
    {
        try
        {
            var type = listener.GetType();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (f.Name.Equals("LastMapTime", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("last_map_time", StringComparison.OrdinalIgnoreCase))
                {
                    var v = f.GetValue(listener);
                    if (v == null) return null;
                    if (v is double d) return d;
                    if (v is float fl) return (double)fl;
                    if (v is int ii) return (double)ii;
                    if (v is long ll) return (double)ll;
                    if (v is decimal dc) return (double)dc;
                    try { return Convert.ToDouble(v); } catch { }
                }
            }
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (p.Name.Equals("LastMapTime", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("last_map_time", StringComparison.OrdinalIgnoreCase))
                {
                    var v = p.GetValue(listener);
                    if (v == null) return null;
                    if (v is double d) return d;
                    if (v is float fl) return (double)fl;
                    if (v is int ii) return (double)ii;
                    if (v is long ll) return (double)ll;
                    if (v is decimal dc) return (double)dc;
                    try { return Convert.ToDouble(v); } catch { }
                }
            }
            if (listener is GameObject go) return go.LastMapTime;
        }
        catch { }
        return null;
    }

    private static Dictionary<(int X, int Y), string> CallAtPreMapRender(object listener, Dictionary<(int X, int Y), string> grid)
    {
        try
        {
            var type = listener.GetType();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var m in methods)
            {
                if (m.Name.Equals("AtPreMapRender", StringComparison.OrdinalIgnoreCase) || m.Name.Equals("at_pre_map_render", StringComparison.OrdinalIgnoreCase))
                {
                    var pars = m.GetParameters();
                    if (pars.Length == 1)
                    {
                        var res = m.Invoke(listener, new object[] { grid });
                        if (res is Dictionary<(int X, int Y), string> d) return d;
                        if (res != null)
                        {
                            try { return (Dictionary<(int X, int Y), string>)res; } catch { }
                        }
                        return grid;
                    }
                }
            }
            if (listener is GameObject go) return go.AtPreMapRender(grid);
        }
        catch { }
        return grid;
    }

    private static void CallAtMapUpdate(object listener, string mapStr, List<(string sym, string desc, (int x, int y) coord)> entries, int minX, int maxY, bool showLegend, string name)
    {
        try
        {
            var type = listener.GetType();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var m in methods)
            {
                if (m.Name.Equals("AtMapUpdate", StringComparison.OrdinalIgnoreCase) || m.Name.Equals("at_map_update", StringComparison.OrdinalIgnoreCase))
                {
                    var pars = m.GetParameters();
                    if (pars.Length == 6)
                    {
                        try { m.Invoke(listener, new object[] { mapStr, entries, minX, maxY, showLegend, name }); return; } catch { }
                    }
                }
            }
            if (listener is GameObject go) { go.AtMapUpdate(mapStr, entries, minX, maxY, showLegend, name); return; }
            try { ((dynamic)listener).AtMapUpdate(mapStr, entries, minX, maxY, showLegend, name); return; } catch { }
            try { ((dynamic)listener).at_map_update(mapStr, entries, minX, maxY, showLegend, name); return; } catch { }
        }
        catch { }
    }

    private static void CallAtLegendUpdate(object listener, List<(string sym, string desc, (int x, int y) coord)> entries, bool show, string area)
    {
        try
        {
            var type = listener.GetType();
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var m in methods)
            {
                if (m.Name.Equals("AtLegendUpdate", StringComparison.OrdinalIgnoreCase) || m.Name.Equals("at_legend_update", StringComparison.OrdinalIgnoreCase))
                {
                    var pars = m.GetParameters();
                    if (pars.Length == 3)
                    {
                        try { m.Invoke(listener, new object[] { entries, show, area }); return; } catch { }
                    }
                }
            }
            if (listener is GameObject go) { go.AtLegendUpdate(entries, show, area); return; }
            try { ((dynamic)listener).AtLegendUpdate(entries, show, area); return; } catch { }
            try { ((dynamic)listener).at_legend_update(entries, show, area); return; } catch { }
        }
        catch { }
    }

    public virtual void RenderLegend()
    {
        List<GameObject> listenersSnapshot;
        bool isOver;
        bool wasSuppressed;
        List<GameObject> objectsSnapshot = new();
        List<LegendEntry> staticSnapshot = new();
        Lock.EnterWriteLock();
        try
        {
            isOver = IsOverLegendCap();
            listenersSnapshot = Listeners.Values.ToList();
            wasSuppressed = _legendSuppressed;
            if (isOver)
            {
                if (wasSuppressed) return;
                _legendSuppressed = true;
            }
            else
            {
                if (wasSuppressed) _legendSuppressed = false;
                objectsSnapshot = Objects.Values.ToList();
                staticSnapshot = LegendEntries.ToList();
            }
        }
        finally { Lock.ExitWriteLock(); }

        if (isOver && !wasSuppressed)
        {
            foreach (var l in listenersSnapshot)
            {
                CallAtLegendUpdate(l, new List<(string, string, (int, int))>(), false, Name);
            }
            return;
        }
        if (!isOver)
        {
            var objEntries = new List<(int oid, (string sym, string desc, (int x, int y) coord) entry)>();
            foreach (var o in objectsSnapshot)
            {
                if (TryGetLocationCoord(o, out var c))
                {
                    string sym = "";
                    string desc = "";
                    try { sym = o.Symbol ?? ""; } catch { }
                    try { desc = o.Name ?? ""; } catch { }
                    objEntries.Add((o.Id, (sym, desc, c)));
                }
            }
            var staticEntries = staticSnapshot.Select(e => (e.Symbol ?? "", e.Desc ?? "", e.Coord ?? (0, 0))).ToList();
            foreach (var l in listenersSnapshot)
            {
                var entries = new List<(string, string, (int, int))>();
                foreach (var (oid, e) in objEntries) if (oid != l.Id) entries.Add(e);
                entries.AddRange(staticEntries);
                CallAtLegendUpdate(l, entries, true, Name);
            }
        }
    }

    public virtual void Render(bool force = false)
    {
        bool needsPre;
        Lock.EnterReadLock();
        try { needsPre = (force || MapChanged) && PreGrid.Count > 0; }
        finally { Lock.ExitReadLock(); }
        if (needsPre) PreRender();
        Lock.EnterWriteLock();
        try { MapChanged = false; }
        finally { Lock.ExitWriteLock(); }

        List<GameObject> listeners;
        Dictionary<(int X, int Y), string> gridSnapshot;
        List<GameObject> objectsSnapshot;
        List<LegendEntry> staticSnapshot;
        bool showLegend;
        Lock.EnterReadLock();
        try
        {
            showLegend = !IsOverLegendCap();
            listeners = Listeners.Values.ToList();
            objectsSnapshot = Objects.Values.ToList();
            staticSnapshot = LegendEntries.ToList();
            gridSnapshot = new Dictionary<(int X, int Y), string>(PostGrid);
        }
        finally { Lock.ExitReadLock(); }

        var objEntries = new List<(int oid, (string sym, string desc, (int x, int y) coord) entry)>();
        foreach (var o in objectsSnapshot)
        {
            if (TryGetLocationCoord(o, out var c))
            {
                string sym = "";
                string desc = "";
                try { sym = o.Symbol ?? ""; } catch { }
                try { desc = o.Name ?? ""; } catch { }
                objEntries.Add((o.Id, (sym, desc, c)));
            }
        }
        var staticEntries = staticSnapshot.Select(e => (e.Symbol ?? "", e.Desc ?? "", e.Coord ?? (0, 0))).ToList();

        double fpsLimit = 0;
        try
        {
            int limit = AtherizSettings.Global.MapFpsLimit;
            if (limit > 0) fpsLimit = 1.0 / limit;
            else fpsLimit = 0;
        }
        catch { fpsLimit = 0; }
        double now = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();

        foreach (var l in listeners)
        {
            if (!GetMapEnabled(l)) continue;
            var last = GetLastMapTime(l);
            bool hasLast = last.HasValue && last.Value != 0;
            if (hasLast && !force && fpsLimit > 0 && (now - last!.Value) <= fpsLimit) continue;
            var entries = new List<(string, string, (int, int))>();
            foreach (var (oid, e) in objEntries) if (oid != l.Id) entries.Add(e);
            entries.AddRange(staticEntries);
            var gridCopy = new Dictionary<(int X, int Y), string>(gridSnapshot);
            gridCopy = CallAtPreMapRender(l, gridCopy);
            var (mapStr, minX, maxY) = RenderGrid(gridCopy);
            CallAtMapUpdate(l, mapStr, entries, minX, maxY, showLegend, Name);
        }
    }

    public virtual void AddLegendEntry(LegendEntry entry)
    {
        using (WriteScope()) { LegendEntries.Add(entry); MapChanged = true; IsModified = true; }
        RenderLegend();
    }

    public virtual void RemoveLegendEntry(LegendEntry entry)
    {
        using (WriteScope()) { LegendEntries.Remove(entry); MapChanged = true; IsModified = true; }
        RenderLegend();
    }

    public virtual void AddListener(GameObject listener, bool notify = false)
    {
        Lock.EnterWriteLock();
        try { Listeners[listener.Id] = listener; }
        finally { Lock.ExitWriteLock(); }
        if (notify) Render(true);
    }

    public virtual void RemoveListener(GameObject listener)
    {
        Lock.EnterWriteLock();
        try { Listeners.Remove(listener.Id); }
        finally { Lock.ExitWriteLock(); }
    }

    public virtual void AddMapable(GameObject mapable, bool notify = true)
    {
        Lock.EnterWriteLock();
        try { Objects[mapable.Id] = mapable; }
        finally { Lock.ExitWriteLock(); }
        if (notify) RenderLegend();
    }

    public virtual void RemoveMapable(GameObject mapable)
    {
        Lock.EnterWriteLock();
        try { Objects.Remove(mapable.Id); }
        finally { Lock.ExitWriteLock(); }
        RenderLegend();
    }

    public virtual void AddMapableList(IEnumerable<GameObject> mapables, bool notify = true)
    {
        Lock.EnterWriteLock();
        try { foreach (var m in mapables) Objects[m.Id] = m; }
        finally { Lock.ExitWriteLock(); }
        if (notify) RenderLegend();
    }

    // DTO for JSON persistence
    public sealed class MapInfoPersistDto
    {
        public string Name { get; set; } = "unknown";
        public Dictionary<string, string> PreGrid { get; set; } = new();
        public Dictionary<string, string> PostGrid { get; set; } = new();
        public List<LegendEntryDto> LegendEntries { get; set; } = new();

        public static MapInfoPersistDto FromDomain(MapInfo mi)
        {
            mi.Lock.EnterReadLock();
            try
            {
                return new MapInfoPersistDto
                {
                    Name = mi.Name,
                    PreGrid = mi.PreGrid.ToDictionary(kv => $"{kv.Key.X},{kv.Key.Y}", kv => kv.Value),
                    PostGrid = mi.PostGrid.ToDictionary(kv => $"{kv.Key.X},{kv.Key.Y}", kv => kv.Value),
                    LegendEntries = mi.LegendEntries.Select(LegendEntryDto.FromDomain).ToList(),
                };
            }
            finally { mi.Lock.ExitReadLock(); }
        }

        public MapInfo ToDomain(AtherizSettings settings)
        {
            var mi = new MapInfo { Name = Name, Settings = settings, MapChanged = false, IsModified = false };
            foreach (var kv in PreGrid)
            {
                var parts = kv.Key.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
                    mi.PreGrid[(x, y)] = kv.Value;
            }
            foreach (var kv in PostGrid)
            {
                var parts = kv.Key.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
                    mi.PostGrid[(x, y)] = kv.Value;
            }
            foreach (var le in LegendEntries) mi.LegendEntries.Add(le.ToDomain());
            mi.MapChanged = false;
            mi.IsModified = false;
            return mi;
        }
    }

    public sealed class LegendEntryDto
    {
        public string? Symbol { get; set; }
        public string? Desc { get; set; }
        public List<int>? Coord { get; set; }
        public bool Show { get; set; } = true;
        public double Fg { get; set; } = 170.0;
        public double? Bg { get; set; }

        public static LegendEntryDto FromDomain(LegendEntry e)
        {
            return new LegendEntryDto
            {
                Symbol = e.Symbol,
                Desc = e.Desc,
                Coord = e.Coord is null ? null : new List<int> { e.Coord.Value.X, e.Coord.Value.Y },
                Show = e.Show,
                Fg = e.Fg,
                Bg = e.Bg,
            };
        }

        public LegendEntry ToDomain()
        {
            (int, int)? c = null;
            if (Coord != null && Coord.Count >= 2) c = (Coord[0], Coord[1]);
            var e = new LegendEntry(Symbol, Desc, c);
            e.Show = Show;
            e.Fg = Fg;
            e.Bg = Bg;
            return e;
        }
    }
}

// ---------------------------------------------------------------------------
// MapHandler — mirrors atheriz/globals/map.py:MapHandler
// ---------------------------------------------------------------------------

/// <summary>
/// Faithful port of <c>atheriz/globals/map.py:MapHandler</c>.
/// Persistence via EF Core JSON (replaces dill). ReaderWriterLockSlim mirrors RLock.
/// </summary>
public class MapHandler
{
    // Audit P2-10: hide public Lock, use NoRecursion (no re-entrant path; snapshots used)
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
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
    // Test hook for serialization lock verification
    public static Func<object, string>? TestSerializeHook;
    private readonly Dictionary<(string Area, int Z), MapInfo> _data = new();
    private readonly AtherizSettings _settings;

    public MapHandler() : this(null, true) { }
    public MapHandler(AtherizSettings? settings, bool autoLoad = true)
    {
        _settings = settings ?? AtherizSettings.Default;
        if (autoLoad) Load();
    }
    public MapHandler(bool autoLoad) : this(null, autoLoad) { }

    public bool IsDirty()
    {
        Lock.EnterReadLock();
        try
        {
            foreach (var mi in _data.Values)
            {
                mi.Lock.EnterReadLock();
                try { if (mi.MapChanged || mi.IsModified) return true; }
                finally { mi.Lock.ExitReadLock(); }
            }
            return false;
        }
        finally { Lock.ExitReadLock(); }
    }

    public void Load()
    {
        try { Load(AtherizDbContextFactory.Create()); } catch { }
    }
    public void Load(AtherizDbContext db)
    {
        try
        {
            db.Database.EnsureCreated();
            var buffer = new Dictionary<(string, int), MapInfo>();
            JsonTableLoader.LoadList(db.MapData, json => JsonSerializer.Deserialize<MapInfo.MapInfoPersistDto>(json, JsonOptions.Default), (dto, row) =>
            {
                var mi = dto.ToDomain(_settings);
                buffer[(row.Area, row.Z)] = mi;
            });
            Lock.EnterWriteLock();
            try { foreach (var kv in buffer) _data[kv.Key] = kv.Value; }
            finally { Lock.ExitWriteLock(); }
        }
        catch { }
    }

    public virtual void Save(bool force = false)
    {
        try { Save(AtherizDbContextFactory.Create(), force); }
        catch
        {
            // Python: catches Exception around get_database and around save, restores flags
            // If Create throws (DB closed), ensure flags remain dirty (not cleared) – since we cleared optimistically inside Save(db), we need to restore
            // But if Create throws before Save(db) is entered, flags were not yet cleared, so nothing to restore
            // Just swallow to mimic Python's logger.error and not raise
        }
    }
    public virtual void Save(AtherizDbContext db, bool force = false)
    {
        if (!force && !ObjectRegistry.AlwaysSaveAll && !_settings.AlwaysSaveAll && !IsDirty()) return;

        List<((string Area, int Z) Key, MapInfo Info)> refs;
        Lock.EnterReadLock();
        try { refs = _data.Select(kv => (kv.Key, kv.Value)).ToList(); }
        finally { Lock.ExitReadLock(); }

        var snapshot = new List<((string Area, int Z) Key, MapInfo.MapInfoPersistDto Dto, MapInfo Original)>();
        var cleared = new List<MapInfo>();
        var jsons = new List<((string Area, int Z) Key, string json)>();
        try
        {
            foreach (var (k, mi) in refs)
            {
                bool wasChanged;
                mi.Lock.EnterWriteLock();
                try
                {
                    wasChanged = mi.MapChanged || mi.IsModified;
                    if (wasChanged)
                    {
                        mi.MapChanged = false;
                        mi.IsModified = false;
                        cleared.Add(mi);
                    }
                }
                finally { mi.Lock.ExitWriteLock(); }

                var dto = MapInfo.MapInfoPersistDto.FromDomain(mi);
                // if not changed we still snapshot for write if force? For non-force we only write changed.
                if (!force && !wasChanged && !_settings.AlwaysSaveAll && !ObjectRegistry.AlwaysSaveAll) continue;
                snapshot.Add((k, dto, mi));
            }

            if (snapshot.Count == 0) return;

            // Serialize outside DB gate
            foreach (var (key, dto, _) in snapshot)
            {
                var json = TestSerializeHook != null ? TestSerializeHook(dto) : JsonSerializer.Serialize(dto, JsonOptions.Default);
                jsons.Add((key, json));
            }
        }
        catch
        {
            foreach (var mi in cleared)
            {
                mi.Lock.EnterWriteLock();
                try { mi.MapChanged = true; mi.IsModified = true; }
                finally { mi.Lock.ExitWriteLock(); }
            }
            throw;
        }

        try
        {
            DbTransactionHelper.WithGateAndTransaction(db, ctx =>
            {
                foreach (var (key, json) in jsons)
                {
                    DbTransactionHelper.UpsertJson(ctx.MapData, () => ctx.MapData.Find(key.Area, key.Z), () => new MapDataRow { Area = key.Area, Z = key.Z }, json);
                }
            }, onRollback: () =>
            {
                foreach (var mi in cleared)
                {
                    mi.Lock.EnterWriteLock();
                    try { mi.MapChanged = true; mi.IsModified = true; }
                    finally { mi.Lock.ExitWriteLock(); }
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping map save: {ex.Message}");
            foreach (var mi in cleared)
            {
                mi.Lock.EnterWriteLock();
                try { mi.MapChanged = true; mi.IsModified = true; }
                finally { mi.Lock.ExitWriteLock(); }
            }
            return;
        }
        catch (Exception ex) when (ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"database closed; skipping map save: {ex.Message}");
            foreach (var mi in cleared)
            {
                mi.Lock.EnterWriteLock();
                try { mi.MapChanged = true; mi.IsModified = true; }
                finally { mi.Lock.ExitWriteLock(); }
            }
            return;
        }
    }

    public void SetMapInfo(string area, int z, MapInfo mapInfo)
    {
        using (WriteScope()) _data[(area, z)] = mapInfo;
    }

    public MapInfo? GetMapInfo(string area, int z)
    {
        using (ReadScope()) return _data.TryGetValue((area, z), out var mi) ? mi : null;
    }

    private MapInfo GetOrCreate(string area, int z)
    {
        Lock.EnterWriteLock();
        try
        {
            if (!_data.TryGetValue((area, z), out var mi))
            {
                mi = new MapInfo { Name = area, Settings = _settings };
                _data[(area, z)] = mi;
            }
            return mi;
        }
        finally { Lock.ExitWriteLock(); }
    }

    public MapInfo GetOrCreatePublic(string area, int z) => GetOrCreate(area, z);
    public MapInfo EnsureMapInfo(string area, int z) => GetOrCreate(area, z);

    private static Coord? ExtractCoord(GameObject obj)
    {
        var loc = obj.Location;
        if (loc is Persistence.Dto.LocationRef.CoordLocation cl) return cl.Coord;
        if (loc is Persistence.Dto.LocationRef.ObjectLocation ol)
        {
            var target = ObjectRegistry.Get(ol.ObjectId);
            if (target.Count > 0 && target[0] is Node n) return n.Coord;
        }
        // fallback: if GameObject is Node itself
        if (obj is Node node) return node.Coord;
        return null;
    }

    public void AddMapable(GameObject mapable, bool notify = false)
    {
        var coord = ExtractCoord(mapable);
        if (coord == null) return;
        var mi = GetOrCreate(coord.Value.Area, coord.Value.Z);
        mi.AddMapable(mapable, notify);
    }

    public void AddListener(GameObject listener, bool notify = false)
    {
        var coord = ExtractCoord(listener);
        if (coord == null) return;
        var mi = GetOrCreate(coord.Value.Area, coord.Value.Z);
        mi.AddListener(listener, notify);
    }

    public void RemoveListener(GameObject listener)
    {
        var coord = ExtractCoord(listener);
        if (coord == null) return;
        MapInfo? mi;
        Lock.EnterReadLock();
        try { _data.TryGetValue((coord.Value.Area, coord.Value.Z), out mi); }
        finally { Lock.ExitReadLock(); }
        mi?.RemoveListener(listener);
    }

    public void MoveListener(GameObject listener, Coord toCoord, Coord? fromCoord = null)
    {
        MapInfo? fromMap = null, toMap = null;
        bool areaChanged = fromCoord != null && (fromCoord.Value.Area != toCoord.Area || fromCoord.Value.Z != toCoord.Z);
        Lock.EnterWriteLock();
        try
        {
            if (fromCoord != null) _data.TryGetValue((fromCoord.Value.Area, fromCoord.Value.Z), out fromMap);
            if (!_data.TryGetValue((toCoord.Area, toCoord.Z), out toMap))
            {
                toMap = new MapInfo { Name = toCoord.Area, Settings = _settings };
                _data[(toCoord.Area, toCoord.Z)] = toMap;
            }
            fromMap?.RemoveListener(listener);
            toMap.AddListener(listener);
        }
        finally { Lock.ExitWriteLock(); }
        if (areaChanged) SendUnbackground(listener);
        fromMap?.Render(false);
        toMap?.Render(true);
    }

    private static void SendUnbackground(GameObject listener)
    {
        try
        {
            var conn = listener.Session?.Connection;
            conn?.SendCommand("unbackground", new List<object?> { "" }, null);
        }
        catch { }
    }

    public void MoveMapable(GameObject mapable, Coord toCoord, Coord? fromCoord = null)
    {
        if (fromCoord != null && fromCoord.Value.Area == toCoord.Area && fromCoord.Value.Z == toCoord.Z)
        {
            var cur = GetOrCreate(toCoord.Area, toCoord.Z);
            cur.AddMapable(mapable);
            cur.Render(true);
            return;
        }
        MapInfo? fromMap = null, toMap = null;
        Lock.EnterWriteLock();
        try
        {
            if (fromCoord != null) _data.TryGetValue((fromCoord.Value.Area, fromCoord.Value.Z), out fromMap);
            if (!_data.TryGetValue((toCoord.Area, toCoord.Z), out toMap))
            {
                toMap = new MapInfo { Name = toCoord.Area, Settings = _settings };
                _data[(toCoord.Area, toCoord.Z)] = toMap;
            }
            fromMap?.RemoveMapable(mapable);
            toMap.AddMapable(mapable);
        }
        finally { Lock.ExitWriteLock(); }
        fromMap?.Render(false);
        toMap?.Render(true);
    }

    public void MoveListenerAndMapable(GameObject obj, Coord toCoord, Coord? fromCoord = null)
    {
        if (fromCoord != null && fromCoord.Value.Area == toCoord.Area && fromCoord.Value.Z == toCoord.Z)
        {
            var cur = GetOrCreate(toCoord.Area, toCoord.Z);
            cur.Lock.EnterWriteLock();
            try
            {
                cur.Listeners[obj.Id] = obj;
                cur.Objects[obj.Id] = obj;
            }
            finally { cur.Lock.ExitWriteLock(); }
            cur.RenderLegend();
            cur.Render(true);
            return;
        }
        MapInfo? fromMap = null, toMap = null;
        bool areaChanged = fromCoord != null && (fromCoord.Value.Area != toCoord.Area || fromCoord.Value.Z != toCoord.Z);
        Lock.EnterWriteLock();
        try
        {
            if (fromCoord != null) _data.TryGetValue((fromCoord.Value.Area, fromCoord.Value.Z), out fromMap);
            if (!_data.TryGetValue((toCoord.Area, toCoord.Z), out toMap))
            {
                toMap = new MapInfo { Name = toCoord.Area, Settings = _settings };
                _data[(toCoord.Area, toCoord.Z)] = toMap;
            }
            if (fromMap != null)
            {
                fromMap.Listeners.Remove(obj.Id);
                fromMap.Objects.Remove(obj.Id);
            }
            toMap.Listeners[obj.Id] = obj;
            toMap.Objects[obj.Id] = obj;
        }
        finally { Lock.ExitWriteLock(); }
        if (areaChanged) SendUnbackground(obj);
        if (fromMap != null && !ReferenceEquals(fromMap, toMap))
        {
            fromMap.RenderLegend();
            fromMap.Render(false);
        }
        toMap.RenderLegend();
        toMap.Render(true);
    }

    public void RemoveMapable(GameObject mapable, string fromArea, int fromZ)
    {
        MapInfo? fromMap;
        Lock.EnterReadLock();
        try { _data.TryGetValue((fromArea, fromZ), out fromMap); }
        finally { Lock.ExitReadLock(); }
        fromMap?.RemoveMapable(mapable);
    }

    public void Clear()
    {
        Lock.EnterWriteLock();
        try { _data.Clear(); }
        finally { Lock.ExitWriteLock(); }
    }

    public IReadOnlyDictionary<(string Area, int Z), MapInfo> Snapshot()
    {
        Lock.EnterReadLock();
        try { return new Dictionary<(string, int), MapInfo>(_data); }
        finally { Lock.ExitReadLock(); }
    }
}
