
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Objects;

/// <summary>
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
    // Paired endpoint publish: the two setters above are single-field (kept for
    // callers that move one end), but a GetNodes snapshot landing between two
    // separate sets pairs endpoints from different generations — no read-side
    // hold can repair that. Writers moving both ends publish via this one hold.
    public void SetEndpoints(Coord from, Coord to)
    {
        bool changed;
        using (WriteScope())
        {
            changed = !_fromCoord.Equals(from) || !_toCoord.Equals(to);
            _fromCoord = from; _toCoord = to;
        }
        if (changed) MarkNodeDoorsModified();
    }
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
            if (nh is null) return;
            nh.Lock3.EnterWriteLock();
            try { nh.MarkDoorsModified(); } finally { nh.Lock3.ExitWriteLock(); }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Door.MarkNodeDoorsModified: " + logEx.Message, "Door"); }
    }

    private readonly Dictionary<string, List<LockEntry>> _locks = new();
    // One lock-table row per entry (mirrors GameObject._locks): the policy is
    // what persists in DoorDto, the predicate is what decides. Bare-lambda
    // "custom" entries are kept in memory but dropped on save with a loud log.

    // Magic mapping key for door announces + per-call construction helper. Each
    // call site still gets its own dict instance (MsgContents copies-then-mutates
    // per call, so sharing one instance would alias); only construction is shared.
    private const string TargetKey = "target";
    private static Dictionary<string, object?> TargetMapping(GameObject caller)
        => new() { [TargetKey] = caller };

    public Door() { }
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
        if (fromCoord is not null) d._fromCoord = fromCoord.Value;
        if (toCoord is not null) d._toCoord = toCoord.Value;
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

    public void AddLock(string name, Func<GameObject, bool> pred)
        => AddLock(name, pred, LockPolicies.Custom);
    public void AddLock(string name, Func<GameObject, bool> pred, string policy)
    {
        using (WriteScope())
        {
            if (!_locks.TryGetValue(name, out var lst)) { lst = []; _locks[name] = lst; }
            lst.Add(new LockEntry(LockPolicies.Classify(policy), pred));
        }
    }
    public void AddLock(string name, Func<GameObject, bool> pred, LockPolicies.LockPolicy policy)
        => AddLock(name, pred, LockPolicies.Name(policy));
    public bool Access(GameObject? caller, string lockName)
    {
        if (caller is null) return false;
        if (caller.IsSuperUser) return true;
        List<LockEntry> snap;
        using (ReadScope())
        {
            if (!_locks.TryGetValue(lockName, out var lst) || lst.Count == 0) return true; snap = [.. lst];
        }
        foreach (var entry in snap)
        {
            bool ok;
            try { ok = entry.Predicate(caller); }
            catch { return false; }
            if (!ok) return false;
        }
        return true;
    }
    public bool CanOpen(GameObject? caller) => Access(caller, "open");
    public bool CanClose(GameObject? caller) => Access(caller, "close");
    public bool CanLock(GameObject? caller) => Access(caller, "lock");
    public bool CanUnlock(GameObject? caller) => Access(caller, "unlock");

    public override string ToString() => $"Door({FromCoord}, 'from_exit':{FromExit}, 'to_coord':{ToCoord}, 'to_exit':{ToExit})";

    public string Desc(Coord fromCoord)
    {
        using (ReadScope())
        {
            var status = _closed ? "A closed" : "An open";
            string text;
            if (fromCoord.Equals(_fromCoord)) text = $"{status} door leading {_fromExit}";
            else if (fromCoord.Equals(_toCoord)) text = $"{status} door leading {_toExit}";
            else return "Door desc: unexpected coord.";
            // DoorDesc is builder flavor persisted with the door (never
            // rendered until now): appended verbatim when set.
            if (!string.IsNullOrEmpty(_doorDesc)) text += " " + _doorDesc;
            return text;
        }
    }

    public (Node? fromNode, Node? toNode) GetNodes()
    {
        var nh = NodeHandler.GetCurrent();
        // Snapshot both endpoints under one read hold: resolving them via two
        // separate property reads admits a coord setter between them and pairs
        // nodes from different generations. Resolution itself stays outside the
        // hold (it takes handler locks).
        Coord from, to;
        using (ReadScope()) { from = _fromCoord; to = _toCoord; }
        Node? fromNode = null, toNode = null;
        if (nh is not null)
        {
            fromNode = nh.GetNode(from);
            toNode = nh.GetNode(to);
        }
        return (fromNode, toNode);
    }

    public virtual bool TryOpen(GameObject caller)
    {
        var (fromNode, toNode) = GetNodes();
        var loc = caller.ResolveLocationObject();
        // Lock order handler -> object: access predicates are game code that may
        // take other locks (incl. the handler door lock RemapDoors holds), so they
        // run BEFORE the door write lock. A lock added in the gap admits one stale
        // open; Python has no such atomicity either.
        bool canAccess = Access(caller, "open");
        string status;
        _lock.EnterWriteLock();
        try
        {
            if (!Closed) status = "already_open";
            else if (Locked) status = "locked";
            else if (!canAccess) status = "no_access";
            else { _closed = false; status = "opened"; }
        }
        finally { _lock.ExitWriteLock(); }
        if (status == "opened")
        {
            MarkNodeDoorsModified();
        }
        if (status == "already_open")
        {
            // Loc-only like already_closed: the idempotent open is no news on
            // the far side (every other idempotent path is loc-only too).
            loc?.MsgContents($"$You(target) $conj(open) the already open door just to be sure.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            return true;
        }
        if (status == "locked")
        {
            loc?.MsgContents($"$You(target) $conj(try) to open the door, but it won't budge.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            return false;
        }
        if (status == "no_access")
        {
            loc?.MsgContents($"$You(target) $conj(try) to open the door, but an unknown force prevents it.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            return false;
        }
        // The flip above already fired the dirty mark, so a throw in the map
        // paint, the announce, or the hook must not unwind the open: contain it.
        try
        {
            MapOpen();
            loc?.MsgContents($"$You(target) $conj(open) the door.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            AtOpen(caller);
        }
        catch (Exception ex) { AtherizLogger.LogDebug("Suppressed Door.TryOpen post-open: " + ex.Message, "Door"); }
        return true;
    }
    // A null caller bypasses access/map/hooks, so the fallback is an explicit
    // ForceOpen explicitly; the no-arg form stays for compat.
    public bool Open(GameObject? caller = null) => caller is not null ? TryOpen(caller) : ForceOpen();
    public bool ForceOpen()
    {
        bool opened;
        _lock.EnterWriteLock();
        // Idempotent open is success even when locked: the state already matches.
        // A locked *shut* door still refuses (the lock guards the transition, not the state).
        try { if (!_closed) return true; if (_locked) return false; _closed = false; opened = true; }
        finally { _lock.ExitWriteLock(); }
        if (opened) MarkNodeDoorsModified();
        return true;
    }

    public virtual bool TryClose(GameObject caller)
    {
        var (fromNode, toNode) = GetNodes();
        var loc = caller.ResolveLocationObject();
        // Access predicates run before the door write lock (see TryOpen).
        bool canAccess = Access(caller, "close");
        string status;
        _lock.EnterWriteLock();
        try
        {
            if (Closed) status = "already_closed";
            else if (!canAccess) status = "no_access";
            else { _closed = true; status = "closed"; }
        }
        finally { _lock.ExitWriteLock(); }
        if (status == "closed")
        {
            MarkNodeDoorsModified();
        }
        if (status == "already_closed")
        {
            loc?.MsgContents($"$You(target) $conj(try) to close the door, but it is already closed.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            // Idempotent close is success: the door state already matches what was wanted.
            return true;
        }
        if (status == "no_access")
        {
            fromNode?.MsgContents($"$You(target) $conj(try) to close the door, but an unknown force prevents it.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            toNode?.MsgContents($"$You(target) $conj(try) to close the door, but an unknown force prevents it.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            return false;
        }
        // Same containment as the open path: the flip and dirty mark already
        // happened, so map/announce/hook failures are logged, not propagated.
        try
        {
            MapClose();
            fromNode?.MsgContents($"$You(target) $conj(close) the door.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            toNode?.MsgContents($"$You(target) $conj(close) the door.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            AtClose(caller);
        }
        catch (Exception ex) { AtherizLogger.LogDebug("Suppressed Door.TryClose post-close: " + ex.Message, "Door"); }
        return true;
    }
    public bool Close(GameObject? caller = null) => caller is not null ? TryClose(caller) : ForceClose();
    public bool ForceClose()
    {
        bool closed;
        _lock.EnterWriteLock();
        // Idempotent close is success: the door state already matches what was wanted.
        try { if (_closed) return true; _closed = true; closed = true; }
        finally { _lock.ExitWriteLock(); }
        if (closed) MarkNodeDoorsModified();
        return true;
    }

    public virtual bool TryLock(GameObject caller)
    {
        var loc = caller.ResolveLocationObject();
        // Access predicates run before the door write lock (see TryOpen).
        bool canAccess = Access(caller, "lock");
        // A keyed door needs its key on the caller: null KeyId means no key.
        int? keyId;
        using (ReadScope()) { keyId = _keyId; }
        bool hasKey = keyId is null || caller.ContentsSnapshot.Contains(keyId.Value);
        string status;
        _lock.EnterWriteLock();
        try
        {
            if (!canAccess) status = "no_access";
            else if (!hasKey) status = "no_key";
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
            loc?.MsgContents($"$You(target) $conj(try) to lock the door, but an unknown force prevents it.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            return false;
        }
        if (status == "no_key")
        {
            loc?.MsgContents($"$You(target) $conj(try) to lock the door, but you lack the key.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            return false;
        }
        if (status == "not_closed")
        {
            loc?.MsgContents($"$You(target) $conj(try) to lock the door, but You can't lock an open door.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            return false;
        }
        if (status == "already_locked")
        {
            loc?.MsgContents($"$You(target) $conj(try) to lock the door, but it is already locked.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            return false;
        }
        loc?.MsgContents($"$You(target) $conj(lock) the door.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
        return true;
    }
    public bool LockDoor(GameObject? caller = null) => caller is not null ? TryLock(caller) : false;

    public virtual bool TryUnlock(GameObject caller)
    {
        var (fromNode, toNode) = GetNodes();
        var loc = caller.ResolveLocationObject();
        // Access predicates run before the door write lock (see TryOpen).
        bool canAccess = Access(caller, "unlock");
        // Same key gate as TryLock: unlocking a keyed door needs its key.
        int? keyId;
        using (ReadScope()) { keyId = _keyId; }
        bool hasKey = keyId is null || caller.ContentsSnapshot.Contains(keyId.Value);
        string status;
        _lock.EnterWriteLock();
        try
        {
            if (!canAccess) status = "no_access";
            else if (!hasKey) status = "no_key";
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
            loc?.MsgContents($"$You(target) $conj(try) to unlock the door, but an unknown force prevents it.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            return false;
        }
        if (status == "no_key")
        {
            loc?.MsgContents($"$You(target) $conj(try) to unlock the door, but you lack the key.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            return false;
        }
        if (status == "unlocked")
        {
            loc?.MsgContents($"$You(target) $conj(unlock) the door.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
            return true;
        }
        loc?.MsgContents($"$You(target) $conj(try) to unlock the door, but it is already unlocked.", exclude: null, fromObj: caller, mapping: TargetMapping(caller));
        return false;
    }
    public bool Unlock(GameObject? caller = null) => caller is not null ? TryUnlock(caller) : false;

    public virtual void MapClose() => MapPaint(ClosedSymbol, closed: true);
    public virtual void MapOpen() => MapPaint(OpenSymbol, closed: false);

    // Shared paint body for MapClose/MapOpen: the blocks differ only in the
    // preset symbol + closed flag under the same gate/seen/render shape.
    private void MapPaint(string preset, bool closed)
    {
        // (The old Default fallback + second Global check made the fallback dead.)
        var settings = AtherizSettings.Global;
        if (!settings.MapEnabled || SymbolCoord is null || FromCoord.Equals(default) || ToCoord.Equals(default)) return;
        var mh = MapHandlerSingleton.Get();
        if (mh is null) return;
        HashSet<(string, int)> seen = [];
        foreach (var coord in new[] { FromCoord, ToCoord })
        {
            var key = (coord.Area, coord.Z);
            if (!seen.Add(key)) continue;
            var mi = mh.GetMapInfo(coord.Area, coord.Z);
            if (mi is not null)
            {
                mi.PaintSymbol(SymbolCoord.Value, SelectGlyph(preset, settings, closed));
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
                Locks = _locks.Select(kv => new LockDefDto
                {
                    Name = kv.Key,
                    Policies = kv.Value.Select(e => e.Policy).ToList(),
                }).ToList(),
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
        foreach (var ld in dto.Locks ?? [])
        {
            if (ld is null || string.IsNullOrEmpty(ld.Name)) continue;
            foreach (var pol in ld.Policies ?? [])
            {
                if (pol == LockPolicies.LockPolicy.Custom)
                {
                    // Ad-hoc lambda: no declarative policy was ever persisted
                    // (documented engine-wide contract, mirrored by GameObject
                    // lock restore) — dropped with a loud log, never executed.
                    Atheriz.Core.AtherizLogger.LogError($"Dropping unpersistable 'custom' lambda on door lock '{ld.Name}'; access allowed.");
                }
                else if (LockPolicies.TryResolve(pol, out var pred))
                    d.AddLock(ld.Name, pred, pol);
                else
                {
                    // Fail closed — a NAMED but unresolvable policy (e.g. a
                    // target-bound pc-view/not-self/puppet-owner entry that
                    // only the 2-arg resolver binds, which Door must not use
                    // with a dummy target) must deny, not vanish:
                    // Door.Access returns true for missing entries, so dropping
                    // the entry escalates to allow. Store the Denied marker so
                    // a re-save preserves the entry (still denying).
                    if (pol != LockPolicies.LockPolicy.Denied)
                        Atheriz.Core.AtherizLogger.LogError($"Unknown lock policy '{pol}' on door lock '{ld.Name}'; denying access.");
                    d.AddLock(ld.Name, _ => false, LockPolicies.LockPolicy.Denied);
                }
            }
        }
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
    // Persisted lock policies as typed LockDefDto rows (mirrors GameObject
    // Locks; bare-lambda "custom" entries are dropped with a loud log).
    public List<LockDefDto> Locks { get; set; } = [];
}
