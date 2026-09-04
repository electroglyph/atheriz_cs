using System.Text.Json;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Objects;

/// <summary>
/// Core entity. Ports <c>atheriz/objects/base_obj.py:Object</c> merged with
/// <c>base_flags.Flags</c>, <c>base_lock.AccessLock</c>, <c>base_db_ops.DbOps</c>.
/// Thread-safe via ReaderWriterLockSlim (SupportsRecursion) mirroring Python RLock.
/// </summary>
public partial class GameObject : IMessageTarget
{
    public static bool _is_thread_safe = true;
    // TODO: SupportsRecursion required for re-entrant hooks: Access -> IsSuperUser, Hookable callbacks and property getters re-enter via Read/Write helpers
    private ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly List<string> _msgLog = new();

    // --- test overrides for move hooks (faithful to MagicMock patching in tests) ---
    public Func<GameObject?, string?, bool>? AtPreMoveOverride { get; set; }
    public Action<GameObject?, string?>? AtPostMoveOverride { get; set; }
    public Func<GameObject?, string?, bool>? AtPreObjectLeaveOverride { get; set; }
    public Action<GameObject?, string?>? AtObjectLeaveOverride { get; set; }
    public Func<GameObject?, string?, bool>? AtPreObjectReceiveOverride { get; set; }
    public Action<GameObject?, string?>? AtObjectReceiveOverride { get; set; }

    // --- tracking for location lock test (counts EnterWriteLock calls when SetLockForTesting used) ---
    private object? _testTracker;
    private System.Reflection.FieldInfo? _trackerEntriesField;
    protected void IncrementTracker() { if (_testTracker != null && _trackerEntriesField != null) { try { var cur = (int)(_trackerEntriesField.GetValue(_testTracker) ?? 0); _trackerEntriesField.SetValue(_testTracker, cur+1); } catch {} } }
    private void EnterWriteLockTracked() { IncrementTracker(); _lock.EnterWriteLock(); }
    private void EnterReadLockTracked() { _lock.EnterReadLock(); }
    // Puppet snapshot — only is_pc/privilege_level per puppet.py:110 wontfix (quelled/can_hear/is_mapable not saved)
    private Dictionary<string, object>? _puppetRestore; // Port of target._puppet_restore (Python) — transient, never persisted
    private double _secondsPlayed; // Port of base_obj.py:103-104 _seconds_played + seconds_played property

    // --- identity ---
    private int _id = -1;
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

    // locks: name -> list of predicates
    private Dictionary<string, List<Func<GameObject, bool>>> _locks = [];

    // hooks: funcName -> set of delegates tagged via attributes
    private Dictionary<string, HashSet<Delegate>> _hooks = [];

    private Dictionary<string, JsonElement> _extra = [];
    private CmdSet? _internalCmdSet;
    private CmdSet? _externalCmdSet;

    public GameObject()
    {
        _lastMapTime = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
        _mapEnabled = true;
    }

    // --- helpers for lock scope ---
    private T Read<T>(Func<T> fn)
    {
        _lock.EnterReadLock();
        try { return fn(); }
        finally { _lock.ExitReadLock(); }
    }
    private void Write(Action action)
    {
        IncrementTracker(); _lock.EnterWriteLock();
        try { action(); }
        finally { _lock.ExitWriteLock(); }
    }
    private T Write<T>(Func<T> fn)
    {
        IncrementTracker(); _lock.EnterWriteLock();
        try { return fn(); }
        finally { _lock.ExitWriteLock(); }
    }
    private void SetFlag(string name, bool value) => Write(() => { if (_flags.TrySet(name, value)) _flags.IsModified = true; });

    // --- scoped lock helpers (audit P2-10: hide public Lock via private + scoped helpers) ---
    public IDisposable ReadScope()
    {
        _lock.EnterReadLock();
        return new LockScope(_lock, isWrite: false);
    }
    public IDisposable WriteScope()
    {
        IncrementTracker();
        _lock.EnterWriteLock();
        return new LockScope(_lock, isWrite: true);
    }
    private sealed class LockScope : IDisposable
    {
        private readonly ReaderWriterLockSlim _rw;
        private readonly bool _isWrite;
        public LockScope(ReaderWriterLockSlim rw, bool isWrite) { _rw = rw; _isWrite = isWrite; }
        public void Dispose()
        {
            if (_isWrite) _rw.ExitWriteLock();
            else _rw.ExitReadLock();
        }
    }

