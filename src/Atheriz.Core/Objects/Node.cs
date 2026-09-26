
namespace Atheriz.Core.Objects;

/// <summary>
/// Core semantics: per-entity ReaderWriterLockSlim, IsModified dirty flag, tick support, link management,
/// with on-disk JSON replacing dill.
/// </summary>

public partial class Node : GameObject
{
    // Single lock: Node shares the base SyncRoot (atheriz/objects/nodes.py uses one
    // self.lock; the split _nodeLock caused node->base vs base->node order inversions).
    // NodeLock/Lock are kept as aliases for existing callers (NodeGrid, Pathfind, tests).
    public ReaderWriterLockSlim NodeLock => SyncRoot;
    public ReaderWriterLockSlim Lock => SyncRoot;

    // rewrites Coord under the grid lock while AtHear/GetDisplayName readers
    // run concurrently. Coord is an immutable struct so a locked reference
    // swap is sufficient — no torn coordinates.
    private Coord _coord;
    public Coord Coord
    {
        get => Read(() => _coord);
        set => Write(() => _coord = value);
    }
    public string Theme { get; set; } = "";
    public string? LegendDesc { get; set; }
    // Snapshot copies (same shape as GameObject.Aliases): the getter returns
    // a copy under the read lock, the setter copies under the write lock and
    // marks modified. Callers holding the node lock touch _links/_nouns
    // directly (re-entering the write lock would be safe but wasteful).
    // Null assignment is a bug at the single write point — fail loud pointing
    // at the culprit instead of letting readers null-check per call.
    private List<NodeLink> _links = [];
    public List<NodeLink> Links
    {
        get => Read<List<NodeLink>>(() => [.. _links]);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Write(() => { _links = [.. value]; IsModified = true; });
        }
    }
    private Dictionary<string, string> _nouns = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Nouns
    {
        get => Read(() => new Dictionary<string, string>(_nouns, _nouns.Comparer));
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Write(() => { _nouns = new Dictionary<string, string>(value, value.Comparer); IsModified = true; });
        }
    }
    public double OpenAttenuation { get; set; } = 10.0;
    public double EnclosedAttenuation { get; set; } = 20.0;
    public double AmbientSoundLevel { get; set; } = 5.0;

    // Tick/script state lives in base storage (atheriz/objects/nodes.py shares
    // _tick_seconds/_is_tickable/scripts with Object); Node only adds coro wiring.
    // Kept for save/load compat (NodeHandler.Save reads ScriptsSet).
    public HashSet<int> ScriptsSet => ScriptsSnapshot;

    public override IEnumerable<(string name, object? value, bool isProperty)> GetExamMembers()
    {
        foreach (var m in base.GetExamMembers()) yield return m;
        object? Safe(Func<object?> f) { try { return f(); } catch { return "<error>"; } }
        yield return ("Coord", Safe(() => (object?)Coord), true);
        yield return ("Theme", Safe(() => (object?)Theme), true);
        yield return ("LegendDesc", Safe(() => (object?)LegendDesc), true);
        yield return ("Links", Safe(() => (object?)Links), true);
        yield return ("Nouns", Safe(() => (object?)Nouns), true);
        yield return ("OpenAttenuation", Safe(() => (object?)OpenAttenuation), true);
        yield return ("EnclosedAttenuation", Safe(() => (object?)EnclosedAttenuation), true);
        yield return ("AmbientSoundLevel", Safe(() => (object?)AmbientSoundLevel), true);
    }

    public Node() : this(new Coord("limbo", 0, 0, 0)) { }
    // Load path: initializes defaults WITHOUT consuming an id (leave -1) and
    // WITHOUT publishing to the registry (no phantom). Factories below adopt
    // the stored id before publication.
    internal static Node CreateForLoad(Coord coord) => new Node(coord);
    // Id-adopting overload for the converter load path: fixes the hash
    // snapshot together with the id so the node is publishable on return.
    internal static Node CreateForLoad(int id, Coord coord)
    {
        var n = new Node(coord);
        n.SetIdRaw(id);
        return n;
    }

    // Persisted-subtype registry (mirrors GameObject.RegisterPersistedSubtype):
    // explicit factories replace the Type.GetType + assembly scan + Activator
    // path. Base Node is pre-registered; games register Custom* node types at
    // startup. Unregistered names load as plain nodes via CreateForLoad.
    private static readonly Dictionary<string, Func<Coord, Node>> _persistedSubtypeFactories = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, string> _persistedSubtypeNames = new();
    private static readonly Lock _persistedSubtypeLock = new();
    static Node()
    {
        // FullName is never null for this compiled type; fall back to Name so the
        // compiler does not need a suppression.
        string nodeName = typeof(Node).FullName ?? typeof(Node).Name;
        _persistedSubtypeFactories[nodeName] = c => CreateForLoad(c);
        _persistedSubtypeNames[typeof(Node)] = nodeName;
    }
    public static void RegisterPersistedSubtype(string fullName, Type type, Func<Coord, Node> factory)
    {
        if (string.IsNullOrEmpty(fullName)) throw new ArgumentException("Subtype full name required.", nameof(fullName));
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(factory);
        lock (_persistedSubtypeLock)
        {
            _persistedSubtypeFactories[fullName] = factory;
            // Prune superseded Type keys for this name: holding a Type roots
            // its AssemblyLoadContext, so without this every re-registration
            // pins the previous plugin generation forever.
            foreach (var k in _persistedSubtypeNames.Where(kv => kv.Value == fullName && kv.Key != type).Select(kv => kv.Key).ToList())
                _persistedSubtypeNames.Remove(k);
            _persistedSubtypeNames[type] = fullName;
        }
    }
    internal static string? RegisteredNameFor(Type t)
    {
        lock (_persistedSubtypeLock) { return _persistedSubtypeNames.TryGetValue(t, out var n) ? n : null; }
    }
    internal static bool TryCreatePersistedSubtype(string objectType, Coord coord, out Node? node)
    {
        // Legacy saves store AssemblyQualifiedName; registrations use FullName:
        // strip the ", Assembly..." suffix to normalize (type names never
        // contain a bare comma).
        string key = objectType;
        int comma = key.IndexOf(',');
        if (comma > 0) key = key.Substring(0, comma).Trim();
        // Snapshot the factory under the lock, invoke it outside: factories are
        // game-registered callbacks and must never run while the registry lock
        // is held (a factory creating another persisted subtype would re-enter).
        Func<Coord, Node>? factory = null;
        lock (_persistedSubtypeLock)
        {
            if (!_persistedSubtypeFactories.TryGetValue(key, out factory))
            {
                // Short-name fallback (mirrors the old scan matching x.Name).
                foreach (var kv in _persistedSubtypeFactories)
                {
                    var rkey = kv.Key;
                    var shortName = rkey.Substring(rkey.LastIndexOfAny(['.', '+']) + 1);
                    if (shortName == key) { factory = kv.Value; break; }
                }
            }
        }
        if (factory is not null) { node = factory(coord); return true; }
        // Shared GameObject subtype registry (tests and games register node
        // doubles there, e.g. DummyNode): adopt Node instances from it.
        // Hydration below overwrites the placeholder coord.
        try
        {
            if (Persistence.Converters.GameObjectDtoConverter.TryCreateSubtype(key, out var shared) && shared is Node sharedNode)
            {
                node = sharedNode;
                return true;
            }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.TryCreatePersistedSubtype: " + logEx.Message, "Node"); }
        node = null;
        return false;
    }
    // Shared category and message prefix for suppressed-error logs below.
    private const string LogContext = "Node";
    private Node(Coord coord) : base(-1)
    {
        Coord = coord;
        base.Name = "room";
        Desc = "";
        Theme = "";
        Symbol = "";
        LegendDesc = null;
        Links = [];
        base.TickSeconds = 1.0;
        OpenAttenuation = 10.0;
        EnclosedAttenuation = 20.0;
        AmbientSoundLevel = 5.0;
        IsNode = true;
        IsModified = true;
        // leave Id == -1, do not AddObject
    }
    public Node(Coord coord, string name = "room", string desc = "", string? theme = null, string? symbol = null, string? legendDesc = null, List<NodeLink>? links = null, double tickSeconds = 1.0)
        : this(coord)
    {
        base.Name = name;
        Desc = desc;
        Theme = theme ?? "";
        Symbol = symbol ?? "";
        LegendDesc = legendDesc;
        Links = links ?? [];
        base.TickSeconds = tickSeconds;
        OpenAttenuation = 10.0;
        EnclosedAttenuation = 20.0;
        AmbientSoundLevel = 5.0;
        IsNode = true;
        // Fresh id with a matching hash snapshot: the private core leaves -1
        // and SetIdRaw fixes id + snapshot together (same as load paths).
        SetIdRaw(IdGenerator.GetUniqueId());
        IsModified = true;
        // no publication from the constructor. Python registers in
        // create(), not __init__ (base_obj.py:121+); publishing `this` here
        // leaks half-built subclass instances when a derived ctor throws.
        // Callers register explicitly via ObjectRegistry.AddObject(node).
    }

    // Identity (id equality) lives on GameObject once; see base Equals/GetHashCode.

    public override void AtDesc(GameObject? looker = null) => Hookable(HookName.AtDesc, () => 0, looker);
    public override void AtTick() => Hookable(HookName.AtTick, () => 0);

    public List<GameObject> GetContents()
    {
        // ContentsSnapshot already snapshots under SyncRoot; resolve outside the lock.
        // Node uses base contents; delegate to ObjectRegistry (its Get snapshots
        // internally, so no caller-side ToList; the snapshot is never mutated here).
        return ObjectRegistry.Get(ContentsSnapshot);
    }

    public void ForContents(Action<GameObject> func, IEnumerable<GameObject>? exclude = null)
    {
        var contents = GetContents();
        HashSet<GameObject>? excl = exclude is not null ? new HashSet<GameObject>(exclude) : null;
        foreach (var obj in contents)
        {
            if (excl is not null && excl.Contains(obj)) continue;
            try { func(obj); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed " + LogContext + ".ForContents: " + logEx.Message, LogContext); }
        }
    }

    public override void ResolveRelations()
    {
        // Same containment as the base (GameObject.ResolveRelations wraps AddCoro,
        // InstallHooks and AtInit each in try/log): a throwing ticker or hook
        // must not abort relation resolution for the whole node.
        if (IsTickable)
        {
            try { Globals.GlobalServices.TryGetTicker()?.AddCoro(AtTick, TickSeconds); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.ResolveRelations: " + logEx.Message, "Node"); }
        }
        HashSet<int> scripts = ScriptsSnapshot;
        foreach (var id in scripts)
        {
            if (GameObject.TryGetScript(id, out var s))
            {
                try { s.InstallHooks(this); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.ResolveRelations: " + logEx.Message, "Node"); }
            }
        }
        try { AtInit(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.ResolveRelations: " + logEx.Message, "Node"); }
    }

    public override double TickSeconds
    {
        get => base.TickSeconds;
        set
        {
            // Predicate + mutation share one write hold (SyncRoot is recursive, so
            // the base accessors nest safely). Across separate acquisitions two
            // racing setters both saw stale state and both AddCoro'd,
            // double-firing the ticker.
            SyncRoot.EnterWriteLock();
            try
            {
                double old = base.TickSeconds;
                bool doSwap = IsTickable && value != old;
                base.TickSeconds = value;
                if (doSwap)
                {
                    var at = Globals.GlobalServices.TryGetTicker();
                    at?.RemoveCoro(AtTick, old);
                    at?.AddCoro(AtTick, value);
                }
            }
            finally { SyncRoot.ExitWriteLock(); }
        }
    }

    public override bool IsTickable
    {
        get => base.IsTickable;
        set
        {
            // Same single-hold discipline as TickSeconds above.
            SyncRoot.EnterWriteLock();
            try
            {
                if (base.IsTickable == value) return;
                double tick = base.TickSeconds;
                base.IsTickable = value;
                var at = Globals.GlobalServices.TryGetTicker();
                if (value) at?.AddCoro(AtTick, tick);
                else at?.RemoveCoro(AtTick, tick);
            }
            finally { SyncRoot.ExitWriteLock(); }
        }
    }

    public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreEmitSound(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
        => Hookable<(bool, GameObject, string, string, double, bool)>(HookName.AtPreEmitSound, () => (true, emitter, soundDesc, soundMsg, loudness, isSay), emitter, soundDesc, soundMsg, loudness, isSay);

    public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
        => Hookable<(bool, GameObject, string, string, double, bool)>(HookName.AtPreHear, () => (true, emitter, soundDesc, soundMsg, loudness, isSay), emitter, soundDesc, soundMsg, loudness, isSay);

// Node propagation, overrides GameObject.AtHear (player hearing is separate)
    public override double AtHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        return Hookable(HookName.AtHear, () =>
        {
            var (allow, em2, sd2, sm2, loud2, isSay2) = AtPreHear(emitter, soundDesc, soundMsg, loudness, isSay);
        bool open = false;
        var nh = NodeHandler.GetCurrent();
        Dictionary<string, Door>? doors = null;
        try { doors = nh?.GetDoors(Coord); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed " + LogContext + ".AtHear: " + logEx.Message, LogContext); }
        if (doors is not null && doors.Count > 0)
        {
            foreach (var d in doors.Values) { if (!d.Closed) { open = true; break; } }
        }
        else open = true;
        double attenuation = open ? OpenAttenuation : EnclosedAttenuation;
        if (!allow || loud2 <= AmbientSoundLevel) return loud2 - attenuation;
        var contents = GetContents();
        foreach (var o in contents)
        {
            if (!o.CanHear) continue;
            try
            {
                var pre = o.AtPreHear(em2, sd2, sm2, loud2, isSay2);
                if (!pre.ok) continue;
                o.AtHear(pre.emitter, pre.desc, pre.msg, pre.loudness, pre.isSay);
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed " + LogContext + ".AtHear: " + logEx.Message, LogContext); }
        }
            return loud2 - attenuation;
        }, emitter, soundDesc, soundMsg, loudness, isSay);
    }

    public override bool AtPreObjectLeave(GameObject? destination, string? toExit = null)
    {
        return Hookable(HookName.AtPreObjectLeave, () => true, destination, toExit);
    }
    public override void AtObjectLeave(GameObject? destination, string? toExit = null)
    {
        Hookable(HookName.AtObjectLeave, () => 0, destination, toExit);
    }
    public override bool AtPreObjectReceive(GameObject? source, string? fromExit = null)
    {
        return Hookable(HookName.AtPreObjectReceive, () => true, source, fromExit);
    }
    public override void AtObjectReceive(GameObject? source, string? fromExit = null)
    {
        Hookable(HookName.AtObjectReceive, () => 0, source, fromExit);
    }
    public override void AtInit() => Hookable(HookName.AtInit, () => 0);

    // Node walks are uncapped by design (contents move home or die — nothing
    // detaches as a depth-capped survivor the way the base walk does);
    // maxDepth threads through to nested object deletes below.
    public override (int Count, List<Persistence.Dto.DeleteOperation> Operations)? Delete(GameObject? caller, bool recursive = false, int maxDepth = ContentUtils.DefaultMaxSearchDepth)
    {
        (List<Persistence.Dto.DeleteOperation> ops, int count) execDeleteRecursive(Node obj)
        {
            List<Persistence.Dto.DeleteOperation> allOps = [];
            int count = 0;
            HashSet<int> seen = [];
            var contents = obj.GetContents();
            foreach (var content in contents)
            {
                if (!seen.Add(content.Id)) continue;
                var res = content.Delete(caller, true, maxDepth);
                if (res is null) continue;
                allOps.AddRange(res.Value.Operations);
                count += res.Value.Count;
            }
            return (allOps, count);
        }
        (List<Persistence.Dto.DeleteOperation> ops, int count) execMoveContents(Node obj)
        {
            List<Persistence.Dto.DeleteOperation> allOps = [];
            int count = 0;
            var contents = obj.GetContents();
            foreach (var content in contents)
            {
                bool moved = false;
                var homeRef = content.Home;
                GameObject? homeObj = null;
                if (homeRef is Persistence.Dto.LocationRef.ObjectLocation ol)
                {
                    homeObj = ObjectRegistry.GetSingle(ol.ObjectId);
                }
                else if (homeRef is Persistence.Dto.LocationRef.CoordLocation cl)
                {
                    // Index-first with scan fallback (same node by construction).
                    homeObj = ObjectRegistry.FindNodeByCoord(cl.Coord);
                }
                GameObject? fallback = null;
                if (caller is not null)
                {
                    // collapsed single expression (the three-way
                    // if/else assigned loc in exactly one case).
                    var loc = caller.ResolveLocationObject();
                    fallback = (loc is not null && !ReferenceEquals(loc, obj)) ? loc : null;
                }
                if (homeObj is not null)
                {
                    try { if (content.MoveTo(homeObj, force: false, announce: false)) moved = true; } catch { moved = false; }
                    if (!moved && fallback is not null)
                    {
                        try { if (content.MoveTo(fallback, force: true, announce: false)) moved = true; } catch { moved = false; }
                    }
                }
                else if (fallback is not null)
                {
                    try { if (content.MoveTo(fallback, force: true, announce: false)) moved = true; } catch { moved = false; }
                }
                if (!moved)
                {
                    if (ReferenceEquals(content.ResolveLocationObject(), obj))
                    {
                        try { obj.RemoveObject(content); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed " + LogContext + ".Delete: " + logEx.Message, LogContext); }
                        try { content.Location = Persistence.Dto.LocationRef.NullLocation.Instance; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed " + LogContext + ".Delete: " + logEx.Message, LogContext); }
                    }
                    var res = content.Delete(caller, true, maxDepth);
                    if (res is not null) { allOps.AddRange(res.Value.Operations); count += res.Value.Count; }
                }
            }
            return (allOps, count);
        }
        void execSelfDelete()
        {
            if (IsTickable)
            {
                try { Globals.GlobalServices.TryGetTicker()?.RemoveCoro(AtTick, TickSeconds); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed " + LogContext + ".Delete: " + logEx.Message, LogContext); }
            }
            try { NodeHandler.GetCurrent()?.RemoveNode(Coord); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed " + LogContext + ".Delete: " + logEx.Message, LogContext); }
        }
        if (caller is not null && !AtDelete(caller)) return null;
        SyncRoot.EnterWriteLock();
        try
        {
            if (IsDeleted) return null;
            IsDeleted = true;
        }
        finally { SyncRoot.ExitWriteLock(); }
        var (ops, kids) = recursive ? execDeleteRecursive(this) : execMoveContents(this);
        execSelfDelete();
        // Self teardown: the kids-only collection above never
        // emits the node's own delete op, detaches its followers/channel
        // subscriptions/session, or unregisters a handler-less node (which
        // RemoveNode never sees). RemoveObject is a same-thread idempotent
        // remove when the handler already unregistered it.
        if (!IsTemporary)
        {
            ops.Add(GetDeleteOperation());
            // Journal the row death so the checkpoint drain removes it.
            ObjectRegistry.NoteDeleted(Id);
        }
        ObjectRegistry.RemoveObject(this);
        TeardownDeleted(this);
        return (1 + kids, ops);
    }

    public override bool AtDelete(GameObject? caller)
    {
        return Hookable(HookName.AtDelete, () =>
        {
            if (!Access(caller, "delete"))
            {
                try { caller?.Msg($"You cannot delete {GetDisplayName(caller)}."); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed " + LogContext + ".AtDelete: " + logEx.Message, LogContext); }
                return false;
            }
            return true;
        }, caller);
    }

}
