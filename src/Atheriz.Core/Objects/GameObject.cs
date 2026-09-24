using System.Diagnostics.CodeAnalysis;

using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Objects;

/// <summary>
/// Core entity.
/// Thread-safe via ReaderWriterLockSlim (SupportsRecursion) mirroring Python RLock.
/// </summary>
public partial class GameObject : IMessageTarget, ISessionProvider
{
    // TODO: SupportsRecursion required for re-entrant hooks: Access -> IsSuperUser, Hookable callbacks and property getters re-enter via Read/Write helpers
    private ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly List<string> _msgLog = new();

    // Puppet snapshot — only is_pc/privilege_level per puppet.py:110 wontfix (quelled/can_hear/is_mapable not saved)
    private PuppetRestoreSnapshot? _puppetRestore; // transient, never persisted
    private double _secondsPlayed;

    // --- identity ---
    private int _id = -1;
    private int _hashCache;
    // Hash snapshot: fixed at construction / load-path re-key (see SetIdRaw),
    // never by the public Id setter. Mutating Id after the instance entered
    // a HashSet/Dictionary keeps it findable (same bucket); same-Id
    // instances arranged before first hashing still hash equal.
    private string _name = "";
    private string _desc = "";
    private string _symbol = "X";
    private string _moveVerb = "walk";
    private List<string> _aliases = [];
    private HashSet<string> _tags = [];
    private Privilege _privilege = Privilege.Guest;

    // --- flags (mirrors FLAG_DEFAULTS) — extracted to Flags value-object (OOP deduplication) ---
    private readonly Flags _flags = new();

    private double _tickSeconds = 1.0;
    private bool _quelled;
    private bool _mapEnabled = true;
    private double? _lastMapTime;
    private string _gender = "neutral";
    private LocationRef _location = LocationRef.NullLocation.Instance;
    private LocationRef _home = LocationRef.NullLocation.Instance;
    private HashSet<int> _contents = [];
    private HashSet<int> _scripts = [];
    private List<int> _channels = [];
    private HashSet<int> _followers = [];
    private int? _following;
    private bool _noFollow;
    private int? _groupChannel;

    // locks: name -> list of predicates (see LockTable), hooks: funcName ->
    // set of delegates tagged via attributes (see HookRegistry). Both are
    // lock-free containers; the GameObject read/write lock above guards them.
    private readonly LockTable _lockTable = new();
    private readonly HookRegistry _hookRegistry = new();
    // Typed hooks access for Script.RemoveHooks (replaces _hooks reflection).
    // Caller must hold the write lock; name mirrors the RawNoLock convention.
    internal Dictionary<string, HashSet<Delegate>> HooksRawNoLock => _hookRegistry.Raw;

    private Dictionary<string, JsonElement> _extra = [];
    private CmdSet? _internalCmdSet;
    private CmdSet? _externalCmdSet;
    public GameObject() : this(GetNextId())
    {
        // Every instance owns a unique registry id from birth: Equals and
        // GetHashCode are id-based, so a transient Id (-1) that is later
        // reassigned breaks HashSet/Dictionary membership (the member stays
        // in the old hash bucket). Load paths use CreateForLoad(id).
    }

    // Construction core: adopts the id and fixes the hash snapshot together,
    // so the instance is publishable on return. The public ctor draws a fresh
    // id; load paths pass the stored id (generator watermark untouched). Only
    // CreateForLoad factories call this with a stored id — the instance must
    // never be hashed or published before the factory returns.
    protected GameObject(int id)
    {
        _id = id;
        _hashCache = id.GetHashCode();
        _lastMapTime = global::Atheriz.Core.Utils.GameClock.MonotonicSeconds();
        _mapEnabled = true;
    }

    // Load-path factory: constructs under the stored id without touching the
    // id generator. Game-registered subtype factories (Func<GameObject>) draw
    // normally and adopt the stored id via SetIdRaw instead.
    internal static GameObject CreateForLoad(int id) => new GameObject(id);
    // --- helpers for lock scope ---
    // Protected so derived holders (Account) reuse the same lock discipline
    // instead of duplicating tiny Enter/Exit wrappers per field.
    protected T Read<T>(Func<T> fn)
    {
        _lock.EnterReadLock();
        try { return fn(); }
        finally { _lock.ExitReadLock(); }
    }
    protected void Write(Action action)
    {
        _lock.EnterWriteLock();
        try { action(); }
        finally { _lock.ExitWriteLock(); }
    }
    protected T Write<T>(Func<T> fn)
    {
        _lock.EnterWriteLock();
        try { return fn(); }
        finally { _lock.ExitWriteLock(); }
    }
    private void SetFlag(Func<bool> getter, Action<bool> setter, bool value) => Write(() => { if (getter() != value) { setter(value); _flags.IsModified = true; } });
    // Guarded-set core for the check-set-dirty setters below: assigns and marks
    // modified only when the value actually changes. Takes the write lock itself
    // because a ref field cannot be captured by the Write(Action) lambda.
    private void SetIfChanged<T>(ref T field, T value)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                _flags.IsModified = true;
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    // --- scoped lock helpers (private lock with scoped read/write helpers) ---
    public IDisposable ReadScope()
    {
        _lock.EnterReadLock();
        return new LockScope(_lock, isWrite: false);
    }
    public IDisposable WriteScope()
    {
        _lock.EnterWriteLock();
        return new LockScope(_lock, isWrite: true);
    }