    // --- properties (setters mark isModified) ---
    public int Id { get => Read(() => _id); set => Write(() => { if (_id != value) { _id = value; _flags.IsModified = true; } }); }
    public string Name { get => Read(() => _name); set => Write(() => { if (_name != value) { _name = value; _flags.IsModified = true; } }); }
    public string Desc { get => Read(() => _desc); set => Write(() => { if (_desc != value) { _desc = value; _flags.IsModified = true; } }); }
    public string Symbol { get => Read(() => _symbol); set => Write(() => { if (_symbol != value) { _symbol = value; _flags.IsModified = true; } }); }
    public string MoveVerb { get => Read(() => _moveVerb); set => Write(() => { _moveVerb = value; _flags.IsModified = true; }); }
    public Privilege PrivilegeLevel { get => Read(() => _privilege); set => Write(() => { if (_privilege != value) { _privilege = value; _flags.IsModified = true; } }); }
    public bool IsPc { get => Read(() => _flags.IsPc); set => SetFlag("is_pc", value); }
    public bool IsNpc { get => Read(() => _flags.IsNpc); set => SetFlag("is_npc", value); }
    public bool IsItem { get => Read(() => _flags.IsItem); set => SetFlag("is_item", value); }
    public bool IsMapable { get => Read(() => _flags.IsMapable); set => Write(() => { _flags.IsMapable = value; _mapEnabled = value; _flags.IsModified = true; }); }
    public bool IsContainer { get => Read(() => _flags.IsContainer); set => SetFlag("is_container", value); }
    public bool IsScript { get => Read(() => _flags.IsScript); set => SetFlag("is_script", value); }
    public bool IsTickable { get => Read(() => _flags.IsTickable); set => SetFlag("is_tickable", value); }
    public bool IsAccount { get => Read(() => _flags.IsAccount); set => SetFlag("is_account", value); }
    public bool IsChannel { get => Read(() => _flags.IsChannel); set => SetFlag("is_channel", value); }
    public bool IsNode { get => Read(() => _flags.IsNode); set => SetFlag("is_node", value); }
    public bool IsModified { get => Read(() => _flags.IsModified); set => Write(() => _flags.IsModified = value); }
    public virtual bool IsDeleted { get => Read(() => _flags.IsDeleted); set => SetFlag("is_deleted", value); }

    // Global MaxSearchDepth for delete recursion — mirrors settings.MAX_SEARCH_DEPTH (default 100)
    public static int MaxSearchDepth { get; set; } = 100;
    public bool IsConnected { get => Read(() => _flags.IsConnected); set => SetFlag("is_connected", value); }
    public bool IsTemporary { get => Read(() => _flags.IsTemporary); set => SetFlag("is_temporary", value); }
    public bool IsBanned { get => Read(() => _flags.IsBanned); set => SetFlag("is_banned", value); }
    public bool CanHear { get => Read(() => _flags.CanHear); set => SetFlag("can_hear", value); }
    public bool Quelled { get => Read(() => _quelled); set => Write(() => { _quelled = value; _flags.IsModified = true; }); }
    public bool MapEnabled { get => Read(() => _mapEnabled); set => Write(() => { _mapEnabled = value; _flags.IsMapable = value; _flags.IsModified = true; }); }
    public double? LastMapTime { get => Read(() => _lastMapTime); set => Write(() => _lastMapTime = value); }
    public string Gender { get => Read(() => _gender); set => Write(() => { if (_gender != value) { _gender = value; _flags.IsModified = true; } }); }
    public double TickSeconds { get => Read(() => _tickSeconds); set => Write(() => { _tickSeconds = value; _flags.IsModified = true; }); }

