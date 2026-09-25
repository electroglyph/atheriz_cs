using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Objects;

/// <summary>
/// Persistent channel with listeners + bounded history.
/// </summary>
public class Channel : GameObject
{
    private readonly Lock _histLock = new();
    // tuples. Listeners receive the FormatMessage form; History projects the
    // raw messages; GetHistory formats on replay — so replay matches live.
    private readonly LinkedList<ChannelHistoryEntry> _history = [];
    private readonly Dictionary<int, GameObject> _listeners = new();
    private readonly int _historyLimit;
    private bool _channelDeleted = false;
    // Mutation generation for the checkpoint flag-dance: every
    // _listeners/_history/_channelDeleted mutation under _histLock bumps
    // this. BuildSaveOperation snapshots it with the history and re-dirties
    // when it moved, so a Msg landing between the history snapshot and the
    // save flag-clear cannot lose a checkpoint entry.
    private long _mutGen = 0;
    private Atheriz.Core.Commands.Command? _command;

    private int _createdBy = -1;
    // Locked: the group kick/leave paths read-modify this across
    // awaits' worth of interleaving — a plain auto-prop also risks a torn
    // leadership transfer observation on weak-memory hardware.
    public int CreatedBy
    {
        get { lock (_histLock) return _createdBy; }
        set { lock (_histLock) _createdBy = value; }
    }

    /// <inheritdoc/>
    public override bool IsKnownProperty(string name) => name switch
    {
        "CreatedBy" or "created_by" or "_created_by" => true,
        "Listeners" or "listeners" or "_listeners" => true,
        "History" or "history" or "_history" => true,
        "Command" or "command" or "_command" => true,
        _ => base.IsKnownProperty(name),
    };

    /// <inheritdoc/>
    public override bool TrySetProperty(string name, object? value, out string? error)
    {
        error = null;
        switch (name)
        {
            case "CreatedBy" or "created_by" or "_created_by":
                CreatedBy = ToInt(value);
                return true;
            case "Listeners" or "listeners" or "_listeners"
                or "History" or "history" or "_history"
                or "Command" or "command" or "_command":
                error = $"'{name}' is a read-only attribute.";
                return false;
            default:
                return base.TrySetProperty(name, value, out error);
        }
    }

    public Channel(int historyLimit = 50)
    {
        IsChannel = true;
        _historyLimit = historyLimit;
    }
    // Load-path construction: no id draw (the factory adopts the stored id
    // via the base core before publication, so the generator watermark is
    // untouched by loads).
    private Channel(int id, int historyLimit = 50) : base(id)
    {
        IsChannel = true;
        _historyLimit = historyLimit;
    }
    internal static Channel CreateForLoad(int id, int historyLimit = 50) => new Channel(id, historyLimit);

