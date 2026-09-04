using Atheriz.Core.Globals;
using Atheriz.Core.Utils;
using Atheriz.Core.Settings;
using System.Text.Json;

namespace Atheriz.Core.Objects;

/// <summary>
/// Faithful port of <c>atheriz/objects/base_door.py:Door</c>.
/// Door is AccessLock-style via declarative Policy.
/// Key fields preserved exactly: from_coord/to_coord/from_exit/to_exit/symbol_coord/closed_symbol/open_symbol/closed/locked.
/// </summary>
public class Door
{
    // TODO: SupportsRecursion required for re-entrant TryOpen/TryClose -> Access pattern (WriteLock held then Access acquires ReadLock)
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
    public Coord FromCoord { get; set; }
    public string FromExit { get; set; } = "";
    public Coord ToCoord { get; set; }
    public string ToExit { get; set; } = "";
    public (int X, int Y)? SymbolCoord { get; set; }
    public string ClosedSymbol { get; set; } = "";
    public string OpenSymbol { get; set; } = "";
    public bool Closed { get; set; } = true;
    public bool Locked { get; set; } = false;
    public string Name { get; set; } = "";
    public string DoorDesc { get; set; } = "";
    public int? KeyId { get; set; }

    private readonly Dictionary<string, List<Func<GameObject, bool>>> _locks = new();

    // Test instrumentation: call counts for faithful port of test_door_revert (mock_try_close / mock_map_close)
    public int TryOpenCallCount { get; private set; }
    public int TryCloseCallCount { get; private set; }
    public int MapOpenCallCount { get; private set; }
    public int MapCloseCallCount { get; private set; }
    public void ResetCallCounts() { TryOpenCallCount = 0; TryCloseCallCount = 0; MapOpenCallCount = 0; MapCloseCallCount = 0; }

    // Port of atheriz/objects/base_door.py:17
    public Door() { }
    // Port of base_door.py:28
    public Door(Coord from, Coord to, string fromExit, string toExit,
        (int, int)? symbolCoord = null, string closedSymbol = "", string openSymbol = "",
        bool closed = true, bool locked = false)
    {
        FromCoord = from; ToCoord = to; FromExit = fromExit; ToExit = toExit;
        SymbolCoord = symbolCoord; ClosedSymbol = closedSymbol; OpenSymbol = openSymbol;
        Closed = closed; Locked = locked;
        Name = fromExit;
        DoorDesc = "";
    }

    // Port of atheriz/objects/base_door.py:56 Door.create classmethod (verbatim)
    public static Door Create(Coord fromCoord, string fromExit, Coord toCoord, string toExit,
        (int, int)? symbolCoord = null, string closedSymbol = "", string openSymbol = "",
        bool closed = true, bool locked = false)
        => new Door(fromCoord, toCoord, fromExit, toExit, symbolCoord, closedSymbol, openSymbol, closed, locked);

    // Overload to support python keyword style and None to_coord
    public static Door Create(Coord? fromCoord, string fromExit, Coord? toCoord, string toExit,
        (int, int)? symbolCoord = null, string closedSymbol = "", string openSymbol = "",
        bool closed = true, bool locked = false)
    {
        var d = new Door();
        if (fromCoord != null) d.FromCoord = fromCoord.Value;
        if (toCoord != null) d.ToCoord = toCoord.Value;
        d.FromExit = fromExit ?? "";
        d.ToExit = toExit ?? "";
        d.SymbolCoord = symbolCoord;
        d.ClosedSymbol = closedSymbol ?? "";
        d.OpenSymbol = openSymbol ?? "";
        d.Closed = closed;
        d.Locked = locked;
        d.Name = fromExit ?? "";
        return d;
    }
    // Compat overload
    public Door(Coord from, Coord to, string name)
        : this(from, to, name, name, null, "", "", false, false) { }

    public bool IsClosed { get => Closed; set => Closed = value; }
    public bool IsLocked { get => Locked; set => Locked = value; }