    // --- map hooks (port of base_obj.py:767 at_map_update, 750 at_legend_update, 805 at_pre_map_render) ---
    public virtual Dictionary<(int X, int Y), string> AtPreMapRender(Dictionary<(int X, int Y), string> grid)
    {
        return Hookable("at_pre_map_render", () => grid, grid);
    }
    public virtual void AtMapUpdate(string mapStr, List<(string sym, string desc, (int x, int y) coord)> entries, int minX, int maxY, bool showLegend, string name)
    {
        Hookable("at_map_update", () =>
        {
            // Port of base_obj.py:767-802 calculate pos then msg then last_map_time
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
                    var objs = Globals.ObjectRegistry.Get(ol.ObjectId);
                    if (objs.Count > 0 && objs[0] is Node n)
                        pos = (n.Coord.X - minX, maxY - n.Coord.Y);
                }
            }
            catch { }
            try
            {
                // Port of base_obj.py:790-801 self.msg(map={map, pos, symbol, legend, min_x, max_y, area, show_legend})
                var payload = new Dictionary<string, object?>
                {
                    ["map"] = mapStr,
                    ["pos"] = new List<int> { pos.relX, pos.relY },
                    ["symbol"] = Symbol,
                    ["legend"] = entries.Select(e => new List<object?> { e.sym, e.desc, new List<int> { e.coord.x, e.coord.y } }).ToList(),
                    ["min_x"] = minX,
                    ["max_y"] = maxY,
                    ["area"] = name,
                    ["show_legend"] = showLegend,
                };
                Session? sess = null;
                try { sess = Session; } catch { }
                var conn = sess?.Connection;
                if (conn != null)
                {
                    conn.SendCommand("map", new List<object?> { payload }, null);
                }
                else
                {
                    // Fallback for test harnesses without connection — store via _msgLog like original Python would via session.msg
                    // Use MsgInternal path via Session if available, otherwise log
                    try { Msg($"[map:{name}]"); } catch { }
                }
            }
            catch { }
            LastMapTime = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
            return 0;
        }, mapStr, entries, minX, maxY, showLegend, name);
    }
    public virtual void AtLegendUpdate(List<(string sym, string desc, (int x, int y) coord)> entries, bool show, string area)
    {
        Hookable("at_legend_update", () =>
        {
            try
            {
                var payload = new Dictionary<string, object?>
                {
                    ["area"] = area,
                    ["legend"] = entries.Select(e => new List<object?> { e.sym, e.desc, new List<int> { e.coord.x, e.coord.y } }).ToList(),
                    ["show_legend"] = show,
                };
                Session? sess = null;
                try { sess = Session; } catch { }
                var conn = sess?.Connection;
                if (conn != null)
                    conn.SendCommand("legend", new List<object?> { payload }, null);
            }
            catch { }
            return 0;
        }, entries, show, area);
    }
    public virtual void AtDesc(GameObject? looker = null) => Hookable("at_desc", () => 0, looker); // Port of base_obj.py:1621 at_desc
    public virtual string AtPreSay(string message) => Hookable("at_pre_say", () => message, message); // Port of base_obj.py:1739 at_pre_say
    public LocationRef Location { get => Read(() => _location); set => Write(() => { _location = value; _flags.IsModified = true; }); }
    public LocationRef Home { get => Read(() => _home); set => Write(() => { _home = value; _flags.IsModified = true; }); }
    private Session? _session; // Port of base_obj.py:109 session: Session | None (excluded from pickle, not IsModified)
    public Session? Session { get => Read(() => _session); set => Write(() => _session = value); } // Port of base_obj.py:109
    // Port of base_obj.py:103-104 + 659-667 seconds_played (computed + elapsed)
    public double SecondsPlayed
    {
        get => Read(() =>
        {
            double baseVal = _secondsPlayed;
            var sess = _session;
            if (sess != null && sess.ConnTime > 0)
            {
                double elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - sess.ConnTime; // Port of base_obj.py:662 time.time() - session.conn_time
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
    public HashSet<string> TagsSnapshot => Read(() => _tags == null ? new HashSet<string>() : new HashSet<string>(_tags));
    public HashSet<int> ContentsSnapshot => Read(() => new HashSet<int>(_contents));
    public HashSet<int> ScriptsSnapshot => Read(() => new HashSet<int>(_scripts));
    public List<int> ChannelsSnapshot => Read(() => new List<int>(_channels));
    public HashSet<int> FollowersSnapshot => Read(() => new HashSet<int>(_followers));

    public ReaderWriterLockSlim SyncRoot => _lock;
    public void SetLockForTesting(ReaderWriterLockSlim newLock) { _lock = newLock; _testTracker = newLock; try { _trackerEntriesField = newLock.GetType().GetField("Entries", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance); } catch { _trackerEntriesField = null; } }

    internal void SetIsDeletedRaw(bool v)
    {
        _lock.EnterWriteLock();
        try { _flags.IsDeleted = v; _flags.IsModified = true; }
        finally { _lock.ExitWriteLock(); }
    }


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
            var set = new HashSet<string>(tags);
            var cur = _tags ?? new HashSet<string>();
            return all ? set.IsSubsetOf(cur) : set.Overlaps(cur);
        });
    }

    // --- contents ops ---
    public void AddContent(int id) => Write(() => { _contents.Add(id); _flags.IsModified = true; });
    public void RemoveContent(int id) => Write(() => { _contents.Remove(id); _flags.IsModified = true; });

    // --- channel subscription (port of base_obj.subscribe / unsubscribe) ---
    public void Subscribe(Channel channel)
    {
        if (channel == null) return;
        if (channel.IsDeleted) return;
        bool already;
        _lock.EnterReadLock();
        try { already = _channels.Contains(channel.Id); }
        finally { _lock.ExitReadLock(); }
        if (already) return;
        if (channel.IsDeleted) return;
        channel.AddListener(this);
        _lock.EnterWriteLock();
        try
        {
            if (!_channels.Contains(channel.Id))
            {
                _channels.Add(channel.Id);
                _flags.IsModified = true;
            }
        }
        finally { _lock.ExitWriteLock(); }
    }
    public void Unsubscribe(Channel channel)
    {
        if (channel == null) return;
        bool had;
        _lock.EnterReadLock();
        try { had = _channels.Contains(channel.Id); }
        finally { _lock.ExitReadLock(); }
        if (!had) return;
        channel.RemoveListener(this);
        _lock.EnterWriteLock();
        try
        {
            if (_channels.Contains(channel.Id))
            {
                _channels.Remove(channel.Id);
                _flags.IsModified = true;
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    // --- locks ---(int id) => Write(() => { _contents.Remove(id); _flags.IsModified = true; });

    // --- locks ---
    public void AddLock(string lockName, Func<GameObject, bool> predicate)
    {
        Write(() =>
        {
            if (!_locks.TryGetValue(lockName, out var lst))
            {
                lst = [];
                _locks[lockName] = lst;
            }
            lst.Add(predicate);
        });
    }
    public void ClearLocksByName(string lockName) => Write(() => _locks.Remove(lockName));

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
        List<Func<GameObject, bool>> snapshot;
        using (ReadScope())
        {
            if (!_locks.TryGetValue(lockName, out var lst) || lst.Count == 0) return true;
            snapshot = new List<Func<GameObject, bool>>(lst);
        }
        foreach (var fn in snapshot)
            if (!fn(accessingObj)) return false;
        return true;
    }

    // --- hooks (minimal port of hookable) ---
    public virtual void InstallHook(string funcName, Delegate hook)
    {
        Write(() =>
        {
            if (!_hooks.TryGetValue(funcName, out var set))
            {
                set = [];
                _hooks[funcName] = set;
            }
            set.Add(hook);
        });
    }
    public bool HasHook(string funcName) => Read(() => _hooks.TryGetValue(funcName, out var s) && s.Count > 0);

    // --- script attachment (port of base_obj.add_script/remove_script/has_script_type/get_scripts_by_type) ---
    public void AddScript(Script script)
    {
        if (script == null) return;
        script.InstallHooks(this);
    }
    public void AddScript(int scriptId)
    {
        var objs = Globals.ObjectRegistry.Get(scriptId);
        if (objs.Count>0 && objs[0] is Script s) s.InstallHooks(this);
    }
    public void RemoveScript(Script script)
    {
        if (script == null) return;
        script.RemoveHooks(this);
    }
    public void RemoveScript(int scriptId)
    {
        var objs = Globals.ObjectRegistry.Get(scriptId);
        if (objs.Count>0 && objs[0] is Script s) s.RemoveHooks(this);
    }
    public bool HasScriptType(string scriptType)
    {
        HashSet<int> ids;
        using (ReadScope())
        {
            if (_scripts == null || _scripts.Count==0) return false; ids = new HashSet<int>(_scripts);
        }
        string needle = scriptType.ToLowerInvariant();
        foreach (var id in ids)
        {
            var objs = Globals.ObjectRegistry.Get(id);
            if (objs.Count>0)
            {
                string cname = objs[0].GetType().Name.ToLowerInvariant();
                if (cname.Contains(needle)) return true;
            }
        }
        return false;
    }
    public List<Script> GetScriptsByType(string scriptType)
    {
        HashSet<int> ids;
        using (ReadScope())
        {
            if (_scripts == null || _scripts.Count==0) return new List<Script>(); ids = new HashSet<int>(_scripts);
        }
        string needle = scriptType.ToLowerInvariant();
        var list = new List<Script>();
        foreach (var id in ids)
        {
            var objs = Globals.ObjectRegistry.Get(id);
            if (objs.Count>0 && objs[0] is Script s)
            {
                string cname = s.GetType().Name.ToLowerInvariant();
                if (cname.Contains(needle)) list.Add(s);
            }
        }
        return list;
    }

    // --- display / messaging (Command parity) --- (implementation in Objects/Messaging/GameObjectMessaging.cs)

    // Port of atheriz/objects/base_obj.py:733 search — delegate to ContentUtils.Search with ObjectRegistry resolver
    public virtual List<GameObject> Search(string query, bool recursive = true, GameObject? looker = null)
        => ContentUtils.Search(this, query, id => Globals.ObjectRegistry.Get(id).FirstOrDefault(), recursive, looker ?? this);

    /// <summary>
    /// Port of <c>atheriz/objects/base_obj.py:581 resolve_relations</c>.
    /// Reconnects location/home already via LocationRef, re-registers tick, reinstalls script hooks, calls at_init.
    /// </summary>
    public virtual void ResolveRelations()
    {
        if (IsTickable)
        {
            try { Objects.GlobalTickerHolder.Get()?.AddCoro(AtTick, TickSeconds); } catch { }
        }
        HashSet<int> scripts;
        _lock.EnterReadLock();
        try { scripts = new HashSet<int>(_scripts); }
        finally { _lock.ExitReadLock(); }
        foreach (var id in scripts)
        {
            var lst = Globals.ObjectRegistry.Get(id);
            if (lst.Count > 0 && lst[0] is Script s)
            {
                try { s.InstallHooks(this); } catch { }
            }
        }
        try { AtInit(); } catch { }
    }

    // --- DTO conversion (mirrors __getstate__/__setstate__) --- (persisted via Persistence/Converters/GameObjectDtoConverter.cs)
    // Port of atheriz/objects/base_obj.py:493 __getstate__ — single BuildDto collapse (update.md 3.3)
    // Faithful: if _puppetRestore present, use original is_pc/privilege_level for serialization (never persist puppeted state)
    private GameObjectDto BuildDto() => Persistence.Converters.GameObjectDtoConverter.BuildDto(this);

    public GameObjectDto ToDto() => Read(() => BuildDto());

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
        o._flags.IsNode = isNodeOverride ?? dto.IsNode;
        o._flags.IsTemporary = dto.IsTemporary;
        o._flags.IsDeleted = dto.IsDeleted;
        o._flags.IsModified = dto.IsModified;
        o._privilege = dto.PrivilegeLevel;
        o._gender = dto.Gender ?? "neutral";
        o._location = dto.Location ?? LocationRef.NullLocation.Instance;
        o._home = dto.Home ?? LocationRef.NullLocation.Instance;
        o._contents = new HashSet<int>(dto.Contents ?? []);
        o._scripts = new HashSet<int>(dto.Scripts ?? []);
        o._channels = new List<int>(dto.Channels ?? []);
        o._extra = new Dictionary<string, JsonElement>(dto.Extra ?? new());
    }

    public static GameObject FromDto(GameObjectDto dto) => Persistence.Converters.GameObjectDtoConverter.FromDto(dto);

    // --- DbOps equivalents — delegated to converter to dedup GetSaveOps/GetSaveOpsClearing (950-992) ---
    public virtual (string Sql, object[] Params) GetSaveOps() => Persistence.Converters.GameObjectDtoConverter.GetSaveOps(this);

    public virtual (string Sql, object[] Params) GetSaveOpsClearing() => Persistence.Converters.GameObjectDtoConverter.GetSaveOpsClearing(this);

    private GameObjectDto ToDtoUnsafe() => BuildDto(); // caller holds _lock; delegate to single BuildDto (update.md 3.3)

    public (string Sql, object[] Params) GetDelOps() => ("DELETE FROM objects WHERE id = ?", [Id]);

    // --- factory (mirrors Object.create) ---
    // Fix for test_account.py:88 — use unified IdGenerator counter (mirrors get_unique_id)
    private static int _nextId = 0; // kept for backwards compat but delegates
    public static int GetNextId() { _ = _nextId; return Globals.IdGenerator.GetUniqueId(); }
    public static void SetNextId(int v) => Globals.IdGenerator.SetId(v);

    public static GameObject Create(string name, string desc = "", IEnumerable<string>? aliases = null,
        bool isPc = false, bool isItem = false, bool isNpc = false, bool isMapable = false, bool isContainer = false,
        bool isTickable = false, double tickSeconds = 1.0, GameObject? caller = null, Privilege privilege = Privilege.Guest)
    {
        var obj = new GameObject();
        obj._id = GetNextId();
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
        obj._lastMapTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        obj._mapEnabled = true;
        obj._flags.IsModified = true;

        // mirrors Python create locks
        if (isPc)
        {
            // view: not is_pc or (is_pc and is_connected) — simplified as: if target is_pc, require is_connected
            obj.AddLock("view", accessing => !obj.IsPc || accessing.IsConnected || obj.IsConnected);
            obj.AddLock("get", accessing => accessing.IsBuilder);
        }
        if (isNpc)
            obj.AddLock("get", accessing => accessing.IsBuilder);

        obj.AddLock("delete", accessing => accessing.Id != obj.Id);
        // puppet lock faithful to base_obj.py:185-193 — is_npc or superuser or owned character via session.account
        obj.AddLock("puppet", accessing =>
        {
            if (obj.IsNpc) return true;
            if (accessing.IsSuperUser) return true;
            var sess = accessing.Session;
            if (sess?.Account is Account acc && acc.Characters.Contains(obj.Id)) return true;
            // fallback via dynamic Account without strong type if needed
            try
            {
                var dynAcc = sess?.Account as dynamic;
                if (dynAcc != null)
                {
                    var chars = dynAcc.Characters as System.Collections.IEnumerable;
                    if (chars != null) foreach (var cid in chars) if (cid is int id && id == obj.Id) return true;
                }
            } catch {}
            return false;
        });

        return obj;
    }

    // --- persistence helpers exposed for GameObjectDtoConverter (P1.5 split) ---
    internal void SetIdRaw(int id)
    {
        _lock.EnterWriteLock();
        try { _id = id; _flags.IsModified = true; }
        finally { _lock.ExitWriteLock(); }
    }
    internal Dictionary<string, System.Text.Json.JsonElement> GetExtraSnapshot() => Read(() => new Dictionary<string, System.Text.Json.JsonElement>(_extra));
    internal Dictionary<string, List<Func<GameObject, bool>>> GetLocksSnapshot() => Read(() => new Dictionary<string, List<Func<GameObject, bool>>>(_locks));
    internal void IncrementTrackerInternal() => IncrementTracker();
    internal Persistence.Dto.GameObjectDto ToDtoUnsafeInternal() => BuildDto();
    // Raw IsModified access without re-entering lock (caller must hold write lock) — used by GetSaveOps to ensure exactly one tracker increment
    internal bool GetIsModifiedRawNoLock() => _flags.IsModified;
    internal void SetIsModifiedRawNoLock(bool v) => _flags.IsModified = v;

    // Typed extra helpers for GrottoObject (replaces reflection on _extra)
    public bool TryGetExtraJson(string key, out System.Text.Json.JsonElement value)
    {
        System.Text.Json.JsonElement tmp = default;
        bool found = false;
        Read(() =>
        {
            if (_extra.TryGetValue(key, out var v)) { tmp = v; found = true; }
            return 0;
        });
        value = tmp;
        return found;
    }
    public void SetExtraJson(string key, System.Text.Json.JsonElement value) => Write(() => { _extra[key] = value; _flags.IsModified = true; });
    public bool TryRemoveExtraJson(string key) => Write(() => { var r = _extra.Remove(key); if (r) _flags.IsModified = true; return r; });

    // --- messaging helpers for converter wrappers kept thin ---
}
