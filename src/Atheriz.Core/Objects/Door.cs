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
    // Mirrors Python's RLock on Door (base_door.py:24 self.lock = RLock()):
    // Try* methods hold the write lock across Access (read lock), and tests set
    // state under an explicit write lock, so recursion is required, not incidental.
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    public ReaderWriterLockSlim SyncRoot => _lock;
    // Compat: keep public Lock for Ported tests (delegates to the single _lock);
    // new code should use SyncRoot/ReadScope/WriteScope.
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
    // Door state lives outside ObjectRegistry, so direct assignment used to be lost
    // on save (only Try* paths marked doors modified). Every mutating setter below
    // takes the lock, and any change marks the NodeHandler doors section modified
    // (after releasing the door lock, so the order is always door -> handler).
    private Coord _fromCoord;
    private string _fromExit = "";
    private Coord _toCoord;
    private string _toExit = "";
    private (int X, int Y)? _symbolCoord;
    private string _closedSymbol = "";
    private string _openSymbol = "";
    private bool _closed = true;
    private bool _locked = false;
    private string _name = "";
    private string _doorDesc = "";
    private int? _keyId;
    public Coord FromCoord { get => ReadProp(ref _fromCoord); set => SetProp(ref _fromCoord, value); }
    public string FromExit { get => ReadProp(ref _fromExit); set => SetProp(ref _fromExit, value ?? ""); }
    public Coord ToCoord { get => ReadProp(ref _toCoord); set => SetProp(ref _toCoord, value); }
    public string ToExit { get => ReadProp(ref _toExit); set => SetProp(ref _toExit, value ?? ""); }
    public (int X, int Y)? SymbolCoord { get => ReadProp(ref _symbolCoord); set => SetProp(ref _symbolCoord, value); }
    public string ClosedSymbol { get => ReadProp(ref _closedSymbol); set => SetProp(ref _closedSymbol, value ?? ""); }
    public string OpenSymbol { get => ReadProp(ref _openSymbol); set => SetProp(ref _openSymbol, value ?? ""); }
    public bool Closed { get => ReadProp(ref _closed); set => SetProp(ref _closed, value); }
    public bool Locked { get => ReadProp(ref _locked); set => SetProp(ref _locked, value); }
    public string Name { get => ReadProp(ref _name); set => SetProp(ref _name, value ?? ""); }
    public string DoorDesc { get => ReadProp(ref _doorDesc); set => SetProp(ref _doorDesc, value ?? ""); }
    public int? KeyId { get => ReadProp(ref _keyId); set => SetProp(ref _keyId, value); }
    private T ReadProp<T>(ref T field)
    {
        _lock.EnterReadLock();
        try { return field; }
        finally { _lock.ExitReadLock(); }
    }
    private void SetProp<T>(ref T field, T value)
    {
        bool changed;
        _lock.EnterWriteLock();
        try
        {
            changed = !EqualityComparer<T>.Default.Equals(field, value);
            if (changed) field = value;
        }
        finally { _lock.ExitWriteLock(); }
        if (changed) MarkNodeDoorsModified();
    }
    /// <summary>
    /// Best-effort doors-modified mark (mirrors the Try* paths below and Python's
    /// <c>try: nh.mark_doors_modified() except Exception: pass</c>). Lock-free when
    /// no NodeHandler is current (e.g. unit tests, FromDto).
    /// </summary>
    private static void MarkNodeDoorsModified()
    {
        try
        {
            var nh = NodeHandler.GetCurrent();
            if (nh == null) return;
            nh.Lock3.EnterWriteLock();
            try { nh.MarkDoorsModified(); } finally { nh.Lock3.ExitWriteLock(); }
        }
        catch { }
    }

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
        // Direct field init: a fresh door must not mark the handler dirty.
        _fromCoord = from; _toCoord = to; _fromExit = fromExit; _toExit = toExit;
        _symbolCoord = symbolCoord; _closedSymbol = closedSymbol; _openSymbol = openSymbol;
        _closed = closed; _locked = locked;
        _name = fromExit;
        _doorDesc = "";
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
        if (fromCoord != null) d._fromCoord = fromCoord.Value;
        if (toCoord != null) d._toCoord = toCoord.Value;
        d._fromExit = fromExit ?? "";
        d._toExit = toExit ?? "";
        d._symbolCoord = symbolCoord;
        d._closedSymbol = closedSymbol ?? "";
        d._openSymbol = openSymbol ?? "";
        d._closed = closed;
        d._locked = locked;
        d._name = fromExit ?? "";
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
            var status = _closed ? "A closed" : "An open";
            if (fromCoord.Equals(_fromCoord)) return $"{status} door leading {_fromExit}";
            if (fromCoord.Equals(_toCoord)) return $"{status} door leading {_toExit}";
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
            else { _closed = false; status = "opened"; }
        }
        finally { _lock.ExitWriteLock(); }
        if (status == "opened")
        {
            // Port of base_door.py:119-124 try/except around mark_doors_modified
            MarkNodeDoorsModified();
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
    // Port of base_door.py:106 wrapper for spec.
    // A null caller bypasses access/map/hooks, so the fallback is an explicit
    // ForceOpen (audit F007); the no-arg form stays for compat.
    public bool Open(GameObject? caller = null) => caller != null ? TryOpen(caller) : ForceOpen();
    public bool ForceOpen()
    {
        _lock.EnterWriteLock();
        try { if (_locked) return false; if (!_closed) return true; _closed = false; return true; }
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
            else { _closed = true; status = "closed"; }
        }
        finally { _lock.ExitWriteLock(); }
        if (status == "closed")
        {
            MarkNodeDoorsModified();
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
    public bool Close(GameObject? caller = null) => caller != null ? TryClose(caller) : ForceClose();
    public bool ForceClose()
    {
        _lock.EnterWriteLock();
        try { if (_closed) return false; _closed = true; return true; }
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
            else { _locked = true; status = "locked"; }
        }
        finally { _lock.ExitWriteLock(); }
        if (status == "locked")
        {
            MarkNodeDoorsModified();
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
            else if (_locked) { _locked = false; status = "unlocked"; }
            else status = "already_unlocked";
        }
        finally { _lock.ExitWriteLock(); }
        if (status == "unlocked")
        {
            MarkNodeDoorsModified();
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
        // Port of base_door.py map_close gate: settings.MAP_ENABLED only.
        // (The old Default fallback + second Global check made the fallback dead.)
        var settings = AtherizSettings.Global;
        if (!settings.MapEnabled || SymbolCoord == null || FromCoord.Equals(default) || ToCoord.Equals(default)) return;
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
        // Port of base_door.py map_open gate: settings.MAP_ENABLED only (see MapClose).
        var settings = AtherizSettings.Global;
        if (!settings.MapEnabled || SymbolCoord == null || FromCoord.Equals(default) || ToCoord.Equals(default)) return;
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
                FromCoord = _fromCoord,
                FromExit = _fromExit,
                ToCoord = _toCoord,
                ToExit = _toExit,
                SymbolCoord = _symbolCoord,
                ClosedSymbol = _closedSymbol,
                OpenSymbol = _openSymbol,
                Closed = _closed,
                Locked = _locked,
                Name = _name,
                Desc = _doorDesc,
                KeyId = _keyId,
            };
        }
    }
    public static Door FromDto(DoorDto dto)
    {
        var d = new Door(dto.FromCoord, dto.ToCoord, dto.FromExit, dto.ToExit, dto.SymbolCoord, dto.ClosedSymbol, dto.OpenSymbol, dto.Closed, dto.Locked);
        // Direct field restore: a loaded door must not mark the handler dirty.
        d._name = dto.Name ?? dto.FromExit;
        d._doorDesc = dto.Desc ?? "";
        d._keyId = dto.KeyId;
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