    // Port of base_lock AccessLock add_lock/access pattern
    public void AddLock(string name, Func<GameObject, bool> pred)
    {
        using (WriteScope())
        {
            if (!_locks.TryGetValue(name, out var lst)) { lst = []; _locks[name] = lst; }
            lst.Add(pred);
        }
    }
    // Port of base_lock.py access
    public bool Access(GameObject? caller, string lockName)
    {
        if (caller is null) return false;
        if (caller.IsSuperUser) return true;
        List<Func<GameObject, bool>> snap;
        using (ReadScope())
        {
            if (!_locks.TryGetValue(lockName, out var lst) || lst.Count == 0) return true; snap = [.. lst];
        }
        foreach (var fn in snap) if (!fn(caller)) return false;
        return true;
    }
    public bool CanOpen(GameObject? caller) => Access(caller, "open");
    public bool CanClose(GameObject? caller) => Access(caller, "close");
    public bool CanLock(GameObject? caller) => Access(caller, "lock");
    public bool CanUnlock(GameObject? caller) => Access(caller, "unlock");

    // Port of base_door.py:80
    public override string ToString() => $"Door({FromCoord}, 'from_exit':{FromExit}, 'to_coord':{ToCoord}, 'to_exit':{ToExit})";

    // Port of base_door.py:86 desc
    public string Desc(Coord fromCoord)
    {
        using (ReadScope())
        {
            var status = Closed ? "A closed" : "An open";
            if (fromCoord.Equals(FromCoord)) return $"{status} door leading {FromExit}";
            if (fromCoord.Equals(ToCoord)) return $"{status} door leading {ToExit}";
            return "Door desc: unexpected coord.";
        }
    }

    // Port of base_door.py:96 get_nodes
    public (Node? fromNode, Node? toNode) GetNodes()
    {
        var nh = NodeHandler.GetCurrent();
        Node? fromNode = null, toNode = null;
        if (nh != null)
        {
            fromNode = nh.GetNode(FromCoord);
            toNode = nh.GetNode(ToCoord);
        }
        return (fromNode, toNode);
    }

