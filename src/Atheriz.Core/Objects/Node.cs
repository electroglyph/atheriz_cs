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
    public override bool Equals(object? obj) => obj is NodeLink o && Name == o.Name && Coord.Equals(o.Coord);
    public override int GetHashCode() => HashCode.Combine(Name, Coord);
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

    // Port of atheriz/objects/nodes.py:122
    public Coord Coord { get; set; }
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

    public Node() : this(new Coord("limbo", 0, 0, 0)) { }
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
        ObjectRegistry.AddObject(this);
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
            try { func(obj); } catch { }
        }
    }

    // Port of nodes.py:189
    public override void ResolveRelations()
    {
        if (IsTickable)
        {
            var at = GlobalTickerHolder.Get();
            at?.AddCoro(AtTick, TickSeconds);
        }
        HashSet<int> scripts = ScriptsSnapshot;
        foreach (var id in scripts)
        {
            var objs = ObjectRegistry.Get(id);
            if (objs.Count > 0 && objs[0] is Script s) s.InstallHooks(this);
        }
        AtInit();
    }

    // Port of nodes.py:206 (state in base storage; Node adds ticker swap)
    public override double TickSeconds
    {
        get => base.TickSeconds;
        set
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
    }

    // Port of nodes.py:227 (state in base storage; Node adds ticker add/remove)
    public override bool IsTickable
    {
        get => base.IsTickable;
        set
        {
            if (base.IsTickable == value) return;
            double tick = base.TickSeconds;
            base.IsTickable = value;
            var at = GlobalTickerHolder.Get();
            if (value) at?.AddCoro(AtTick, tick);
            else at?.RemoveCoro(AtTick, tick);
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
        try { doors = nh?.GetDoors(Coord); } catch { }
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
            catch { }
        }
            return loud2 - attenuation;
        }, emitter, soundDesc, soundMsg, loudness, isSay);
    }

    // Port of nodes.py:332
    public override bool AtPreObjectLeave(GameObject? destination, string? toExit = null)
    {
        if (AtPreObjectLeaveOverride != null) return AtPreObjectLeaveOverride(destination, toExit);
        return Hookable("at_pre_object_leave", () => true, destination, toExit);
    }
    // Port of nodes.py:348
    public override void AtObjectLeave(GameObject? destination, string? toExit = null)
    {
        if (AtObjectLeaveOverride != null) { AtObjectLeaveOverride(destination, toExit); return; }
        Hookable("at_object_leave", () => 0, destination, toExit);
    }
    // Port of nodes.py:359
    public override bool AtPreObjectReceive(GameObject? source, string? fromExit = null)
    {
        if (AtPreObjectReceiveOverride != null) return AtPreObjectReceiveOverride(source, fromExit);
        return Hookable("at_pre_object_receive", () => true, source, fromExit);
    }
    // Port of nodes.py:374
    public override void AtObjectReceive(GameObject? source, string? fromExit = null)
    {
        if (AtObjectReceiveOverride != null) { AtObjectReceiveOverride(source, fromExit); return; }
        Hookable("at_object_receive", () => 0, source, fromExit);
    }
    // Port of nodes.py:386
    public override void AtInit() => Hookable("at_init", () => 0);

    // Port of nodes.py:394 delete
    public override (int count, List<object> ops)? Delete(GameObject? caller, bool recursive = false)
    {
        List<object> execDeleteRecursive(Node obj)
        {
            var allOps = new List<object>();
            var seen = new HashSet<int>();
            var contents = obj.GetContents();
            foreach (var content in contents.ToList())
            {
                if (!seen.Add(content.Id)) continue;
                var res = content.Delete(caller, true);
                if (res == null) continue;
                allOps.AddRange(res.Value.ops);
            }
            return allOps;
        }
        List<object> execMoveContents(Node obj)
        {
            var allOps = new List<object>();
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
                    var loc = caller.ResolveLocationObject();
                    if (loc != null && !ReferenceEquals(loc, obj)) fallback = loc;
                    else if (loc == obj) fallback = null;
                    else fallback = loc;
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
                        try { obj.RemoveObject(content); } catch { }
                        try { content.Location = Persistence.Dto.LocationRef.NullLocation.Instance; } catch { }
                    }
                    var res = content.Delete(caller, true);
                    if (res != null) allOps.AddRange(res.Value.ops);
                }
            }
            return allOps;
        }
        void execSelfDelete()
        {
            if (IsTickable)
            {
                try { GlobalTickerHolder.Get()?.RemoveCoro(AtTick, TickSeconds); } catch { }
            }
            try { NodeHandler.GetCurrent()?.RemoveNode(Coord); } catch { }
        }
        if (caller != null && !AtDelete(caller)) return null;
        SyncRoot.EnterWriteLock();
        try
        {
            if (IsDeleted) return null;
            IsDeleted = true;
        }
        finally { SyncRoot.ExitWriteLock(); }
        var ops = recursive ? execDeleteRecursive(this) : execMoveContents(this);
        execSelfDelete();
        return (1, ops);
    }

    // Port of nodes.py:479
    public override bool AtDelete(GameObject caller)
    {
        return Hookable("at_delete", () =>
        {
            if (!Access(caller, "delete"))
            {
                try { caller.Msg($"You cannot delete {GetDisplayName(caller)}."); } catch { }
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
            if (dest != null) go.MoveTo(dest);
            else go.Msg("You can't go that way.");
        }
    }
}
