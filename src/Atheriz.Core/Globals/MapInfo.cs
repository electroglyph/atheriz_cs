using Atheriz.Core;

namespace Atheriz.Core.Globals;

// ---------------------------------------------------------------------------
// MapInfo — mirrors atheriz/globals/map.py:MapInfo
// ---------------------------------------------------------------------------

/// <summary>
/// Keeps public fields, locks, IsDirty/MapChanged semantics, BFS stubs.
/// JSON persistence replaces dill.
/// </summary>
public class MapInfo
{
    public string Name { get; set; } = "unknown";
    // every MapChanged=true bumps the dirty generation, so Render
    // can clear only dirtiness it already rendered (an UpdateGrid landing
    // between PreRender and the clear must survive for the next render).
    private bool _mapChanged = true;
    private long _mapGen;
    public bool MapChanged
    {
        get => _mapChanged;
        set { if (value) System.Threading.Interlocked.Increment(ref _mapGen); _mapChanged = value; }
    }
    public bool IsModified { get; set; } = false; // for parity with Python getattr is_modified
    // Snapshot copies: the getters return copies under the read lock, so a
    // reader enumerating the copy never races a concurrent mutator. The lock
    // is NoRecursion, so lock-held internals below touch _preGrid/_postGrid/
    // _legendEntries/_objects/_listeners directly, and external callers that
    // used to mutate the live dict under their own mi scope use the locked
    // cell mutators (SetPreCell/RemovePreCell/SetPostCell/...) instead.
    private Dictionary<(int X, int Y), string> _preGrid = new();
    public Dictionary<(int X, int Y), string> PreGrid
    {
        get { using (ReadScope()) return new Dictionary<(int X, int Y), string>(_preGrid); }
    }
    private Dictionary<(int X, int Y), string> _postGrid = new();
    public Dictionary<(int X, int Y), string> PostGrid
    {
        get { using (ReadScope()) return new Dictionary<(int X, int Y), string>(_postGrid); }
    }
    private List<LegendEntry> _legendEntries = new();
    public List<LegendEntry> LegendEntries
    {
        get { using (ReadScope()) return new List<LegendEntry>(_legendEntries); }
    }
    private Dictionary<int, GameObject> _objects = new();
    public Dictionary<int, GameObject> Objects
    {
        get { using (ReadScope()) return new Dictionary<int, GameObject>(_objects); }
    }
    private Dictionary<int, GameObject> _listeners = new();
    public Dictionary<int, GameObject> Listeners
    {
        get { using (ReadScope()) return new Dictionary<int, GameObject>(_listeners); }
    }
    // Lock uses NoRecursion (no re-entrant path; snapshots used)
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    public ReaderWriterLockSlim SyncRoot => _lock;
    // Compat: keep public Lock for Ported tests (now delegates to private _lock); new code should use SyncRoot/ReadScope/WriteScope
    public ReaderWriterLockSlim Lock => _lock;
    public IDisposable ReadScope() { _lock.EnterReadLock(); return new LockScope(_lock, false); }
    public IDisposable WriteScope() { _lock.EnterWriteLock(); return new LockScope(_lock, true); }
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
        Settings = settings ?? AtherizSettings.Global;
        if (preGrid is not null) foreach (var kv in preGrid) _preGrid[kv.Key] = kv.Value;
        if (postGrid is not null) foreach (var kv in postGrid) _postGrid[kv.Key] = kv.Value;
        if (legendEntries is not null) _legendEntries.AddRange(legendEntries);
    }

    private bool IsOverLegendCap()
    {
        return (_objects.Count + _legendEntries.Count) > Settings.MaxObjectsPerLegend;
    }

    public bool Equals(MapInfo? other)
    {
        if (other is null) return false;
        if (Name != other.Name) return false;
        // Snapshot each side under its own read lock (taking both info locks
        // here would order them arbitrarily).
        var myPre = PreGrid; var otherPre = other.PreGrid;
        if (myPre.Count != otherPre.Count) return false;
        var myPost = PostGrid; var otherPost = other.PostGrid;
        if (myPost.Count != otherPost.Count) return false;
        if (!myPre.OrderBy(kv => kv.Key).SequenceEqual(otherPre.OrderBy(kv => kv.Key))) return false;
        if (!myPost.OrderBy(kv => kv.Key).SequenceEqual(otherPost.OrderBy(kv => kv.Key))) return false;
        var myLegend = LegendEntries; var otherLegend = other.LegendEntries;
        if (myLegend.Count != otherLegend.Count) return false;
        for (int i = 0; i < myLegend.Count; i++) if (!myLegend[i].Equals(otherLegend[i])) return false;
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
                    if (_preGrid.TryGetValue(key, out var existing) && existing == Settings.RoomPlaceholder)
                        continue;
                    _preGrid[key] = ch;
                }
            MapChanged = true;
            IsModified = true;
        }
    }

    public static (string Rendered, int MinX, int MaxY) RenderGrid(Dictionary<(int X, int Y), string> grid)
    {
        if (grid.Count == 0) return ("", 0, 0);
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var k in grid.Keys)
        {
            if (k.X < minX) minX = k.X;
            if (k.X > maxX) maxX = k.X;
            if (k.Y < minY) minY = k.Y;
            if (k.Y > maxY) maxY = k.Y;
        }
        List<string> lines = [];
        for (int y = maxY; y >= minY; y--)
        {
            var row = new System.Text.StringBuilder();
            for (int x = minX; x <= maxX; x++)
                row.Append(grid.TryGetValue((x, y), out var v) ? v : " ");
            lines.Add(row.ToString());
        }
        return (string.Join("\n", lines), minX, maxY);
    }

    // The List<string> overload was removed; callers pass HashSet<string> directly
    // to avoid a per-call copy. This narrows the public surface: external game
    // code calling the removed overload will fail to compile. No in-repo callers
    // remain, but out-of-repo callers cannot be verified from this tree.
    public static (bool N, bool S, bool E, bool W) GetDirs(Dictionary<(int X, int Y), string> grid, (int X, int Y) coord, HashSet<string> chars)
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
        if (n && s && e && w) return style == "double" ? "╬" : "┼";
        if (style == "single")
        {
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
        using (WriteScope())
        {
            Dictionary<string, string> placeholderStyles = new()
            {
                [Settings.SingleWallPlaceholder] = "single",
                [Settings.DoubleWallPlaceholder] = "double",
                [Settings.RoundedWallPlaceholder] = "rounded",
                [Settings.PathPlaceholder] = "rounded",
                [Settings.RoadPlaceholder] = "double",
            };
            var allSymbols = new HashSet<string>(Settings.AllSymbols);
            // No working copy of the grid: the resolve pass reads PreGrid
            // (stable under the held write lock, identical to the old copy)
            // and the publish pass writes PostGrid, so in-progress glyphs
            // never pollute neighbor reads. The style table stays per-call:
            // placeholders and symbols are per-Settings-instance.
            Dictionary<(int, int), string> toPlace = [];
            foreach (var kv in _preGrid)
            {
                if (placeholderStyles.TryGetValue(kv.Value, out var style))
                {
                    var (n, s, e, w) = GetDirs(_preGrid, kv.Key, allSymbols);
                    toPlace[kv.Key] = ResolveChar(n, s, e, w, style);
                }
                else if (kv.Value == Settings.RoomPlaceholder)
                {
                    toPlace[kv.Key] = " ";
                }
            }
            _postGrid.Clear();
            _postGrid.EnsureCapacity(_preGrid.Count);
            foreach (var kv in _preGrid) _postGrid[kv.Key] = toPlace.TryGetValue(kv.Key, out var resolved) ? resolved : kv.Value;
        }
    }

    public void UpdateGrid((int X, int Y) coord, string newSymbol)
    {
        bool shouldRender;
        using (WriteScope())
        {
            _preGrid[coord] = newSymbol;
            MapChanged = true;
            IsModified = true;
            shouldRender = _batchUpdate == 0;
        }
        if (shouldRender) Render(true);
    }

    public IDisposable BatchUpdate()
    {
        using (WriteScope())
        {
            _batchUpdate++;
            if (_preGrid.Count == 0 && _postGrid.Count > 0)
            {
                foreach (var kv in _postGrid) _preGrid[kv.Key] = kv.Value;
            }
        }
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
            using (_mi.WriteScope())
            {
                _mi._batchUpdate--;
                shouldRender = _mi._batchUpdate == 0 && _mi.MapChanged;
            }
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
            if (loc is null) return false;
            if (loc is Persistence.Dto.LocationRef.NullLocation) return false;
            if (loc is Persistence.Dto.LocationRef.CoordLocation cl)
            {
                coord = (cl.Coord.X, cl.Coord.Y);
                return true;
            }
            if (loc is Persistence.Dto.LocationRef.ObjectLocation ol)
            {
                var target = Globals.ObjectRegistry.GetSingle(ol.ObjectId);
                if (target is not null)
                {
                    if (target is Node n) { coord = (n.Coord.X, n.Coord.Y); return true; }
                    var tloc = target.Location;
                    if (tloc is Persistence.Dto.LocationRef.CoordLocation tcl) { coord = (tcl.Coord.X, tcl.Coord.Y); return true; }
                }
                return false;
            }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed BatchScope.TryGetLocationCoord: " + logEx.Message, "BatchScope"); }
        return false;
    }

    // Single suppression point for game-override calls: a throwing override
    // degrades to the fallback instead of breaking the render for everyone
    // else. (The old per-call wrappers duplicated this try/catch six times.)
    internal static T Suppress<T>(Func<T> call, T fallback, string what)
    {
        try { return call(); }
        catch (Exception ex) { AtherizLogger.LogDebug($"Suppressed MapHandler.{what}: " + ex.Message, "MapHandler"); return fallback; }
    }

    internal static void Suppress(Action call, string what)
    {
        try { call(); }
        catch (Exception ex) { AtherizLogger.LogDebug($"Suppressed MapHandler.{what}: " + ex.Message, "MapHandler"); }
    }

    private static (List<(int oid, (string sym, string desc, (int x, int y) coord) entry)> objEntries, List<(string, string, (int, int))> staticEntries)
        BuildEntries(List<GameObject> objectsSnapshot, List<LegendEntry> staticSnapshot)
    {
        List<(int oid, (string sym, string desc, (int x, int y) coord) entry)> objEntries = [];
        foreach (var o in objectsSnapshot)
        {
            if (TryGetLocationCoord(o, out var c))
            {
                string sym = Suppress(() => o.Symbol ?? "", "", "Symbol");
                string desc = Suppress(() => o.Name ?? "", "", "Name");
                objEntries.Add((o.Id, (sym, desc, c)));
            }
        }
        var staticEntries = new List<(string, string, (int, int))>(staticSnapshot.Count);
        foreach (var e in staticSnapshot)
        {
            if (e.Coord is not null)
                staticEntries.Add((e.Symbol ?? "", e.Desc ?? "", e.Coord.Value));
        }
        return (objEntries, staticEntries);
    }

    private static List<(string, string, (int, int))> EntriesFor(
        List<(int oid, (string sym, string desc, (int x, int y) coord) entry)> objEntries,
        List<(string, string, (int, int))> staticEntries, int listenerId)
    {
        List<(string, string, (int, int))> entries = [];
        foreach (var (oid, e) in objEntries) if (oid != listenerId) entries.Add(e);
        entries.AddRange(staticEntries);
        return entries;
    }

    // Shared-list variant of EntriesFor for the Render fan-out: listeners
    // absent from objEntries all filter to the same list, so one instance is
    // built once and reused. The shared list is read-only downstream
    // (AtMapUpdate consumes it; only AtPreMapRender transforms, and that
    // keeps its per-listener grid copy) — a listener present in objEntries
    // still gets a private filtered copy.
    private static List<(string, string, (int, int))> EntriesForShared(
        List<(int oid, (string sym, string desc, (int x, int y) coord) entry)> objEntries,
        List<(string, string, (int, int))> staticEntries, int listenerId,
        ref List<(string, string, (int, int))>? shared)
    {
        foreach (var (oid, _) in objEntries)
            if (oid == listenerId) return EntriesFor(objEntries, staticEntries, listenerId);
        shared ??= EntriesFor(objEntries, staticEntries, listenerId);
        return shared;
    }

    public virtual void RenderLegend()
    {
        List<GameObject> listenersSnapshot;
        bool isOver;
        bool wasSuppressed;
        List<GameObject> objectsSnapshot = new();
        List<LegendEntry> staticSnapshot = new();
        using (WriteScope())
        {
            isOver = IsOverLegendCap();
            listenersSnapshot = _listeners.Values.ToList();
            wasSuppressed = _legendSuppressed;
            if (isOver)
            {
                if (wasSuppressed) return;
                _legendSuppressed = true;
            }
            else
            {
                if (wasSuppressed) _legendSuppressed = false;
                objectsSnapshot = _objects.Values.ToList();
                staticSnapshot = _legendEntries.ToList();
            }
        }

        if (isOver && !wasSuppressed)
        {
            foreach (var l in listenersSnapshot)
            {
                Suppress(() => { l.AtLegendUpdate([], false, Name); }, "AtLegendUpdate");
            }
            return;
        }
        if (!isOver)
        {
            var (objEntries, staticEntries) = BuildEntries(objectsSnapshot, staticSnapshot);
            // Shared filtered list across listeners absent from objEntries, like Render:
            // read-only downstream (ProjectLegendEntries copies for the payload).
            // If an at_legend_update hook mutates the received list it leaks to later
            // listeners — same assumption Render makes for at_map_update (see simplify.md §8.1).
            List<(string, string, (int, int))>? sharedEntries = null;
            foreach (var l in listenersSnapshot)
            {
                var entries = EntriesForShared(objEntries, staticEntries, l.Id, ref sharedEntries);
                Suppress(() => { l.AtLegendUpdate(entries, true, Name); }, "AtLegendUpdate");
            }
        }
    }

    public virtual void Render(bool force = false)
    {
        bool needsPre;
        long gen;
        using (ReadScope()) { needsPre = (force || MapChanged) && _preGrid.Count > 0; gen = _mapGen; }
        if (needsPre) PreRender();
        using (WriteScope()) { if (_mapGen == gen) MapChanged = false; }

        List<GameObject> listeners;
        Dictionary<(int X, int Y), string> gridSnapshot;
        List<GameObject> objectsSnapshot;
        List<LegendEntry> staticSnapshot;
        bool showLegend;
        using (ReadScope())
        {
            showLegend = !IsOverLegendCap();
            listeners = _listeners.Values.ToList();
            objectsSnapshot = _objects.Values.ToList();
            staticSnapshot = _legendEntries.ToList();
            gridSnapshot = new Dictionary<(int X, int Y), string>(_postGrid);
        }

        var (objEntries, staticEntries) = BuildEntries(objectsSnapshot, staticSnapshot);
        List<(string, string, (int, int))>? sharedEntries = null;

        // The handler's own settings, not the ambient global: an
        // explicit-settings boot must throttle with its own limit.
        double fpsLimit = Suppress(() => { int limit = Settings.MapFpsLimit; return limit > 0 ? 1.0 / limit : 0; }, 0.0, "MapFpsLimit");
        double now = global::Atheriz.Core.Utils.GameClock.MonotonicSeconds();

        foreach (var l in listeners)
        {
            if (!Suppress(() => l.MapEnabled, true, "MapEnabled")) continue;
            var last = Suppress<double?>(() => l.LastMapTime, null, "LastMapTime");
            bool hasLast = last.HasValue && last.Value != 0;
            if (hasLast && !force && fpsLimit > 0 && (now - last!.Value) <= fpsLimit) continue;
            // Listeners absent from the object entries share one filtered
            // list (read-only downstream); the per-listener grid copy stays:
            // AtPreMapRender mutations must not leak across listeners.
            var entries = EntriesForShared(objEntries, staticEntries, l.Id, ref sharedEntries);
            var gridCopy = new Dictionary<(int X, int Y), string>(gridSnapshot);
            gridCopy = Suppress(() => l.AtPreMapRender(gridCopy), gridCopy, "AtPreMapRender");
            var (mapStr, minX, maxY) = RenderGrid(gridCopy);
            Suppress(() => { l.AtMapUpdate(mapStr, entries, minX, maxY, showLegend, Name); }, "AtMapUpdate");
        }
    }

    public virtual void AddLegendEntry(LegendEntry entry)
    {
        using (WriteScope()) { _legendEntries.Add(entry); MapChanged = true; IsModified = true; }
        RenderLegend();
    }

    public virtual void RemoveLegendEntry(LegendEntry entry)
    {
        using (WriteScope()) { _legendEntries.Remove(entry); MapChanged = true; IsModified = true; }
        RenderLegend();
    }

    public virtual void AddListener(GameObject listener, bool notify = false)
    {
        using (WriteScope()) { _listeners[listener.Id] = listener; }
        if (notify) Render(true);
    }

    public virtual void RemoveListener(GameObject listener)
    {
        using (WriteScope()) { _listeners.Remove(listener.Id); }
    }

    public virtual void AddMapable(GameObject mapable, bool notify = true)
    {
        using (WriteScope()) { _objects[mapable.Id] = mapable; }
        if (notify) RenderLegend();
    }

    public virtual void RemoveMapable(GameObject mapable)
    {
        using (WriteScope()) { _objects.Remove(mapable.Id); }
        RenderLegend();
    }

    public virtual void AddMapableList(IEnumerable<GameObject> mapables, bool notify = true)
    {
        using (WriteScope()) { foreach (var m in mapables) _objects[m.Id] = m; }
        if (notify) RenderLegend();
    }

    // Locked cell mutators for external writers that used to mutate the live
    // grid dicts under their own mi scope (a snapshot getter cannot serve
    // them: the NoRecursion lock forbids re-entry). Each takes its own scope;
    // flag semantics match the old direct writes (none — render scheduling
    // and dirty tracking stay with the caller, as before).
    public void SetPreCell((int X, int Y) coord, string symbol)
    {
        using (WriteScope()) { _preGrid[coord] = symbol; }
    }
    public void RemovePreCell((int X, int Y) coord)
    {
        using (WriteScope()) { _preGrid.Remove(coord); }
    }
    public void SetPostCell((int X, int Y) coord, string symbol)
    {
        using (WriteScope()) { _postGrid[coord] = symbol; }
    }
    // Door glyph repaint: post cell always, pre cell only when the pre grid
    // is populated, plus map-dirty. One scope — the old manual mi.Lock block
    // read PreGrid.Count inside it, which a snapshot getter cannot do on a
    // NoRecursion lock.
    public void PaintSymbol((int X, int Y) coord, string glyph)
    {
        using (WriteScope())
        {
            _postGrid[coord] = glyph;
            if (_preGrid.Count > 0) { _preGrid[coord] = glyph; MapChanged = true; }
        }
    }
    // Legend bulk replace for the network legend-update path: swaps the whole
    // list and marks map-dirty under one scope (no render — the caller renders).
    public void ReplaceLegendEntries(List<LegendEntry> newEntries)
    {
        ArgumentNullException.ThrowIfNull(newEntries);
        using (WriteScope()) { _legendEntries.Clear(); _legendEntries.AddRange(newEntries); MapChanged = true; }
    }
    // Hot-reload rewire core: swap stale listener/mapable instances for the
    // replacement (same id) under one scope.
    public void ReplaceEntry(int id, GameObject replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        using (WriteScope())
        {
            if (_listeners.TryGetValue(id, out var l) && !ReferenceEquals(l, replacement))
                _listeners[id] = replacement;
            if (_objects.TryGetValue(id, out var o) && !ReferenceEquals(o, replacement))
                _objects[id] = replacement;
        }
    }
    // Same-map move core: listener + mapable land under one scope.
    public void AddListenerAndMapable(GameObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        using (WriteScope()) { _listeners[obj.Id] = obj; _objects[obj.Id] = obj; }
    }

    // DTO for JSON persistence
    public sealed record MapInfoPersistDto
    {
        public string Name { get; set; } = "unknown";
        public Dictionary<string, string> PreGrid { get; set; } = new();
        public Dictionary<string, string> PostGrid { get; set; } = new();
        public List<LegendEntryDto> LegendEntries { get; set; } = new();

        public static MapInfoPersistDto FromDomain(MapInfo mi)
        {
            using (mi.ReadScope())
            {
                return new MapInfoPersistDto
                {
                    Name = mi.Name,
                    PreGrid = mi._preGrid.ToDictionary(kv => $"{kv.Key.X},{kv.Key.Y}", kv => kv.Value),
                    PostGrid = mi._postGrid.ToDictionary(kv => $"{kv.Key.X},{kv.Key.Y}", kv => kv.Value),
                    LegendEntries = mi._legendEntries.Select(LegendEntryDto.FromDomain).ToList(),
                };
            }
        }

        public MapInfo ToDomain(AtherizSettings settings)
        {
            var mi = new MapInfo { Name = Name, Settings = settings, MapChanged = false, IsModified = false };
            foreach (var kv in PreGrid)
            {
                var parts = kv.Key.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
                    mi._preGrid[(x, y)] = kv.Value;
                // malformed persisted grid keys warn, not vanish silently.
                else AtherizLogger.LogWarning($"[Load] skipping malformed pre-grid key '{kv.Key}' in area '{Name}'.");
            }
            foreach (var kv in PostGrid)
            {
                var parts = kv.Key.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
                    mi._postGrid[(x, y)] = kv.Value;
                else AtherizLogger.LogWarning($"[Load] skipping malformed post-grid key '{kv.Key}' in area '{Name}'.");
            }
            foreach (var le in LegendEntries)
            {
                // Malformed persisted entries warn, not vanish silently —
                // and must not abort the whole area load (same convention
                // as the malformed grid keys above).
                try { mi._legendEntries.Add(le.ToDomain()); }
                catch (Exception ex) { AtherizLogger.LogWarning($"[Load] skipping malformed legend entry in area '{Name}': {ex.Message}"); }
            }
            mi.MapChanged = false;
            mi.IsModified = false;
            return mi;
        }
    }

    public sealed record LegendEntryDto
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
            // A legend coord is exactly [x, y] (matches TupleCoordConverter
            // and the network validator): extras must not silently truncate
            // and singletons must not invent y=0 — both throw instead.
            // Null stays legal (coord-less entry).
            (int, int)? c = null;
            if (Coord is not null)
            {
                if (Coord.Count != 2)
                    throw new JsonException($"Coord array must have exactly 2 elements ([x, y]); got {Coord.Count}.");
                c = (Coord[0], Coord[1]);
            }
            var e = new LegendEntry(Symbol, Desc, c);
            e.Show = Show;
            e.Fg = Fg;
            e.Bg = Bg;
            return e;
        }
    }
}
