using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Utils;
using Atheriz.Core.Commands;

namespace Atheriz.Core.Objects;

/// <summary>
/// Faithful port of <c>atheriz/objects/nodes.py:Node</c> + <c>NodeGrid</c> + <c>NodeArea</c> + <c>Transition</c>.
/// Core semantics: per-entity ReaderWriterLockSlim, IsModified dirty flag, tick support, link management,
/// with on-disk JSON replacing dill.
/// </summary>

public sealed class NodeLink
{
    public string Name { get; set; } = "";
    public Coord Coord { get; set; }
    public List<string> Aliases { get; set; } = [];
    public NodeLink() { }
    public NodeLink(string name, Coord coord, List<string>? aliases = null)
    {
        Name = name;
        Coord = coord;
        Aliases = aliases ?? [];
    }
    // Port of nodes.py:63-66 __eq__: name+coord only (aliases excluded).
    public override bool Equals(object? obj) => obj is NodeLink o && Name == o.Name && Coord.Equals(o.Coord);
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Name);
        h.Add(Coord);
        return h.ToHashCode();
    }
    public override string ToString() => $"NodeLink: {Name}, [{string.Join(",", Aliases)}], {Coord}";
}

// Port of atheriz/objects/nodes.py:79
public partial class Node : GameObject
{
    public new static bool _is_thread_safe = true;
    // Single lock: Node shares the base SyncRoot (atheriz/objects/nodes.py uses one
    // self.lock; the split _nodeLock caused node->base vs base->node order inversions).
    // NodeLock/Lock are kept as aliases for existing callers (NodeGrid, Pathfind, tests).
    public ReaderWriterLockSlim NodeLock => SyncRoot;
    public ReaderWriterLockSlim Lock => SyncRoot;

    // Port of atheriz/objects/nodes.py:122. Lock-guarded : ApplyMoves
    // rewrites Coord under the grid lock while AtHear/GetDisplayName readers
    // run concurrently. Coord is an immutable struct so a locked reference
    // swap is sufficient — no torn coordinates.
    private Coord _coord;
    public Coord Coord
    {
        get { SyncRoot.EnterReadLock(); try { return _coord; } finally { SyncRoot.ExitReadLock(); } }
        set { SyncRoot.EnterWriteLock(); try { _coord = value; } finally { SyncRoot.ExitWriteLock(); } }
    }
    public string Theme { get; set; } = "";
    public string? LegendDesc { get; set; }
    public List<NodeLink> Links { get; set; } = [];
    public Dictionary<string, string> Nouns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double OpenAttenuation { get; set; } = 10.0; // Port of nodes.py:134 DEFAULT_OPEN_SOUND_ATTENUATION
    public double EnclosedAttenuation { get; set; } = 20.0; // Port of nodes.py:135 DEFAULT_ENCLOSED_SOUND_ATTENUATION
    public double AmbientSoundLevel { get; set; } = 5.0; // Port of nodes.py:136

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
    // WITHOUT publishing to the registry (no phantom). Caller must SetIdRaw +
    // explicit AddObject (via swap/second-phase) after filling fields.
    internal static Node CreateForLoad(Coord coord)
    {
        var n = new Node(NoIdMarker.Instance, coord);
        return n;
    }

