using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Objects;

/// <summary>
/// Port of <c>atheriz/objects/base_channel.py:Channel</c>.
/// Persistent channel with listeners + bounded history.
/// </summary>
public class Channel : GameObject
{
    public new static bool _is_thread_safe = true;
    private readonly object _histLock = new();
    private readonly LinkedList<string> _history = [];
    private readonly Dictionary<int, GameObject> _listeners = new();
    private readonly int _historyLimit;
    private bool _channelDeleted = false;
    private Atheriz.Core.Commands.Command? _command;

    public int CreatedBy { get; set; } = -1;

    public Channel(int historyLimit = 50)
    {
        IsChannel = true;
        _historyLimit = historyLimit;
    }

    public static Channel Create(string name, GameObject? caller = null)
    {
        var ch = new Channel();
        ch.Name = name;
        ch.Id = Globals.IdGenerator.GetUniqueId();
        ch.CreatedBy = caller?.Id ?? -1;
        Globals.ObjectRegistry.AddObjectUnique(ch, o => o.IsChannel && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase), $"Channel {name} already exists.");
        ch.AtCreate();
        return ch;
    }

    public override void AtCreate() => Hookable("at_create", () => 0);
    public override bool AtDelete(GameObject? caller) => Hookable("at_delete", () => true, caller);

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
    /// Re-syncs the delete guard after <c>GameObject.ApplyDtoFields</c> restores
    /// <c>_flags.IsDeleted</c> directly (bypassing this override). Called with the
    /// restored value; takes only _histLock so no lock order is violated.
    /// </summary>
    internal void SyncDeletedGuard(bool deleted)
    {
        lock (_histLock) { _channelDeleted = deleted; }
    }

    public IReadOnlySet<int> Listeners
    {
        get { lock (_histLock) return new HashSet<int>(_listeners.Keys); }
    }
    public IReadOnlyCollection<GameObject> ListenerObjects
    {
        get { lock (_histLock) return _listeners.Values.ToList().AsReadOnly(); }
    }
    public IReadOnlyList<string> History
    {
        get { lock (_histLock) return _history.ToList(); }
    }

    public void AddListener(GameObject obj)
    {
        lock (_histLock)
        {
            if (_channelDeleted) return;
            _listeners[obj.Id] = obj;
        }
        IsModified = true;
    }
    public void RemoveListener(GameObject obj)
    {
        lock (_histLock) { _listeners.Remove(obj.Id); }
        IsModified = true;
    }
    /// <summary>
    /// Hot-reload rewire: swap a stale listener instance for its replacement
    /// (same id). Listeners are keyed by id so only the value needs swapping.
    /// </summary>
    public void ReplaceListener(GameObject replacement)
    {
        lock (_histLock)
        {
            if (_listeners.TryGetValue(replacement.Id, out var cur) && !ReferenceEquals(cur, replacement))
                _listeners[replacement.Id] = replacement;
        }
    }

    public Atheriz.Core.Commands.Command? Command => _command;
    private string? _commandKey;
    private string? _commandDesc;

    public Atheriz.Core.Commands.Command? GetCommand()
    {
        // Snapshot Name/Desc before locking: GameObject props take SyncRoot, so
        // reading them under _histLock would nest channel -> object (inversion;
        // the fixed order everywhere is object -> channel).
        string key = Name.ToLowerInvariant();
        string desc = Desc;
        lock (_histLock)
        {
            // Invalidate the cached command on rename (old cache ignored Name/Desc).
            if (_command != null && _commandKey == key && _commandDesc == desc) return _command;
            var cmd = new BaseChannelCommand();
            ((BaseChannelCommand)cmd).SetKey(key);
            ((BaseChannelCommand)cmd).SetDesc(desc);
            cmd.Channel = this;
            cmd.Id = Id;
            _command = cmd;
            _commandKey = key;
            _commandDesc = desc;
            return cmd;
        }
    }

    public override (int count, List<object> ops)? Delete(GameObject? caller = null, bool recursive = false)
    {
        if (!AtDelete(caller)) return null;
        List<int> toDetach;
        lock (_histLock)
        {
            if (_channelDeleted) return null;
            _channelDeleted = true;
            toDetach = _listeners.Keys.ToList();
            _listeners.Clear();
        }
        try { SetIsDeletedRaw(true); } catch {}
        foreach (var lid in toDetach)
        {
            var objs = Globals.ObjectRegistry.Get(lid);
            var o = objs.FirstOrDefault();
            if (o != null)
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
                catch (Exception ex) { try { AtherizLogger.LogError($"channel delete detach failed for object {lid}: {ex.Message}"); } catch { } }
            }
        }
        Globals.ObjectRegistry.RemoveObject(this);
        var ops = new List<object>();
        if (!this.IsTemporary)
            ops.Add(this.GetDelOps());
        return (1, ops);
    }

    public void Send(string text, GameObject? from = null) => Msg(text, from);

    public void Msg(string text, GameObject? from = null)
    {
        string senderName = from?.Name ?? "";
        int timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        List<GameObject> listeners;
        lock (_histLock)
        {
            _history.AddLast(text);
            while (_history.Count > _historyLimit) _history.RemoveFirst();
            IsModified = true;
            listeners = _listeners.Values.ToList();
        }
        string formatted = FormatMessage(timestamp, senderName, text);
        foreach (var listener in listeners)
        {
            // FormatMessage is a pure function of (timestamp, sender, text), so
            // format once instead of once per listener.
            try { listener.Msg(formatted); } catch {}
        }
    }

    public virtual string FormatMessage(int timestamp, string sender, string message)
    {
        if (!string.IsNullOrEmpty(sender)) return $"({Name}) [{DateTimeOffset.FromUnixTimeSeconds(timestamp):dd MMMM, yyyy HH:mm:ss}] {sender}: {message}";
        return $"({Name}) [{DateTimeOffset.FromUnixTimeSeconds(timestamp):dd MMMM, yyyy HH:mm:ss}] {message}";
    }

    public string GetHistory(int count)
    {
        int limit = Settings.AtherizSettings.Global.ChannelHistoryLimit;
        count = Math.Max(0, Math.Min(count, limit));
        List<string> entries;
        lock (_histLock)
        {
            if (count == 0) return "";
            entries = _history.ToList();
            if (count < entries.Count) entries = entries.Skip(entries.Count - count).ToList();
        }
        var lines = new List<string>();
        foreach (var msg in entries)
        {
            lines.Add(msg + "\n");
        }
        return string.Join("", lines);
    }

    public string GetHistory() => GetHistory(Settings.AtherizSettings.Global.ChannelHistoryLimit);

    public void ClearHistory()
    {
        lock (_histLock)
        {
            _history.Clear();
            IsModified = true;
        }
    }

    // Save ops never nest _histLock inside SyncRoot (or vice versa): history is
    // snapshotted under _histLock, then the modified-flag dance runs under
    // SyncRoot only. Fixed lock order everywhere is object -> channel.
    public override (string Sql, object[] Params) GetSaveOps() => BuildSaveOps(clearing: false);

    public override (string Sql, object[] Params) GetSaveOpsClearing() => BuildSaveOps(clearing: true);

    private (string Sql, object[] Params) BuildSaveOps(bool clearing)
    {
        IncrementTracker();
        List<string> histSnap;
        lock (_histLock) { histSnap = _history.ToList(); }
        bool had = false;
        string json;
        SyncRoot.EnterWriteLock();
        try
        {
            had = GetIsModifiedRawNoLock();
            SetIsModifiedRawNoLock(false);
            try
            {
                var dto = BuildDto(histSnap);
                if (clearing) dto.IsModified = false;
                json = Persistence.Dto.GameObjectDtoSerializer.ToJson(dto);
                // Non-clearing restores the flag (GetSaveOps is a peek); clearing
                // leaves it cleared. Either way a throw restores the old flag.
                SetIsModifiedRawNoLock(clearing ? false : had);
            }
            catch
            {
                SetIsModifiedRawNoLock(had);
                throw;
            }
        }
        finally { SyncRoot.ExitWriteLock(); }
        return ("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", new object[] { Id, json });
    }

    public override GameObjectDto ToDto()
    {
        List<string> histSnap;
        lock (_histLock) { histSnap = _history.ToList(); }
        return BuildDto(histSnap);
    }

    private GameObjectDto BuildDto(List<string> history)
    {
        var dto = base.ToDto();
        dto.Type = "channel";
        dto.Extra["history"] = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(history)).RootElement.Clone();
        // listeners intentionally excluded per __getstate__ (pop listeners)
        // lock also excluded (not in DTO)
        dto.Extra.Remove("listeners");
        dto.Extra.Remove("lock");
        // Ensure command not persisted
        dto.Extra.Remove("_command");
        dto.Extra.Remove("command");
        return dto;
    }

    internal void RestoreHistory(List<string> hist)
    {
        lock (_histLock)
        {
            _history.Clear();
            foreach (var h in hist) _history.AddLast(h);
            while (_history.Count > _historyLimit) _history.RemoveFirst();
        }
    }
}