    // --- properties (setters mark isModified) ---
    // Id reassignment intentionally does NOT move the hash snapshot: an
    // instance already sitting in a HashSet/Dictionary stays findable under
    // its construction-time hash. Fresh load-path re-keying goes through
    // SetIdRaw (which does move the snapshot, before first hashing).
    public int Id { get => Read(() => _id); set => SetIfChanged(ref _id, value); }
    public virtual string Name { get => Read(() => _name); set => SetIfChanged(ref _name, value); }
    public string Desc { get => Read(() => _desc); set => SetIfChanged(ref _desc, value); }
    public virtual string Symbol { get => Read(() => _symbol); set => SetIfChanged(ref _symbol, value); }
    public string MoveVerb { get => Read(() => _moveVerb); set => Write(() => { _moveVerb = value; _flags.IsModified = true; }); }
    public Privilege PrivilegeLevel { get => Read(() => _privilege); set => SetIfChanged(ref _privilege, value); }
    public bool IsPc { get => Read(() => _flags.IsPc); set => SetFlag(() => _flags.IsPc, v => _flags.IsPc = v, value); }
    public bool IsNpc { get => Read(() => _flags.IsNpc); set => SetFlag(() => _flags.IsNpc, v => _flags.IsNpc = v, value); }
    public bool IsItem { get => Read(() => _flags.IsItem); set => SetFlag(() => _flags.IsItem, v => _flags.IsItem = v, value); }
    public bool IsMapable { get => Read(() => _flags.IsMapable); set => Write(() => { _flags.IsMapable = value; _mapEnabled = value; _flags.IsModified = true; }); }
    public bool IsContainer { get => Read(() => _flags.IsContainer); set => SetFlag(() => _flags.IsContainer, v => _flags.IsContainer = v, value); }
    public bool IsScript { get => Read(() => _flags.IsScript); set => SetFlag(() => _flags.IsScript, v => _flags.IsScript = v, value); }
    public virtual bool IsTickable { get => Read(() => _flags.IsTickable); set => SetFlag(() => _flags.IsTickable, v => _flags.IsTickable = v, value); }
    public bool IsAccount { get => Read(() => _flags.IsAccount); set => SetFlag(() => _flags.IsAccount, v => _flags.IsAccount = v, value); }
    public bool IsChannel { get => Read(() => _flags.IsChannel); set => SetFlag(() => _flags.IsChannel, v => _flags.IsChannel = v, value); }
    public bool IsNode { get => Read(() => _flags.IsNode); set => SetFlag(() => _flags.IsNode, v => _flags.IsNode = v, value); }
    public bool IsModified { get => Read(() => _flags.IsModified); set => Write(() => _flags.IsModified = value); }
    public virtual bool IsDeleted { get => Read(() => _flags.IsDeleted); set => SetFlag(() => _flags.IsDeleted, v => _flags.IsDeleted = v, value); }

    public bool IsConnected { get => Read(() => _flags.IsConnected); set => SetFlag(() => _flags.IsConnected, v => _flags.IsConnected = v, value); }
    public bool IsTemporary { get => Read(() => _flags.IsTemporary); set => SetFlag(() => _flags.IsTemporary, v => _flags.IsTemporary = v, value); }
    public bool IsBanned { get => Read(() => _flags.IsBanned); set => SetFlag(() => _flags.IsBanned, v => _flags.IsBanned = v, value); }
    public bool CanHear { get => Read(() => _flags.CanHear); set => SetFlag(() => _flags.CanHear, v => _flags.CanHear = v, value); }
    public bool Quelled { get => Read(() => _quelled); set => Write(() => { _quelled = value; _flags.IsModified = true; }); }
    public bool MapEnabled { get => Read(() => _mapEnabled); set => Write(() => { _mapEnabled = value; _flags.IsMapable = value; _flags.IsModified = true; }); }
    public double? LastMapTime { get => Read(() => _lastMapTime); set => Write(() => _lastMapTime = value); }
    public string Gender { get => Read(() => _gender); set => SetIfChanged(ref _gender, value); }
    public virtual double TickSeconds { get => Read(() => _tickSeconds); set => Write(() => { _tickSeconds = value; _flags.IsModified = true; }); }