    public static Channel Create(string name, GameObject? caller = null)
    {
        var ch = new Channel();
        ch.Name = name;
        ch.CreatedBy = caller?.Id ?? -1;
        Globals.ObjectRegistry.AddObjectUnique(ch, o => o.IsChannel && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase), $"Channel {name} already exists.");
        ch.AtCreate();
        return ch;
    }

    // Renames the channel atomically: GetCommand snapshots Name+Desc under one
    // read hold, so the pair must also be written under one write hold —
    // separate Name=/Desc= sets can straddle a GetCommand and cache a mixed
    // new-key/old-desc command. Use this for every post-registration rename.
    public void Rename(string name, string desc)
    {
        using (WriteScope()) { Name = name; Desc = desc; }
    }

    public override void AtCreate() => Hookable(HookName.AtCreate, () => 0);    public override bool AtDelete(GameObject? caller) => Hookable(HookName.AtDelete, () => true, caller);

    public override bool IsDeleted
    {
        get { lock (_histLock) return _channelDeleted; }
        set
        {
            lock (_histLock) _channelDeleted = value;
            SetIsDeletedRaw(value);
        }
    }

    /// <summary>
    /// Lock-free delete-guard read for use while holding a peer lock (taking
    /// _histLock there would invert the peer → channel order used by Delete
    /// detach vs Msg delivery). May be microscopically stale; callers needing
    /// exactness must take _histLock via IsDeleted.
    /// </summary>
    internal bool IsDeletedSnapshot() => Volatile.Read(ref _channelDeleted);
    /// <summary>
    /// Re-syncs the delete guard after <c>GameObject.ApplyDtoFields</c> restores
    /// <c>_flags.IsDeleted</c> directly (bypassing this override). Called with the
    /// restored value; takes only _histLock so no lock order is violated.
    /// </summary>
    internal override void SyncDeletedGuard(bool deleted)
    {
        lock (_histLock) { _channelDeleted = deleted; }
    }

    public IReadOnlySet<int> Listeners
    {
        get { lock (_histLock) return new HashSet<int>(_listeners.Keys); }
    }

    public override IEnumerable<(string name, object? value, bool isProperty)> GetExamMembers()
    {
        foreach (var m in base.GetExamMembers()) yield return m;
        object? Safe(Func<object?> f) { try { return f(); } catch { return "<error>"; } }
        yield return ("created_by", Safe(() => (object?)CreatedBy), true);
        yield return ("Listeners", Safe(() => (object?)Listeners), true);
    }
    public IReadOnlyCollection<GameObject> ListenerObjects
    {
        get { lock (_histLock) return _listeners.Values.ToList().AsReadOnly(); }
    }
    public IReadOnlyList<string> History
    {
        // Raw-message projection of the (timestamp, sender, message) entries.
        get { lock (_histLock) return _history.Select(e => e.Message).ToList(); }
    }

    public void AddListener(GameObject obj)
    {
        // Snapshot the id before taking the channel lock: Id is a locked
        // property (peer's SyncRoot), so reading it under _histLock nests
        // channel -> peer — the inverse of the fixed object -> channel order
        // (Delete detaches under the peer lock) and ABBA-deadlocks.
        // The snapshot loses nothing: _histLock never protected the peer's
        // id to begin with, and ids are assigned once at creation.
        int id = obj.Id;
        lock (_histLock)
        {
            if (_channelDeleted) return;
            _listeners[id] = obj;
            _mutGen++;
        }
        IsModified = true;
    }
    public override void RemoveListener(GameObject obj)
    {
        // Same channel -> peer inversion as AddListener (obj.Id under
        // _histLock): snapshot first, mutate under the channel lock only.
        int id = obj.Id;
        lock (_histLock) { _listeners.Remove(id); _mutGen++; }
        IsModified = true;
    }
    /// <summary>
    /// Hot-reload rewire: swap a stale listener instance for its replacement
    /// (same id). Listeners are keyed by id so only the value needs swapping.
    /// </summary>
    public void ReplaceListener(GameObject replacement)
    {
        // Id snapshot precedes the channel lock (see AddListener).
        int id = replacement.Id;
        lock (_histLock)
        {
            if (_listeners.TryGetValue(id, out var cur) && !ReferenceEquals(cur, replacement))
                _listeners[id] = replacement;
        }
    }

    // Live view: re-resolves on rename instead of returning the cached
    // instance, so readers never observe a stale key/desc pair.
    public Atheriz.Core.Commands.Command? Command => GetCommand();
    private string? _commandKey;
    private string? _commandDesc;

    public Atheriz.Core.Commands.Command? GetCommand()
    {
        // Snapshot Name/Desc before locking: GameObject props take SyncRoot, so
        // reading them under _histLock would nest channel -> object (inversion;
        // the fixed order everywhere is object -> channel). The command ctor
        // below re-reads the same pair plus Id the same way (never under _histLock).
        // Name and Desc share one ReadScope — two independent reads could cache
        // a new-key/old-desc command across a concurrent rename. Pair with the
        // single-hold Rename on the write side; the read hold alone cannot
        // repair a tear the writer created between two separate sets.
        string key;
        string desc;
        using (ReadScope()) { key = Name.ToLowerInvariant(); desc = Desc; }
        lock (_histLock)
        {
            // Invalidate the cached command on rename (old cache ignored Name/Desc).
            if (_command is not null && _commandKey == key && _commandDesc == desc) return _command;
        }
        // Construct outside the channel lock: the ctor takes the
        // channel's object read scope, which nests channel -> object — the
        // inverse of Delete's object -> channel order (ABBA deadlock when a
        // listener reads its own channel concurrently with its teardown).
        Atheriz.Core.Commands.BaseChannelCommand cmd = new(this);
        lock (_histLock)
        {
            // Re-check: another thread may have installed meanwhile.
            if (_command is not null && _commandKey == key && _commandDesc == desc) return _command;
            _command = cmd;
            _commandKey = key;
            _commandDesc = desc;
            return cmd;
        }
    }

    public override (int Count, List<DeleteOperation> Operations)? Delete(GameObject? caller = null, bool recursive = false, int maxDepth = ContentUtils.DefaultMaxSearchDepth)
    {
        if (!AtDelete(caller)) return null;
        List<int> toDetach;
        lock (_histLock)
        {
            if (_channelDeleted) return null;
            _channelDeleted = true;
            toDetach = _listeners.Keys.ToList();
            _listeners.Clear();
            _mutGen++;
        }
        try { SetIsDeletedRaw(true); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Channel.Delete: " + logEx.Message, "Channel"); }
        foreach (var lid in toDetach)
        {
            var o = Globals.ObjectRegistry.GetSingle(lid);
            if (o is not null)
            {
                // Typed peer detach (was GetField("_channels") reflection, now banned
                // in prod). Unsubscribe removes this channel id from the peer and
                // marks it modified; GameObject._lock is recursive so holding the
                // peer write lock across it is safe. Lock order stays object -> channel.
                try
                {
                    o.SyncRoot.EnterWriteLock();
                    try { o.Unsubscribe(this); }
                    finally { o.SyncRoot.ExitWriteLock(); }
                }
                catch (Exception ex) { try { AtherizLogger.LogError($"channel delete detach failed for object {lid}: {ex.Message}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Channel.Delete: " + logEx.Message, "Channel"); } }
            }
        }
        Globals.ObjectRegistry.RemoveObject(this);
        List<DeleteOperation> ops = [];
        if (!this.IsTemporary)
        {
            ops.Add(this.GetDeleteOperation());
            // Journal the row death so the checkpoint drain removes it.
            Globals.ObjectRegistry.NoteDeleted(this.Id);
        }
        return (1, ops);
    }

    public void Send(string text, GameObject? from = null) => Msg(text, from);

    public void Msg(string text, GameObject? from = null)
    {
        string senderName = from?.Name ?? "";
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
// history keeps the
        // (timestamp, sender, message) entry; listeners receive the formatted
        // form, and GetHistory re-formats on replay so both match.
        var entry = new ChannelHistoryEntry(timestamp, senderName, text);
        string formatted = FormatMessage(timestamp, senderName, text);
        List<GameObject> listeners;
        lock (_histLock)
        {
            // A delete racing the send wins: the message is dropped instead
            // of appending to a dead channel's history (R5). User feedback
            // stays at the command layer, which already resolves liveness.
            if (_channelDeleted) return;
            _history.AddLast(entry);
            while (_history.Count > _historyLimit) _history.RemoveFirst();
            listeners = _listeners.Values.ToList();
            _mutGen++;
        }
        IsModified = true;
        // A Delete landing between the snapshot above and this loop
        // cleared the listeners and detached the peers — delivering now
        // would ghost-deliver a dead-channel message to detached peers.
        // The history entry stays (harmless on a deleted channel); only
        // delivery is dropped. Volatile read, no lock (never nests).
        if (IsDeletedSnapshot()) return;
        foreach (var listener in listeners)
        {
            // FormatMessage is a pure function of (timestamp, sender, text), so
            // format once instead of once per listener. The sender rides along
            // so per-receiver hooks (at_msg_receive) keep provenance.
            try { listener.Msg(formatted, from); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Channel.Msg: " + logEx.Message, "Channel"); }
        }
    }

    public virtual string FormatMessage(long timestamp, string sender, string message)
    {
        var ts = DateTimeOffset.FromUnixTimeSeconds(timestamp).ToString("dd MMMM, yyyy HH:mm:ss");
        var prefix = $"({Name}) [{ts}] ";
        if (!string.IsNullOrEmpty(sender)) return $"{prefix}{sender}: {message}";
        return $"{prefix}{message}";
    }

    public string GetHistory(int count)
    {
        int limit = Settings.AtherizSettings.Global.ChannelHistoryLimit;
        count = Math.Max(0, Math.Min(count, limit));
        List<ChannelHistoryEntry> entries;
        lock (_histLock)
        {
            if (count == 0) return "";
            entries = count < _history.Count ? _history.TakeLast(count).ToList() : _history.ToList();
        }
        var sb = new System.Text.StringBuilder();
        foreach (var e in entries)
        {
            sb.Append(FormatMessage(e.Timestamp, e.Sender, e.Message));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public string GetHistory() => GetHistory(Settings.AtherizSettings.Global.ChannelHistoryLimit);

    public void ClearHistory()
    {
        // Fixed order is object -> channel: snapshot/clear under _histLock,
        // set the flag after release .
        lock (_histLock)
        {
            _history.Clear();
            _mutGen++;
        }
        IsModified = true;
    }

    // Save ops never nest _histLock inside SyncRoot (or vice versa): history is
    // snapshotted under _histLock, then the modified-flag dance runs under
    // SyncRoot only. Fixed lock order everywhere is object -> channel.
    public override SaveOperation GetSaveOperation() => BuildSaveOperation(clearing: false);

    public override SaveOperation GetSaveOperationClearing() => BuildSaveOperation(clearing: true);

    private List<ChannelHistoryEntry> SnapshotHistory() { lock (_histLock) return _history.ToList(); }

    private SaveOperation BuildSaveOperation(bool clearing)
    {
        List<ChannelHistoryEntry> histSnap;
        long genSnap;
        lock (_histLock) { histSnap = _history.ToList(); genSnap = _mutGen; }
        // Flag dance + post-release encode live in the shared converter core;
        // only the history-snapshot DTO body stays here.
        string json = Persistence.Converters.GameObjectDtoConverter.BuildSaveJson(this, () => BuildDto(histSnap), clearing);
        if (clearing)
        {
            // Checkpoint-miss half: a Msg/Add/Remove/Clear landing
            // between the snapshot above and the converter's flag-clear set
            // IsModified before the clear and is lost. The generation moved,
            // so re-dirty (outside _histLock — no channel->object nesting).
            bool moved;
            lock (_histLock) { moved = (_mutGen != genSnap); }
            if (moved) IsModified = true;
        }
        return new SaveOperation(Id, json);
    }

    public override GameObjectDto ToDto()
    {
        List<ChannelHistoryEntry> histSnap = SnapshotHistory();
        return BuildDto(histSnap);
    }

    private GameObjectDto BuildDto(List<ChannelHistoryEntry> history)
    {
        var dto = base.ToDto();
        dto.Type = "channel";
        // Persisted as [timestamp, sender, message] triples mirroring the
        // Python (timestamp, sender, message) history tuples.
        var triples = history.Select(e => new object[] { e.Timestamp, e.Sender, e.Message }).ToList();
        dto.Extra["history"] = Persistence.JsonOptions.ToElement(triples);
        // listeners intentionally excluded per __getstate__ (pop listeners)
        // lock also excluded (not in DTO)
        dto.Extra.Remove("listeners");
        dto.Extra.Remove("lock");
        // Ensure command not persisted
        dto.Extra.Remove("_command");
        dto.Extra.Remove("command");
        return dto;
    }

    internal void RestoreHistory(List<ChannelHistoryEntry> hist)
    {
        lock (_histLock)
        {
            _history.Clear();
            foreach (var h in hist) _history.AddLast(h);
            while (_history.Count > _historyLimit) _history.RemoveFirst();
            _mutGen++;
        }
    }
}