    // Port of base_door.py:106 try_open
    public virtual bool TryOpen(GameObject caller)
    {
        TryOpenCallCount++;
        var (fromNode, toNode) = GetNodes();
        var loc = caller.ResolveLocationObject();
        string status;
        _lock.EnterWriteLock();
        try
        {
            if (!Closed) status = "already_open";
            else if (Locked) status = "locked";
            else if (!Access(caller, "open")) status = "no_access";
            else { Closed = false; status = "opened"; }
        }
        finally { _lock.ExitWriteLock(); }
        if (status == "opened")
        {
            try
            {
                var nh = NodeHandler.GetCurrent();
                if (nh != null)
                {
                    nh.Lock3.EnterWriteLock();
                    try { nh.MarkDoorsModified(); } finally { nh.Lock3.ExitWriteLock(); }
                }
            }
            catch { }
        }
        if (status == "already_open")
        {
            fromNode?.MsgContents($"$You(target) $conj(open) the already open door just to be sure.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
            toNode?.MsgContents($"$You(target) $conj(open) the already open door just to be sure.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
            return true;
        }
        if (status == "locked")
        {
            loc?.MsgContents($"$You(target) $conj(try) to open the door, but it won't budge.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
            return false;
        }
        if (status == "no_access")
        {
            loc?.MsgContents($"$You(target) $conj(try) to open the door, but an unknown force prevents it.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
            return false;
        }
        MapOpen();
        loc?.MsgContents($"$You(target) $conj(open) the door.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
        AtOpen(caller);
        return true;
    }
    // Port of base_door.py:106 wrapper for spec
    public bool Open(GameObject? caller = null) => caller != null ? TryOpen(caller) : TryOpenFallback();
    private bool TryOpenFallback()
    {
        _lock.EnterWriteLock();
        try { if (Locked) return false; if (!Closed) return true; Closed = false; return true; }
        finally { _lock.ExitWriteLock(); }
    }

    // Port of base_door.py:165 try_close
    public virtual bool TryClose(GameObject caller)
    {
        TryCloseCallCount++;
        var (fromNode, toNode) = GetNodes();
        var loc = caller.ResolveLocationObject();
        string status;
        _lock.EnterWriteLock();
        try
        {
            if (Closed) status = "already_closed";
            else if (!Access(caller, "close")) status = "no_access";
            else { Closed = true; status = "closed"; }
        }
        finally { _lock.ExitWriteLock(); }
        if (status == "closed")
        {
            try
            {
                var nh = NodeHandler.GetCurrent();
                if (nh != null) { nh.Lock3.EnterWriteLock(); try { nh.MarkDoorsModified(); } finally { nh.Lock3.ExitWriteLock(); } }
            }
            catch { }
        }
        if (status == "already_closed")
        {
            loc?.MsgContents($"$You(target) $conj(try) to close the door, but it is already closed.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
            return false;
        }
        if (status == "no_access")
        {
            fromNode?.MsgContents($"$You(target) $conj(try) to close the door, but an unknown force prevents it.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
            toNode?.MsgContents($"$You(target) $conj(try) to close the door, but an unknown force prevents it.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
            return false;
        }
        MapClose();
        fromNode?.MsgContents($"$You(target) $conj(close) the door.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
        toNode?.MsgContents($"$You(target) $conj(close) the door.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
        AtClose(caller);
        return true;
    }
    public bool Close(GameObject? caller = null) => caller != null ? TryClose(caller) : TryCloseFallback();
    private bool TryCloseFallback()
    {
        _lock.EnterWriteLock();
        try { if (Closed) return false; Closed = true; return true; }
        finally { _lock.ExitWriteLock(); }
    }

    // Port of base_door.py:220 try_lock
    public virtual bool TryLock(GameObject caller)
    {
        var loc = caller.ResolveLocationObject();
        string status;
        _lock.EnterWriteLock();
        try
        {
            if (!Access(caller, "lock")) status = "no_access";
            else if (!Closed) status = "not_closed";
            else if (Locked) status = "already_locked";
            else { Locked = true; status = "locked"; }
        }
        finally { _lock.ExitWriteLock(); }
        if (status == "locked")
        {
            try { var nh = NodeHandler.GetCurrent(); if (nh != null) { nh.Lock3.EnterWriteLock(); try { nh.MarkDoorsModified(); } finally { nh.Lock3.ExitWriteLock(); } } } catch { }
        }
        if (status == "no_access")
        {
            loc?.MsgContents($"$You(target) $conj(try) to lock the door, but an unknown force prevents it.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
            return false;
        }
        if (status == "not_closed")
        {
            loc?.MsgContents($"$You(target) $conj(try) to lock the door, but You can't lock an open door.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
            return false;
        }
        if (status == "already_locked")
        {
            loc?.MsgContents($"$You(target) $conj(try) to lock the door, but it is already locked.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
            return false;
        }
        loc?.MsgContents($"$You(target) $conj(lock) the door.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
        return true;
    }
    public bool LockDoor(GameObject? caller = null) => caller != null ? TryLock(caller) : false;

    // Port of base_door.py:271 try_unlock
    public virtual bool TryUnlock(GameObject caller)
    {
        var (fromNode, toNode) = GetNodes();
        var loc = caller.ResolveLocationObject();
        string status;
        _lock.EnterWriteLock();
        try
        {
            if (!Access(caller, "unlock")) status = "no_access";
            else if (Locked) { Locked = false; status = "unlocked"; }
            else status = "already_unlocked";
        }
        finally { _lock.ExitWriteLock(); }
        if (status == "unlocked")
        {
            try { var nh = NodeHandler.GetCurrent(); if (nh != null) { nh.Lock3.EnterWriteLock(); try { nh.MarkDoorsModified(); } finally { nh.Lock3.ExitWriteLock(); } } } catch { }
        }
        if (status == "no_access")
        {
            loc?.MsgContents($"$You(target) $conj(try) to unlock the door, but an unknown force prevents it.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
            return false;
        }
        if (status == "unlocked")
        {
            loc?.MsgContents($"$You(target) $conj(unlock) the door.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
            return true;
        }
        loc?.MsgContents($"$You(target) $conj(try) to unlock the door, but it is already unlocked.", exclude: null, fromObj: caller, mapping: new Dictionary<string, object?> { ["target"] = caller });
        return false;
    }
    public bool Unlock(GameObject? caller = null) => caller != null ? TryUnlock(caller) : false;

    // Port of base_door.py:313 map_close
    public void MapClose()
    {
        MapCloseCallCount++;
        var settings = AtherizSettings.Global;
        if (!settings.MapEnabled) settings = AtherizSettings.Default;
        if (!AtherizSettings.Global.MapEnabled || SymbolCoord == null || FromCoord.Equals(default) || ToCoord.Equals(default)) return;
        var mh = MapHandlerHolder.Get();
        if (mh == null) return;
        var seen = new HashSet<(string, int)>();
        foreach (var coord in new[] { FromCoord, ToCoord })
        {
            var key = (coord.Area, coord.Z);
            if (!seen.Add(key)) continue;
            var mi = mh.GetMapInfo(coord.Area, coord.Z);
            if (mi != null)
            {
                mi.Lock.EnterWriteLock();
                try
                {
                    mi.PostGrid[SymbolCoord.Value] = SelectGlyph(ClosedSymbol, settings, true);
                    if (mi.PreGrid.Count > 0) { mi.PreGrid[SymbolCoord.Value] = SelectGlyph(ClosedSymbol, settings, true); mi.MapChanged = true; }
                }
                finally { mi.Lock.ExitWriteLock(); }
                mi.Render(true);
            }
        }
    }
    // Port of base_door.py:331 map_open
    public void MapOpen()
    {
        MapOpenCallCount++;
        var settings = AtherizSettings.Global;
        if (!settings.MapEnabled) settings = AtherizSettings.Default;
        if (!AtherizSettings.Global.MapEnabled || SymbolCoord == null || FromCoord.Equals(default) || ToCoord.Equals(default)) return;
        var mh = MapHandlerHolder.Get();
        if (mh == null) return;
        var seen = new HashSet<(string, int)>();
        foreach (var coord in new[] { FromCoord, ToCoord })
        {
            var key = (coord.Area, coord.Z);
            if (!seen.Add(key)) continue;
            var mi = mh.GetMapInfo(coord.Area, coord.Z);
            if (mi != null)
            {
                mi.Lock.EnterWriteLock();
                try
                {
                    mi.PostGrid[SymbolCoord.Value] = SelectGlyph(OpenSymbol, settings, false);
                    if (mi.PreGrid.Count > 0) { mi.PreGrid[SymbolCoord.Value] = SelectGlyph(OpenSymbol, settings, false); mi.MapChanged = true; }
                }
                finally { mi.Lock.ExitWriteLock(); }
                mi.Render(true);
            }
        }
    }

    private string SelectGlyph(string preset, AtherizSettings settings, bool closed)
    {
        if (!string.IsNullOrEmpty(preset)) return preset;
        // fallback glyph selection via get_dir + settings
        var dir = GameUtils.GetDir(FromCoord, ToCoord);
        bool isEW = dir.Contains("east") || dir.Contains("west");
        bool isNS = dir.Contains("north") || dir.Contains("south");
        if (isNS) return closed ? settings.NsClosedDoor : settings.NsOpenDoor1;
        if (isEW) return closed ? settings.EwClosedDoor : settings.EwOpenDoor1;
        // UD fallback: check Z diff
        if (FromCoord.Z != ToCoord.Z) return closed ? settings.UdClosedDoor : settings.UdOpenDoor;
        return closed ? settings.NsClosedDoor : settings.NsOpenDoor1;
    }

    // Hooks
    public void AtOpen(GameObject? caller) { }
    public void AtClose(GameObject? caller) { }

    // DTO for persistence
    public DoorDto ToDto()
    {
        using (ReadScope())
        {
            return new DoorDto
            {
                FromCoord = FromCoord,
                FromExit = FromExit,
                ToCoord = ToCoord,
                ToExit = ToExit,
                SymbolCoord = SymbolCoord,
                ClosedSymbol = ClosedSymbol,
                OpenSymbol = OpenSymbol,
                Closed = Closed,
                Locked = Locked,
                Name = Name,
                Desc = DoorDesc,
                KeyId = KeyId,
            };
        }
    }
    public static Door FromDto(DoorDto dto)
    {
        var d = new Door(dto.FromCoord, dto.ToCoord, dto.FromExit, dto.ToExit, dto.SymbolCoord, dto.ClosedSymbol, dto.OpenSymbol, dto.Closed, dto.Locked)
        {
            Name = dto.Name ?? dto.FromExit,
            DoorDesc = dto.Desc ?? "",
            KeyId = dto.KeyId,
        };
        return d;
    }
}

public sealed class DoorDto
{
    public Coord FromCoord { get; set; }
    public string FromExit { get; set; } = "";
    public Coord ToCoord { get; set; }
    public string ToExit { get; set; } = "";
    public (int X, int Y)? SymbolCoord { get; set; }
    public string ClosedSymbol { get; set; } = "";
    public string OpenSymbol { get; set; } = "";
    public bool Closed { get; set; } = true;
    public bool Locked { get; set; } = false;
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public int? KeyId { get; set; }
}

public static class MapHandlerHolder
{
    private static MapHandler? _instance;
    private static readonly object _lock = new();
    public static MapHandler? Get() { lock (_lock) return _instance; }
    public static void Set(MapHandler h) { lock (_lock) _instance = h; }
}