    public virtual Dictionary<(int X, int Y), string> AtPreMapRender(Dictionary<(int X, int Y), string> grid)
    {
        return Hookable(HookName.AtPreMapRender, () => grid, grid);
    }
    // Legend projection shared by AtMapUpdate/AtLegendUpdate: the two Select
    // bodies were verified element-wise identical, so one helper emits both.
    internal static List<List<object?>> ProjectLegendEntries(List<(string sym, string desc, (int x, int y) coord)> entries)
        => entries.Select(e => new List<object?> { e.sym, e.desc, new List<int> { e.coord.x, e.coord.y } }).ToList();
    public virtual void AtMapUpdate(string mapStr, List<(string sym, string desc, (int x, int y) coord)> entries, int minX, int maxY, bool showLegend, string name)
    {
        Hookable(HookName.AtMapUpdate, () =>
        {
            (int relX, int relY) pos = (0, 0);
            try
            {
                var loc = Location;
                if (loc is Persistence.Dto.LocationRef.CoordLocation cl)
                {
                    pos = (cl.Coord.X - minX, maxY - cl.Coord.Y);
                }
                else if (loc is Persistence.Dto.LocationRef.ObjectLocation ol)
                {
                    var single = Globals.ObjectRegistry.GetSingle(ol.ObjectId);
                    if (single is Node n)
                        pos = (n.Coord.X - minX, maxY - n.Coord.Y);
                }
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.AtMapUpdate: " + logEx.Message, "GameObject"); }
            bool sent = false;
            try
            {
                Dictionary<string, object?> payload = new()
                {
                    ["map"] = mapStr,
                    ["pos"] = new List<int> { pos.relX, pos.relY },
                    ["symbol"] = Symbol,
                    ["legend"] = ProjectLegendEntries(entries),
                    ["min_x"] = minX,
                    ["max_y"] = maxY,
                    ["area"] = name,
                    ["show_legend"] = showLegend,
                };
                Session? sess = Session;
                var conn = sess?.Connection;
                // Stamp LastMapTime only when delivery succeeded: a failed
                // send raises before the stamp line, so failures never
                // become success stamps.
                if (conn is not null)
                {
                    conn.SendCommand("map", new List<object?> { payload }, null);
                    sent = true;
                }
                else
                {
                    // Fallback for test harnesses without connection — store via _msgLog like original Python would via session.msg
                    // Use MsgInternal path via Session if available, otherwise log
                    try { Msg($"[map:{name}]"); sent = true; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.AtMapUpdate: " + logEx.Message, "GameObject"); }
                }
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.AtMapUpdate: " + logEx.Message, "GameObject"); }
            if (sent) LastMapTime = global::Atheriz.Core.Utils.GameClock.MonotonicSeconds();
            return 0;
        }, mapStr, entries, minX, maxY, showLegend, name);
    }
    public virtual void AtLegendUpdate(List<(string sym, string desc, (int x, int y) coord)> entries, bool show, string area)
    {
        Hookable(HookName.AtLegendUpdate, () =>
        {
            try
            {
                Dictionary<string, object?> payload = new()
                {
                    ["area"] = area,
                    ["legend"] = ProjectLegendEntries(entries),
                    ["show_legend"] = show,
                };
                Session? sess = Session;
                var conn = sess?.Connection;
                if (conn is not null)
                    conn.SendCommand("legend", new List<object?> { payload }, null);
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.AtLegendUpdate: " + logEx.Message, "GameObject"); }
            return 0;
        }, entries, show, area);
    }
    public virtual void AtDesc(GameObject? looker = null) => Hookable(HookName.AtDesc, () => 0, looker);
    public virtual string AtPreSay(string message) => Hookable(HookName.AtPreSay, () => message, message);
    public LocationRef Location { get => Read(() => _location); set => Write(() => { _location = value; _flags.IsModified = true; }); }
    public LocationRef Home { get => Read(() => _home); set => Write(() => { _home = value; _flags.IsModified = true; }); }
    private Session? _session;
    public Session? Session { get => Read(() => _session); set => Write(() => _session = value); }
    public double SecondsPlayed
    {
        get => Read(() =>
        {
            double baseVal = _secondsPlayed;
            var sess = _session;
            if (sess is not null && sess.ConnTime > 0)
            {
                double elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - sess.ConnTime;
                if (elapsed > 0) baseVal += elapsed;
            }
            return baseVal;
        });
        set => Write(() => _secondsPlayed = value);
    }
    internal double RawSecondsPlayed { get => Read(() => _secondsPlayed); set => Write(() => _secondsPlayed = value); }
    public bool NoFollow { get => Read(() => _noFollow); set => Write(() => { _noFollow = value; _flags.IsModified = true; }); }
    public int? Following { get => Read(() => _following); set => Write(() => { _following = value; _flags.IsModified = true; }); }
    public int? GroupChannel { get => Read(() => _groupChannel); set => Write(() => { _groupChannel = value; _flags.IsModified = true; }); }
    public CmdSet? InternalCmdSet { get => Read(() => _internalCmdSet); set => Write(() => _internalCmdSet = value); }
    public CmdSet? ExternalCmdSet { get => Read(() => _externalCmdSet); set => Write(() => _externalCmdSet = value); }

    public bool IsSuperUser => Read(() => _privilege >= Privilege.Admin && !_quelled);
    public bool IsBuilder => Read(() => _privilege >= Privilege.Builder && !_quelled);

    // snapshots for collections
    public List<string> Aliases
    {
        get => Read(() => new List<string>(_aliases));
        set => Write(() => { _aliases = new List<string>(value); _flags.IsModified = true; });
    }
    // Null-tolerant (hasattr equivalent): reflection/test teardown can force
    // a null field, mirroring Python's deleted-attr guard (test_tags.py:187).
    public HashSet<string> TagsSnapshot => Read(() => _tags is null ? [] : new HashSet<string>(_tags));
    public HashSet<int> ContentsSnapshot => Read(() => new HashSet<int>(_contents));
    public HashSet<int> ScriptsSnapshot => Read(() => new HashSet<int>(_scripts));
    public List<int> ChannelsSnapshot => Read(() => new List<int>(_channels));
    public HashSet<int> FollowersSnapshot => Read(() => new HashSet<int>(_followers));

    /// <summary>
    /// Curated member list for the examine command (typed replacement for the
    /// reflection field/property walk). Game-code subclasses override to append
    /// their own members. Bool marks CLR properties ("[property]" in output).
    /// </summary>
    public virtual IEnumerable<(string name, object? value, bool isProperty)> GetExamMembers()
        => Atheriz.Core.Commands.LoggedIn.ExamFormatter.BaseMembers(this);

    public ReaderWriterLockSlim SyncRoot => _lock;

    internal void SetIsDeletedRaw(bool v) => Write(() => { _flags.IsDeleted = v; _flags.IsModified = true; });

    // Identity is the registry id (atheriz/objects/nodes.py:85-93 defines it for Node;
    // ids are registry-unique so this holds for all objects). Transient Id == -1 objects
    // only equal themselves, mirroring Python's identity fallback.
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        if (obj is GameObject o) return Id != -1 && Id == o.Id;
        return false;
    }
// hash by registry id, matching Equals above.
    // Snapshot semantics (see _hashCache): the public Id setter never moves
    // the hash, so mutating Id after hash-container insertion keeps the
    // member findable. Same-Id instances arranged before first hashing
    // (Create/load/duplicate patterns) hash equal as before.
    public override int GetHashCode() => Read(() => _hashCache);

    // ==/!= use the same Id value-equality as Equals (nodes.py:85-93),
    // so same-Id reload instances compare equal instead of falling back to
    // reference identity (which stranded followers after reloads).
    public static bool operator ==(GameObject? a, GameObject? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.Id != -1 && a.Id == b.Id;
    }
    public static bool operator !=(GameObject? a, GameObject? b) => !(a == b);


    // --- tag ops ---
    public void AddTag(string tag) => AddTags([tag]);
    public void AddTags(IEnumerable<string> tags)
    {
        Write(() =>
        {
            _tags ??= [];
            foreach (var t in tags) _tags.Add(t);
            _flags.IsModified = true;
        });
    }
    public void RemoveTag(string tag) => RemoveTags([tag]);
    public void RemoveTags(IEnumerable<string> tags)
    {
        Write(() =>
        {
            _tags ??= [];
            foreach (var t in tags) _tags.Remove(t);
            _flags.IsModified = true;
        });
    }
    public bool HasTag(string tag, bool all = false) => HasTags([tag], all);
    public bool HasTags(IEnumerable<string> tags, bool all = false)
    {
        return Read(() =>
        {
            var cur = _tags ?? [];
            return all ? tags.All(cur.Contains) : tags.Any(cur.Contains);
        });
    }

    // --- contents ops ---
    public void AddContent(int id) => Write(() => { _contents.Add(id); _flags.IsModified = true; });
    public void RemoveContent(int id) => Write(() => { _contents.Remove(id); _flags.IsModified = true; });

    // Listener-holding dispatch: only holders (Channel) keep a listener set.
    // The base is a no-op so teardown and unsubscribe paths never name
    // derived types to reach the holder behavior.
    public virtual void RemoveListener(GameObject departing) { }

    public void Subscribe(Channel channel)
    {
        if (channel is null) return;
        if (channel.IsDeleted) return;
        bool already;
        _lock.EnterReadLock();
        try { already = _channels.Contains(channel.Id); }
        finally { _lock.ExitReadLock(); }
        if (already) return;
        if (channel.IsDeleted) return;
        channel.AddListener(this);
        // Record first (peer lock only), install the command after (channel
        // locks only): acquiring the channel lock while holding the peer
        // write lock nests peer → channel and deadlocks against Msg
        // delivery (channel → peer). Mirrors the B-OBJ-14 discipline.
        bool added = false;
        _lock.EnterWriteLock();
        try
        {
            // re-check under write lock; a channel deleted in the race
            // must not land in _channels. Uses the lock-free snapshot: taking
            // _histLock here (via IsDeleted) would nest peer → channel and
            // deadlock against Msg delivery (channel → peer).
            if (channel.IsDeletedSnapshot())
            {
                try { channel.RemoveListener(this); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Subscribe: " + logEx.Message, "GameObject"); }
            }
            else if (!_channels.Contains(channel.Id))
            {
                _channels.Add(channel.Id);
                _flags.IsModified = true;
                // The InternalCmdSet itself is allocated here, under the peer write
                // lock — allocating it outside (after release) let two racing
                // Subscribes both see null, both allocate, and the second
                // silently orphan the first channel's installed command.
                InternalCmdSet ??= new Commands.CmdSet();
                added = true;
            }
        }
        finally { _lock.ExitWriteLock(); }
        if (added)
        {
            // command on the internal cmdset (removed again on unsubscribe).
            try
            {
                var cmd = channel.GetCommand();
                if (cmd is not null)
                {
                    var cs = InternalCmdSet;
                    if (cs is null) { cs = new Commands.CmdSet(); InternalCmdSet = cs; }
                    try { cs.Add(cmd); }
                    catch (InvalidOperationException) { /* already installed */ }
                    // A Delete/Unsubscribe detach can land between the
                    // peer-add above and this install: its cmdset removal
                    // then slips past the not-yet-added command and orphans
                    // a live command for a dead/unsubscribed channel (race
                    // R3). Re-validate under the peer read lock and roll the
                    // install back on mismatch. Keeping it is safe when the
                    // check passes: any later detach removes this exact
                    // (cached) instance. IsDeletedSnapshot is lock-free, so
                    // no peer->channel nesting here (see above).
                    bool stale;
                    _lock.EnterReadLock();
                    try { stale = !_channels.Contains(channel.Id) || channel.IsDeletedSnapshot(); }
                    finally { _lock.ExitReadLock(); }
                    if (stale)
                    {
                        try { cs.Remove(cmd); }
                        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Subscribe: " + logEx.Message, "GameObject"); }
                    }
                }
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Subscribe: " + logEx.Message, "GameObject"); }
        }
    }
    /// <summary>
    /// Id-based half of <see cref="Unsubscribe(Channel)"/> for teardown paths
    /// where the channel object itself may already be gone.
    /// </summary>
    public void UnsubscribeById(int channelId)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_channels.Remove(channelId)) _flags.IsModified = true;
        }
        finally { _lock.ExitWriteLock(); }
    }
    public void Unsubscribe(Channel channel)
    {        if (channel is null) return;
        bool had;
        _lock.EnterReadLock();
        try { had = _channels.Contains(channel.Id); }
        finally { _lock.ExitReadLock(); }
        if (!had) return;
        channel.RemoveListener(this);
        bool removed = false;
        _lock.EnterWriteLock();
        try
        {
            if (_channels.Remove(channel.Id))
            {
                _flags.IsModified = true;
                removed = true;
            }
        }
        finally { _lock.ExitWriteLock(); }
        if (removed)
        {
            // Channel locks only (see Subscribe) — never under the peer lock.
            try { var cmd = channel.GetCommand(); if (cmd is not null) InternalCmdSet?.Remove(cmd); }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Unsubscribe: " + logEx.Message, "GameObject"); }
        }
    }

    // --- locks ---
    // Lock-table insert core shared by the concurrent AddLock path and the
    // single-threaded load restore path (ApplyDtoFields). AddLock itself does
    // nothing but this insert (no logging, no events), so restoring through
    // the same core produces identical lock state without re-taking the lock.
    private void AddLockRestored(string lockName, LockEntry entry) => _lockTable.AddRestored(lockName, entry);
    public void AddLock(string lockName, Func<GameObject, bool> predicate, string policy = LockPolicies.Custom)
    {
        Write(() => AddLockRestored(lockName, new LockEntry(LockPolicies.Classify(policy), predicate)));
    }
    // Typed authoring overload: maps the enum straight to the stored entry, so
    // the persisted policies are identical to the string call.
    public void AddLock(string lockName, Func<GameObject, bool> predicate, LockPolicies.LockPolicy policy)
        => Write(() => AddLockRestored(lockName, new LockEntry(policy, predicate)));
    public void ClearLocksByName(string lockName) => Write(() => { _lockTable.Remove(lockName); });

    /// <summary>
    /// Mirrors <c>base_lock.AccessLock.access</c>: self-delete/get block, superuser bypass, then iterate locks[name].
    /// </summary>
    public virtual bool Access(GameObject? accessingObj, string lockName)
    {
        if (accessingObj is null) return false;
        // self-lock: accessing self for delete/get always denied (even superuser)
        if (Id != -1 && accessingObj.Id == Id && (lockName == "delete" || lockName == "get"))
            return false;
        if (accessingObj.IsSuperUser) return true;
        List<LockEntry> snapshot;
        using (ReadScope())
        {
            if (!_lockTable.TrySnapshot(lockName, out snapshot)) return true;
        }
        foreach (var entry in snapshot)
        {
            bool ok;
            try { ok = entry.Predicate(accessingObj); }
            catch { return false; }
            if (!ok) return false;
        }
        return true;
    }

    public virtual void InstallHook(string funcName, Delegate hook)
    {
        // Attach-time arity validation for known hooks: a delegate that
        // cannot take any dispatch shape is refused loudly instead of
        // installing and skipping at every dispatch. Unknown (custom hook)
        // names bypass validation — game code defines their own shapes.
        if (HookNameExtensions.TryParseName(funcName) is { } name
            && !name.AcceptsArity(hook, HookMarkerCache.KindOf(hook)))
        {
            AtherizLogger.LogError($"GameObject.InstallHook refused {funcName} hook with mismatched signature ({hook.Method}); hook skipped.");
            return;
        }
        Write(() => _hookRegistry.Add(funcName, hook));
    }
    public void InstallHook(HookName hookName, Delegate hook) => InstallHook(hookName.Name(), hook);
    public bool HasHook(string funcName) => Read(() => _hookRegistry.Has(funcName));
    public bool HasHook(HookName hookName) => HasHook(hookName.Name());

    // Single-id fetch core for the GetSingle is Script shape repeated across
    // ResolveRelations/AddScript/RemoveScript/GetScriptsByType. A missing id
    // and a wrong-typed id both mean "absent" at every one of those sites
    // (verified: each skips identically), so one helper covers them.
    internal static bool TryGetScript(int id, [NotNullWhen(true)] out Script? script)
    {
        var single = Globals.ObjectRegistry.GetSingle(id);
        if (single is Script s) { script = s; return true; }
        script = null;
        return false;
    }
    // Type-name match core for HasScriptType/GetScriptsByType: lowered
    // substring on the CLR type name (ordinal Contains on both lowered
    // sides — the ordinality is load-bearing, do not "fix" the casing).
    private static bool ScriptTypeMatches(GameObject obj, string needle)
        => obj.GetType().Name.ToLowerInvariant().Contains(needle);
    private HashSet<int> SnapshotScriptIds()
    {
        using (ReadScope()) return new HashSet<int>(_scripts);
    }
    public void AddScript(Script script)
    {
        if (script is null) return;
        script.InstallHooks(this);
    }
    public void AddScript(int scriptId)
    {
        if (TryGetScript(scriptId, out var s)) s.InstallHooks(this);
    }
    public void RemoveScript(Script script)
    {
        if (script is null) return;
        script.RemoveHooks(this);
    }
    public void RemoveScript(int scriptId)
    {
        if (TryGetScript(scriptId, out var s)) s.RemoveHooks(this);
    }
    // Typed _scripts-set writers (single SyncRoot; replaces _scripts reflection in Script/NodeHandler).
    internal void AddScriptId(int id) => Write(() => { if (_scripts.Add(id)) _flags.IsModified = true; });
    internal void RemoveScriptId(int id) => Write(() => { _scripts.Remove(id); _flags.IsModified = true; });
    // Load-path bulk restore (mirrors __setstate__ raw-dict restore: no dirty-marking; caller clears flags).
    internal void RestoreScriptIds(IEnumerable<int> ids) => Write(() => { _scripts.Clear(); foreach (var id in ids) _scripts.Add(id); });
    public bool HasScriptType(string scriptType)
    {
        // Keeps the direct GetSingle fetch (not TryGetScript): a non-Script object
        // whose type name contains the needle still counts here, as before.
        // Only the snapshot and the name comparison are shared.
        HashSet<int> ids = SnapshotScriptIds();
        if (ids.Count == 0) return false;
        string needle = scriptType.ToLowerInvariant();
        foreach (var id in ids)
        {
            var single = Globals.ObjectRegistry.GetSingle(id);
            if (single is not null && ScriptTypeMatches(single, needle)) return true;
        }
        return false;
    }
    // Typed generic front doors: no type-name string juggling at call sites.
    public bool HasScriptType<T>() where T : Script
    {
        var ids = SnapshotScriptIds();
        foreach (var id in ids)
            if (TryGetScript(id, out var s) && s is T) return true;
        return false;
    }
    public List<T> GetScriptsByType<T>() where T : Script
    {
        var ids = SnapshotScriptIds();
        List<T> list = [];
        foreach (var id in ids)
            if (TryGetScript(id, out var s) && s is T t) list.Add(t);
        return list;
    }
    public List<Script> GetScriptsByType(string scriptType)
    {
        HashSet<int> ids = SnapshotScriptIds();
        if (ids.Count == 0) return [];
        string needle = scriptType.ToLowerInvariant();
        List<Script> list = [];
        foreach (var id in ids)
        {
            if (TryGetScript(id, out var s) && ScriptTypeMatches(s, needle)) list.Add(s);
        }
        return list;
    }

    // --- display / messaging (Command parity) --- (implementation in Objects/Messaging/GameObjectMessaging.cs)

// delegate to ContentUtils.Search with ObjectRegistry resolver
    public virtual List<GameObject> Search(string query, bool recursive = true, GameObject? looker = null)
        => ContentUtils.Search(this, query, id => Globals.ObjectRegistry.Get(id).FirstOrDefault(), recursive, looker ?? this);

    /// <summary>
    /// Reconnects location/home already via LocationRef, re-registers tick, reinstalls script hooks, calls at_init.
    /// </summary>
    public virtual void ResolveRelations()
    {
        if (IsTickable)
        {
            try { Globals.GlobalServices.TryGetTicker()?.AddCoro(AtTick, TickSeconds); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.ResolveRelations: " + logEx.Message, "GameObject"); }
        }
        HashSet<int> scripts;
        _lock.EnterReadLock();
        try { scripts = new HashSet<int>(_scripts); }
        finally { _lock.ExitReadLock(); }
        foreach (var id in scripts)
        {
            if (TryGetScript(id, out var s))
            {
                try { s.InstallHooks(this); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.ResolveRelations: " + logEx.Message, "GameObject"); }
            }
        }
        try { AtInit(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.ResolveRelations: " + logEx.Message, "GameObject"); }
    }

    // --- DTO conversion (mirrors __getstate__/__setstate__) --- (persisted via Persistence/Converters/GameObjectDtoConverter.cs)
// single BuildDto collapse (update.md 3.3)
    // Faithful: if _puppetRestore present, use original is_pc/privilege_level for serialization (never persist puppeted state)
    private GameObjectDto BuildDto() => Persistence.Converters.GameObjectDtoConverter.BuildDto(this);

    public virtual GameObjectDto ToDto() => Read(() => BuildDto());

    // Delete-guard re-sync after a DTO restore writes _flags.IsDeleted
    // directly (bypassing overrides). Base is a no-op; Channel overrides it
    // to re-sync its listener fast-path guard.
    internal virtual void SyncDeletedGuard(bool deleted) { }

    internal static void ApplyDtoFields(GameObject o, GameObjectDto dto, bool? isNodeOverride)
    {
        o._name = dto.Name;
        o._desc = dto.Desc;
        o._aliases = new List<string>(dto.Aliases ?? []);
        o._tags = new HashSet<string>(dto.Tags ?? []);
        o._flags.IsPc = dto.IsPc;
        o._flags.IsNpc = dto.IsNpc;
        o._flags.IsItem = dto.IsItem;
        o._flags.IsContainer = dto.IsContainer;
        o._flags.IsMapable = dto.IsMapable;
        // Keep the twin field in sync (setters assign both; restore both).
        o._mapEnabled = dto.IsMapable;
        o._flags.IsNode = isNodeOverride ?? dto.IsNode;
        o._flags.IsTemporary = dto.IsTemporary;
        o._flags.IsDeleted = dto.IsDeleted;
        o._flags.IsModified = dto.IsModified;
        // restore roundtripped fields (missing in old saves → DTO defaults).
        o._flags.CanHear = dto.CanHear;
        o._flags.IsTickable = dto.IsTickable;
        o._tickSeconds = dto.TickSeconds != 0 ? dto.TickSeconds : 1.0;
        o._symbol = dto.Symbol ?? "X";
        o._moveVerb = dto.MoveVerb ?? "walk";
        o._quelled = dto.Quelled;
        o._flags.IsBanned = dto.IsBanned;
        o._noFollow = dto.NoFollow;
        o._secondsPlayed = dto.SecondsPlayed;
        // Channel keeps a separate delete guard for its listener fast path;
        // the direct flag restore above bypasses its IsDeleted override, so
        // re-sync through the virtual (no base-names-derived-type check here).
        o.SyncDeletedGuard(dto.IsDeleted);
        o._privilege = dto.PrivilegeLevel;
        o._gender = dto.Gender ?? "neutral";
        o._location = dto.Location ?? LocationRef.NullLocation.Instance;
        o._home = dto.Home ?? LocationRef.NullLocation.Instance;
        o._contents = new HashSet<int>(dto.Contents ?? []);
        o._scripts = new HashSet<int>(dto.Scripts ?? []);
        o._channels = new List<int>(dto.Channels ?? []);
        o._extra = new Dictionary<string, JsonElement>(dto.Extra ?? new());
        // Restore locks from declarative policies (F004). Unknown policies are dropped
        // with a loud log — never silently weakened, never executed from the save file.
        o._lockTable.Clear();
        if (dto.Locks is not null)
        {
            foreach (var ld in dto.Locks)
            {
                if (ld is null || string.IsNullOrEmpty(ld.Name)) continue;
                foreach (var pol in ld.Policies ?? [])
                {
                    if (pol == LockPolicies.LockPolicy.Custom)
                    {
                        // Ad-hoc lambda: no declarative policy was ever
                        // persisted — dropped with a loud log, never executed
                        // (mirrors Door.FromDto).
                        AtherizLogger.LogError($"Dropping unpersistable 'custom' lambda on lock '{ld.Name}' for object {dto.Id}; access allowed.");
                    }
                    else if (LockPolicies.TryResolve(pol, o, out var pred))
                    {
                        o.AddLockRestored(ld.Name, new LockEntry(pol, pred));
                    }
                    else
                    {
                        // Fail closed (mirrors Door.FromDto): Access returns
                        // true for missing entries, so dropping an
                        // unresolvable policy would escalate to allow. Deny
                        // and store the Denied marker so a re-save keeps the
                        // entry (still denying). Our own marker reloads
                        // quietly; anything else logs loudly.
                        if (pol != LockPolicies.LockPolicy.Denied)
                            AtherizLogger.LogError($"Unknown lock policy '{pol}' on lock '{ld.Name}' for object {dto.Id}; denying access.");
                        o.AddLockRestored(ld.Name, new LockEntry(LockPolicies.LockPolicy.Denied, _ => false));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Registers a game-defined <see cref="GameObject"/> subtype for persistence round-trips (F004).
    /// Replaces the old <c>Type.GetType + assembly scan + Activator</c> path: only explicitly
    /// registered full names are ever instantiated from save data; anything else loads as its
    /// base kind with a loud log. Games register their <c>Custom*</c> types at startup; tests
    /// register their doubles in fixtures.
    /// </summary>
    public static void RegisterPersistedSubtype(string fullName, Type type, Func<GameObject> factory)
        => Persistence.Converters.GameObjectDtoConverter.RegisterSubtype(fullName, type, factory);

    public static GameObject FromDto(GameObjectDto dto) => Persistence.Converters.GameObjectDtoConverter.FromDto(dto);

    // --- DbOps equivalents — typed save rows (SaveOperation) built by the converter ---
    public virtual Persistence.Dto.SaveOperation GetSaveOperation() => Persistence.Converters.GameObjectDtoConverter.GetSaveOperation(this);

    public virtual Persistence.Dto.SaveOperation GetSaveOperationClearing() => Persistence.Converters.GameObjectDtoConverter.GetSaveOperationClearing(this);

    public DeleteOperation GetDeleteOperation() => new(Id);

    // --- factory (mirrors Object.create) ---
    // Fix for test_account.py:88 — use unified IdGenerator counter (mirrors get_unique_id)
    public static int GetNextId() => Globals.IdGenerator.GetUniqueId();
    public static void SetNextId(int v) => Globals.IdGenerator.SetId(v);

    public static GameObject Create(string name, string desc = "", IEnumerable<string>? aliases = null,
        bool isPc = false, bool isItem = false, bool isNpc = false, bool isMapable = false, bool isContainer = false,
        bool isTickable = false, double tickSeconds = 1.0, GameObject? caller = null, Privilege privilege = Privilege.Guest)
    {
        var obj = new GameObject();
        obj._name = name;
        obj._desc = desc;
        obj._aliases = aliases is null ? [] : new List<string>(aliases);
        obj._privilege = privilege;
        obj._flags.IsPc = isPc;
        obj._flags.IsNpc = isNpc;
        obj._flags.IsItem = isItem;
        obj._flags.IsMapable = isMapable || isPc; // pc implies mapable per Python
        obj._flags.IsContainer = isContainer || isPc;
        if (isPc) obj._flags.CanHear = true;
        if (isNpc) obj._flags.CanHear = true;
        obj._flags.IsTickable = isTickable;
        obj._tickSeconds = tickSeconds;
        obj._lastMapTime = global::Atheriz.Core.Utils.GameClock.MonotonicSeconds();
        obj._mapEnabled = isMapable || isPc;
        obj._flags.IsModified = true;

        // mirrors Python create locks
        if (isPc)
        {
// tests only the *target's* connection.
            // Builders and above keep sight of offline PCs (room lists,
            // search, examine); regular players fail view and never see them.
            obj.AddLock("view", accessing => obj.IsConnected || accessing.IsBuilder, LockPolicies.LockPolicy.PcView);
        }
        // One "get" entry for pc and/or npc: duplicate Builder entries decide
        // identically, so a second call would only double the persisted policy.
        // Doubled rows still load and decide the same; they converge on next save.
        if (isPc || isNpc)
            obj.AddLock("get", accessing => accessing.IsBuilder, LockPolicies.LockPolicy.Builder);

        obj.AddLock("delete", accessing => accessing.Id != obj.Id, LockPolicies.LockPolicy.NotSelf);
        // puppet lock faithful to base_obj.py:185-193 — resolved from the
        // PuppetOwner policy (single spelling): the old inline lambda was
        // verified line-for-line identical to the policy body (obj ↔ target),
        // capturing no extra context, so the policy predicate replaces it.
        _ = LockPolicies.TryResolve(LockPolicies.LockPolicy.PuppetOwner, obj, out var puppetPred);
        obj.AddLock("puppet", puppetPred, LockPolicies.LockPolicy.PuppetOwner);

        // at_create hook. Registration stays explicit (AddObject/AddObjectUnique
        // at the call site — auto-adding here would double-register); ticker
        // enrolment mirrors get_async_ticker().add_coro for fresh tickables
        // (load path re-registers via ResolveRelations).
        obj._internalCmdSet = new Commands.CmdSet();
        obj._externalCmdSet = new Commands.CmdSet();
        try { obj.AtCreate(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Create: " + logEx.Message, "GameObject"); }
        if (isTickable)
        {
            try { Globals.GlobalServices.TryGetTicker()?.AddCoro(obj.AtTick, tickSeconds); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Create: " + logEx.Message, "GameObject"); }
        }

        return obj;
    }

    // --- persistence helpers exposed for GameObjectDtoConverter ---
    // Load-path re-key: moves the hash snapshot with the id. Only for fresh
    // (never-hashed) instances — production delete/create paths use this;
    // the public setter deliberately leaves the snapshot alone.
    internal void SetIdRaw(int id) => Write(() => { _id = id; _hashCache = id.GetHashCode(); _flags.IsModified = true; });
    internal Dictionary<string, System.Text.Json.JsonElement> GetExtraSnapshot() => Read(() => new Dictionary<string, System.Text.Json.JsonElement>(_extra));
    internal Dictionary<string, List<LockEntry>> GetLockEntriesSnapshot()
        => Read(() => _lockTable.SnapshotEntries());
    internal Dictionary<string, List<Func<GameObject, bool>>> GetLocksSnapshot()
        => Read(() => _lockTable.SnapshotPredicates());
    internal Dictionary<string, List<string>> GetLockPoliciesSnapshot()
        => Read(() => _lockTable.SnapshotPolicyNames());
    internal Persistence.Dto.GameObjectDto ToDtoUnsafeInternal() => BuildDto();
    // Raw IsModified access without re-entering lock (caller must hold write lock) — used by GetSaveOps.
    internal bool GetIsModifiedRawNoLock() => _flags.IsModified;
    internal void SetIsModifiedRawNoLock(bool v) => _flags.IsModified = v;

    // Typed extra helpers for GrottoObject (replaces reflection on _extra)
    public bool TryGetExtraJson(string key, out System.Text.Json.JsonElement value)
    {
        // Nullable smuggle instead of the old (bool, JsonElement) tuple: a
        // missing key reads as null (absent) while a stored JSON null reads as
        // a present Null-kind element — the distinction is preserved.
        System.Text.Json.JsonElement? hit = Read(() => _extra.TryGetValue(key, out var v) ? v : (System.Text.Json.JsonElement?)null);
        if (hit is null) { value = default; return false; }
        value = hit.Value;
        return true;
    }
    public void SetExtraJson(string key, System.Text.Json.JsonElement value) => Write(() => { _extra[key] = value; _flags.IsModified = true; });
    public bool TryRemoveExtraJson(string key) => Write(() => { var r = _extra.Remove(key); if (r) _flags.IsModified = true; return r; });
    // F010 note: intentionally internal, not public. PortedHelpPrivilegeTests.Set_TargetMe:318 probes
    // GetMethod("HasExtra") with public-only binding flags and takes the no-check branch when absent;
    // widening this to public flips the test into its NonPublic-invoke branch and NREs. Same-assembly
    // callers (SetHelper) work fine with internal.
    internal bool HasExtra(string key) => Read(() => _extra.ContainsKey(key));

    // Typed follower ops (F001: replaces _followers reflection in Follow/Exit/Nofollow commands).
    // Callers that already hold the target write lock mutate via the raw helpers below.
    public void AddFollower(int id) => Write(() => { if (_followers.Add(id)) _flags.IsModified = true; });
    public void RemoveFollower(int id) => Write(() => { if (_followers.Remove(id)) _flags.IsModified = true; });
    public void ClearFollowersExcept(HashSet<int>? keep = null) => Write(() => { if (_followers.RemoveWhere(id => keep is null || !keep.Contains(id)) > 0) _flags.IsModified = true; });
    internal void AddFollowerRawNoLock(int id) { if (_followers.Add(id)) _flags.IsModified = true; }
    internal void RemoveFollowerRawNoLock(int id) { if (_followers.Remove(id)) _flags.IsModified = true; }
    internal void ClearFollowersRawNoLock(HashSet<int>? keep = null) { if (_followers.RemoveWhere(id => keep is null || !keep.Contains(id)) > 0) _flags.IsModified = true; }

    // Typed channel-id ops (F001: replaces _channels reflection in GroupExtensions).
    internal void AddChannelId(int chId) => Write(() => { if (!_channels.Contains(chId)) { _channels.Add(chId); _flags.IsModified = true; } });
    internal void RemoveChannelId(int chId) => Write(() => { if (_channels.Remove(chId)) _flags.IsModified = true; });

    // Ban reason for any object (F001: replaces BanReason/_extra reflection in Ban/Connect commands).
    // Stored under the "ban_reason" extra key (BanCommand spelling). Account overrides this with its field-backed store.
    public virtual string BanReason
    {
        get => Read(() =>
        {
            if (_extra.TryGetValue("ban_reason", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String) return v.GetString() ?? "";
            return "";
        });
        set => Write(() =>
        {
            _extra["ban_reason"] = System.Text.Json.JsonSerializer.SerializeToElement(value);
            _flags.IsModified = true;
        });
    }

    // --- messaging helpers for converter wrappers kept thin ---
}
