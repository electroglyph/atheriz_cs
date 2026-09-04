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

    public Atheriz.Core.Commands.Command? Command => _command;

    public Atheriz.Core.Commands.Command? GetCommand()
    {
        lock (_histLock)
        {
            if (_command != null) return _command;
            var cmd = new BaseChannelCommand();
            ((BaseChannelCommand)cmd).SetKey(Name.ToLowerInvariant());
            ((BaseChannelCommand)cmd).SetDesc(Desc);
            cmd.Channel = this;
            cmd.Id = Id;
            _command = cmd;
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
                try
                {
                    o.SyncRoot.EnterWriteLock();
                    try
                    {
                        var f = o.GetType().GetField("_channels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (f?.GetValue(o) is List<int> lst) lst.Remove(this.Id);
                        o.IsModified = true;
                    }
                    finally { o.SyncRoot.ExitWriteLock(); }
                } catch {}
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
        foreach (var listener in listeners)
        {
            try { listener.Msg(FormatMessage(timestamp, senderName, text)); } catch {}
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

    public override (string Sql, object[] Params) GetSaveOps()
    {
        bool had;
        string json;
        IncrementTracker();
        lock (_histLock)
        {
            SyncRoot.EnterWriteLock();
            try
            {
                had = GetIsModifiedRawNoLock();
                SetIsModifiedRawNoLock(false);
                try { json = Persistence.Dto.GameObjectDtoSerializer.ToJson(ToDto()); }
                finally { SetIsModifiedRawNoLock(had); }
            }
            finally { SyncRoot.ExitWriteLock(); }
        }
        return ("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", new object[] { Id, json });
    }

    public override (string Sql, object[] Params) GetSaveOpsClearing()
    {
        string json;
        bool had = false;
        IncrementTracker();
        lock (_histLock)
        {
            SyncRoot.EnterWriteLock();
            try
            {
                had = GetIsModifiedRawNoLock();
                SetIsModifiedRawNoLock(false);
                try
                {
                    var dto = ToDto();
                    dto.IsModified = false;
                    json = Persistence.Dto.GameObjectDtoSerializer.ToJson(dto);
                    SetIsModifiedRawNoLock(false);
                }
                catch
                {
                    SetIsModifiedRawNoLock(had);
                    throw;
                }
            }
            finally { SyncRoot.ExitWriteLock(); }
        }
        return ("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", new object[] { Id, json });
    }

    public new GameObjectDto ToDto()
    {
        var dto = base.ToDto();
        dto.Type = "channel";
        lock (_histLock)
        {
            dto.Extra["history"] = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(_history.ToList())).RootElement.Clone();
            // listeners intentionally excluded per __getstate__ (pop listeners)
            // lock also excluded (not in DTO)
            dto.Extra.Remove("listeners");
            dto.Extra.Remove("lock");
        }
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