    // Persisted-subtype registry (mirrors GameObject.RegisterPersistedSubtype):
    // explicit factories replace the Type.GetType + assembly scan + Activator
    // path. Base Node is pre-registered; games register Custom* node types at
    // startup. Unregistered names load as plain nodes via CreateForLoad.
    private static readonly Dictionary<string, Func<Coord, Node>> _persistedSubtypeFactories = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, string> _persistedSubtypeNames = new();
    private static readonly object _persistedSubtypeLock = new();
    static Node() { _persistedSubtypeFactories[typeof(Node).FullName!] = c => CreateForLoad(c); _persistedSubtypeNames[typeof(Node)] = typeof(Node).FullName!; }
    public static void RegisterPersistedSubtype(string fullName, Type type, Func<Coord, Node> factory)
    {
        if (string.IsNullOrEmpty(fullName)) throw new ArgumentException("Subtype full name required.", nameof(fullName));
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        lock (_persistedSubtypeLock) { _persistedSubtypeFactories[fullName] = factory; _persistedSubtypeNames[type] = fullName; }
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
        lock (_persistedSubtypeLock)
        {
            if (_persistedSubtypeFactories.TryGetValue(key, out var f)) { node = f(coord); return true; }
            // Short-name fallback (mirrors the old scan matching x.Name).
            foreach (var kv in _persistedSubtypeFactories)
            {
                var rkey = kv.Key;
                var shortName = rkey.Substring(rkey.LastIndexOfAny(['.', '+']) + 1);
                if (shortName == key) { node = kv.Value(coord); return true; }
            }
        }
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
    private sealed class NoIdMarker
    {
        public static readonly NoIdMarker Instance = new();
        private NoIdMarker() { }
    }
    private Node(NoIdMarker _, Coord coord)
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
    // Port of nodes.py:122
    public Node(Coord coord, string name = "room", string desc = "", string? theme = null, string? symbol = null, string? legendDesc = null, List<NodeLink>? links = null, double tickSeconds = 1.0)
    {
        Coord = coord;
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
        IsModified = true;
        if (Id == -1) Id = IdGenerator.GetUniqueId();
        // no publication from the constructor. Python registers in
        // create(), not __init__ (base_obj.py:121+); publishing `this` here
        // leaks half-built subclass instances when a derived ctor throws.
        // Callers register explicitly via ObjectRegistry.AddObject(node).
    }

    // Identity (id equality) lives on GameObject once; see base Equals/GetHashCode.

    // Port of nodes.py:95
    public override void AtDesc(GameObject? looker = null) => Hookable("at_desc", () => 0, looker);
    // Port of nodes.py:101
    public override void AtTick() => Hookable("at_tick", () => 0);

    // Port of nodes.py:108
    public List<GameObject> GetContents()
    {
        // ContentsSnapshot already snapshots under SyncRoot; resolve outside the lock.
        // Node uses base contents; delegate to ObjectRegistry
        return ObjectRegistry.Get(ContentsSnapshot.ToList());
    }

    // Port of nodes.py:113
    public void ForContents(Action<GameObject> func, IEnumerable<GameObject>? exclude = null)
    {
        var contents = GetContents();
        HashSet<GameObject>? excl = exclude != null ? new HashSet<GameObject>(exclude) : null;
        foreach (var obj in contents)
        {
            if (excl != null && excl.Contains(obj)) continue;
            try { func(obj); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NoIdMarker.ForContents: " + logEx.Message, "NoIdMarker"); }
        }
    }

    // Port of nodes.py:189
    public override void ResolveRelations()
    {
        // Same containment as the base (GameObject.ResolveRelations wraps AddCoro,
        // InstallHooks and AtInit each in try/log): a throwing ticker or hook
        // must not abort relation resolution for the whole node.
        if (IsTickable)
        {
            try { GlobalTickerHolder.Get()?.AddCoro(AtTick, TickSeconds); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.ResolveRelations: " + logEx.Message, "Node"); }
        }
        HashSet<int> scripts = ScriptsSnapshot;
        foreach (var id in scripts)
        {
            var objs = ObjectRegistry.Get(id);
            if (objs.Count > 0 && objs[0] is Script s)
            {
                try { s.InstallHooks(this); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.ResolveRelations: " + logEx.Message, "Node"); }
            }
        }
        try { AtInit(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.ResolveRelations: " + logEx.Message, "Node"); }
    }

    // Port of nodes.py:206 (state in base storage; Node adds ticker swap)
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
                    var at = GlobalTickerHolder.Get();
                    at?.RemoveCoro(AtTick, old);
                    at?.AddCoro(AtTick, value);
                }
            }
            finally { SyncRoot.ExitWriteLock(); }
        }
    }

    // Port of nodes.py:227 (state in base storage; Node adds ticker add/remove)
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
                var at = GlobalTickerHolder.Get();
                if (value) at?.AddCoro(AtTick, tick);
                else at?.RemoveCoro(AtTick, tick);
            }
            finally { SyncRoot.ExitWriteLock(); }
        }
    }

    // Port of nodes.py:252 at_pre_emit_sound
    public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreEmitSound(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
        => Hookable<(bool, GameObject, string, string, double, bool)>("at_pre_emit_sound", () => (true, emitter, soundDesc, soundMsg, loudness, isSay), emitter, soundDesc, soundMsg, loudness, isSay);

    // Port of nodes.py:271 at_pre_hear
    public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
        => Hookable<(bool, GameObject, string, string, double, bool)>("at_pre_hear", () => (true, emitter, soundDesc, soundMsg, loudness, isSay), emitter, soundDesc, soundMsg, loudness, isSay);

    // Port of nodes.py:293 at_hear — Node propagation, overrides GameObject.AtHear (player hearing is separate)
    public override double AtHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        return Hookable("at_hear", () =>
        {
            var (allow, em2, sd2, sm2, loud2, isSay2) = AtPreHear(emitter, soundDesc, soundMsg, loudness, isSay);
        bool open = false;
        var nh = NodeHandler.GetCurrent();
        Dictionary<string, Door>? doors = null;
        try { doors = nh?.GetDoors(Coord); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NoIdMarker.AtHear: " + logEx.Message, "NoIdMarker"); }
        if (doors != null && doors.Count > 0)
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
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NoIdMarker.AtHear: " + logEx.Message, "NoIdMarker"); }
        }
            return loud2 - attenuation;
        }, emitter, soundDesc, soundMsg, loudness, isSay);
    }

    // Port of nodes.py:332
    public override bool AtPreObjectLeave(GameObject? destination, string? toExit = null)
    {
        return Hookable("at_pre_object_leave", () => true, destination, toExit);
    }
    // Port of nodes.py:348
    public override void AtObjectLeave(GameObject? destination, string? toExit = null)
    {
        Hookable("at_object_leave", () => 0, destination, toExit);
    }
    // Port of nodes.py:359
    public override bool AtPreObjectReceive(GameObject? source, string? fromExit = null)
    {
        return Hookable("at_pre_object_receive", () => true, source, fromExit);
    }
    // Port of nodes.py:374
    public override void AtObjectReceive(GameObject? source, string? fromExit = null)
    {
        Hookable("at_object_receive", () => 0, source, fromExit);
    }
    // Port of nodes.py:386
    public override void AtInit() => Hookable("at_init", () => 0);

    // Port of nodes.py:394 delete
    public override (int count, List<object> ops)? Delete(GameObject? caller, bool recursive = false)
    {
        (List<object> ops, int count) execDeleteRecursive(Node obj)
        {
            var allOps = new List<object>();
            int count = 0;
            var seen = new HashSet<int>();
            var contents = obj.GetContents();
            foreach (var content in contents.ToList())
            {
                if (!seen.Add(content.Id)) continue;
                var res = content.Delete(caller, true);
                if (res == null) continue;
                allOps.AddRange(res.Value.ops);
                count += res.Value.count;
            }
            return (allOps, count);
        }
        (List<object> ops, int count) execMoveContents(Node obj)
        {
            var allOps = new List<object>();
            int count = 0;
            var contents = obj.GetContents().ToList();
            foreach (var content in contents)
            {
                bool moved = false;
                var homeRef = content.Home;
                GameObject? homeObj = null;
                if (homeRef is Persistence.Dto.LocationRef.ObjectLocation ol)
                {
                    var got = ObjectRegistry.Get(ol.ObjectId);
                    homeObj = got.FirstOrDefault();
                }
                else if (homeRef is Persistence.Dto.LocationRef.CoordLocation cl)
                {
                    var cands = ObjectRegistry.FilterBy(o => o is Node n && n.Coord.Equals(cl.Coord));
                    homeObj = cands.FirstOrDefault();
                }
                GameObject? fallback = null;
                if (caller != null)
                {
                    // collapsed single expression (the three-way
                    // if/else assigned loc in exactly one case).
                    var loc = caller.ResolveLocationObject();
                    fallback = (loc != null && !ReferenceEquals(loc, obj)) ? loc : null;
                }
                if (homeObj != null)
                {
                    if (content.MoveTo(homeObj)) moved = true;
                    else if (fallback != null && content.MoveTo(fallback, force: true, announce: false)) moved = true;
                }
                else if (fallback != null)
                {
                    if (content.MoveTo(fallback, force: true, announce: false)) moved = true;
                }
                if (!moved)
                {
                    if (ReferenceEquals(content.ResolveLocationObject(), obj))
                    {
                        try { obj.RemoveObject(content); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NoIdMarker.Delete: " + logEx.Message, "NoIdMarker"); }
                        try { content.Location = Persistence.Dto.LocationRef.NullLocation.Instance; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NoIdMarker.Delete: " + logEx.Message, "NoIdMarker"); }
                    }
                    var res = content.Delete(caller, true);
                    if (res != null) { allOps.AddRange(res.Value.ops); count += res.Value.count; }
                }
            }
            return (allOps, count);
        }
        void execSelfDelete()
        {
            if (IsTickable)
            {
                try { GlobalTickerHolder.Get()?.RemoveCoro(AtTick, TickSeconds); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NoIdMarker.Delete: " + logEx.Message, "NoIdMarker"); }
            }
            try { NodeHandler.GetCurrent()?.RemoveNode(Coord); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NoIdMarker.Delete: " + logEx.Message, "NoIdMarker"); }
        }
        if (caller != null && !AtDelete(caller)) return null;
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
            ops.Add(GetDelOps());
        ObjectRegistry.RemoveObject(this);
        TeardownDeleted(this);
        return (1 + kids, ops);
    }

    // Port of nodes.py:479
    public override bool AtDelete(GameObject? caller)
    {
        return Hookable("at_delete", () =>
        {
            if (!Access(caller, "delete"))
            {
                try { caller?.Msg($"You cannot delete {GetDisplayName(caller)}."); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed NoIdMarker.AtDelete: " + logEx.Message, "NoIdMarker"); }
                return false;
            }
            return true;
        }, caller);
    }

}

/// <summary>Holds global ticker singleton for Node tick wiring.</summary>
internal static class GlobalTickerHolder
{
    private static Atheriz.Core.Concurrency.AsyncTicker? _instance;
    private static readonly object _lock = new();
    public static Atheriz.Core.Concurrency.AsyncTicker? Get() { lock (_lock) return _instance; }
    public static void Set(Atheriz.Core.Concurrency.AsyncTicker ticker) { lock (_lock) _instance = ticker; }
}

/// <summary>Minimal ExitCommand port of atheriz/commands/loggedin/exit.py:ExitCommand</summary>
public sealed class ExitCommand : Command
{
    public int CallerId { get; set; }
    public Coord Location { get; set; }
    public Coord Destination { get; set; }
    private string _key = "";
    public override string Key => _key;
    public string ExitName { get; set; } = "";
    private List<string> _aliases = [];
    public override IReadOnlyList<string> Aliases => _aliases;
    public void SetKey(string k) { _key = k; ExitName = k; }
    public void SetAliases(List<string> a) => _aliases = a ?? [];
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is GameObject go)
        {
            var dest = NodeHandler.GetCurrent()?.GetNode(Destination);
            if (dest != null)
            {
                // Port of exit.py:95-103 via the shared helper: moving
                // through an exit breaks following like any other move.
                try { Commands.LoggedIn.LoggedInExitCommand.ClearFollowing(go); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ExitCommand.Run: " + logEx.Message, "ExitCommand"); }
                go.MoveTo(dest);
            }
            else go.Msg("You can't go that way.");
        }
    }
}
