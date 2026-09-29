using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Persistence.Converters;

/// <summary>
/// Persistence converter extracted from the <c>GameObject.cs</c> god file.
/// Moves <c>BuildDto</c>, <c>FromDto</c>, <c>GetSaveOps</c>/<c>GetSaveOpsClearing</c>
/// out of domain object. <c>GameObject</c> keeps thin wrappers for API compat.
/// </summary>
internal static class GameObjectDtoConverter
{
    // Explicit persistence-subtype registry (F004). Only registered full names are ever
    // instantiated from save data — no Type.GetType / assembly scan / Activator in prod.
    // Games register their Custom* types at startup; tests register doubles in fixtures.
    private static readonly Lock _subtypeLock = new();
    private static readonly Dictionary<string, Func<GameObject>> _subtypeFactories = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, string> _subtypeNames = new();
    // Types that intentionally never persist (opt-out via PersistedTypes):
    // still downgrade to their base kind, but without the loud log.
    private static readonly HashSet<Type> _transientTypes = new();

    internal static void MarkTransient(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (_subtypeLock) { _transientTypes.Add(type); }
    }

    internal static void RegisterSubtype(string fullName, Type type, Func<GameObject> factory)
    {
        if (string.IsNullOrEmpty(fullName)) throw new ArgumentException("Subtype full name required.", nameof(fullName));
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(factory);
        lock (_subtypeLock)
        {
            _subtypeFactories[fullName] = factory;
            // Duplicate keys are loud: the same key for a different Type is a
            // game wiring bug (or a hot-reload generation swap) and must show
            // in the log. Same-Type re-registration stays silent (idempotent
            // startup). The prune below still releases the old Type key so a
            // re-registration never pins the previous plugin generation.
            var superseded = _subtypeNames.Where(kv => kv.Value == fullName && kv.Key != type).Select(kv => kv.Key).ToList();
            if (superseded.Count > 0)
                AtherizLogger.LogError($"Persisted subtype key '{fullName}' re-registered from {string.Join(",", superseded.Select(t => t.FullName))} to {type.FullName}; old mapping replaced.");
            foreach (var k in superseded)
                _subtypeNames.Remove(k);
            _subtypeNames[type] = fullName;
        }
    }

    internal static Dictionary<string, string> SnapshotSubtypeNames()
    {
        lock (_subtypeLock) { return _subtypeNames.ToDictionary(kv => kv.Value, kv => kv.Key.FullName ?? kv.Key.Name); }
    }

    internal static bool TryCreateSubtype(string fullName, out GameObject? instance)
    {
        // Snapshot the factory under the lock, invoke it outside: factories are
        // game-registered callbacks and must never run while the registry lock
        // is held (a factory creating another subtype would re-enter).
        // Exact match first; then an assembly-qualified fallback (old rows
        // stored "FullName, Assembly"): strip from the first comma and retry
        // the registry. Anything else is a corrupt row, not an old save.
        Func<GameObject>? factory;
        lock (_subtypeLock) { _subtypeFactories.TryGetValue(fullName, out factory); }
        if (factory is null)
        {
            string stripped = StripAssemblySuffix(fullName);
            if (!string.Equals(stripped, fullName, StringComparison.Ordinal))
                lock (_subtypeLock) { _subtypeFactories.TryGetValue(stripped, out factory); }
        }
        if (factory is not null) { instance = factory(); return true; }
        instance = null;
        return false;
    }

    internal static string StripAssemblySuffix(string key)
    {
        int comma = key.IndexOf(',');
        return comma < 0 ? key : key.Substring(0, comma).Trim();
    }

    private static string? RegisteredNameFor(Type t)
    {
        lock (_subtypeLock) { return _subtypeNames.TryGetValue(t, out var n) ? n : null; }
    }

    // Key lookup for the node save path: node doubles registered in the shared
    // registry (instead of the node registry) round-trip under their key.
    internal static bool TryGetRegisteredName(Type t, out string? name)
    {
        lock (_subtypeLock) { return _subtypeNames.TryGetValue(t, out name); }
    }

    public static GameObjectDto BuildDto(GameObject obj)
    {
        // Snapshot via public/internal APIs — caller may hold lock; snapshots use recursion-safe reads
        LocationRef loc = obj.Location;
        string type = obj.IsAccount ? "account" : obj.IsChannel ? "channel" : obj.IsScript ? "script" : obj.IsNode ? "node" : "object";
        bool isNode = obj.IsNode;
        if (isNode && obj is Node n)
        {
            loc = LocationRef.FromCoord(n.Coord);
            type = "node";
        }

        bool serIsPc = obj.IsPc;
        var serPriv = obj.PrivilegeLevel;
        var puppet = obj.GetPuppetRestore();
        if (puppet is not null)
        {
            serIsPc = puppet.IsPc;
            serPriv = puppet.PrivilegeLevel;
        }

        var dto = new GameObjectDto
        {
            Id = obj.Id,
            SchemaVersion = 1,
            Type = type,
            Name = obj.Name,
            DisplayName = obj.DisplayName,
            Desc = obj.Desc,
            Aliases = new List<string>(obj.Aliases),
            Tags = new HashSet<string>(obj.TagsSnapshot),
            IsPc = serIsPc,
            IsNpc = obj.IsNpc,
            IsItem = obj.IsItem,
            IsContainer = obj.IsContainer,
            IsMapable = obj.IsMapable,
            IsNode = isNode,
            IsTemporary = obj.IsTemporary,
            IsDeleted = obj.IsDeleted,
            IsModified = obj.IsModified,
            CanHear = obj.CanHear,
            IsTickable = obj.IsTickable,
            TickSeconds = obj.TickSeconds,
            Symbol = obj.Symbol,
            MoveVerb = obj.MoveVerb,
            Quelled = obj.Quelled,
            IsBanned = obj.IsBanned,
            NoFollow = obj.NoFollow,
            SecondsPlayed = obj.RawSecondsPlayed,
            PrivilegeLevel = serPriv,
            Gender = obj.Gender,
            Location = loc,
            Home = obj.Home,
            Contents = new HashSet<int>(obj.ContentsSnapshot),
            Scripts = new HashSet<int>(obj.ScriptsSnapshot),
            Channels = new List<int>(obj.ChannelsSnapshot),
            Extra = obj.GetExtraSnapshot(),
            // Declarative lock policies (F004) — never Method.Name (which silently weakened locks on reload).
            Locks = BuildLockDefs(obj),
        };

        if (obj.IsScript)
        {
            // Preserve concrete Script subtype only for explicitly registered types (F004).
            // Base Script itself needs no marker; anything else goes through the shared writer.
            if (obj.GetType() != typeof(Script))
                WriteSubtypeMarker(obj, dto, "__script_type", "script", "script");
        }

        if (!obj.IsScript)
        {
            var t = obj.GetType();
            if (t != typeof(GameObject) && t != typeof(Node) && t != typeof(Script) && t != typeof(Channel) && t != typeof(Account))
                WriteSubtypeMarker(obj, dto, "__object_type", "object", type);
        }

        bool hadObjectMarker = dto.Extra.TryGetValue(markerObjectType, out var savedObjectMarker);
        bool hadObjectVersion = dto.Extra.TryGetValue(markerObjectType + "_version", out var savedObjectVersion);
        bool hadScriptMarker = dto.Extra.TryGetValue(markerScriptType, out var savedScriptMarker);
        bool hadScriptVersion = dto.Extra.TryGetValue(markerScriptType + "_version", out var savedScriptVersion);
        obj.SaveExtra(dto.Extra);
        // SaveExtra must not clobber the subtype markers above: a game hook
        // writing "__object_type" would silently rekey the row. Restore the
        // markers if they were removed or changed and say so loudly.
        RestoreMarker(dto, markerObjectType, hadObjectMarker, savedObjectMarker);
        RestoreMarker(dto, markerObjectType + "_version", hadObjectVersion, savedObjectVersion);
        RestoreMarker(dto, markerScriptType, hadScriptMarker, savedScriptMarker);
        RestoreMarker(dto, markerScriptType + "_version", hadScriptVersion, savedScriptVersion);
        return dto;
    }

    private const string markerObjectType = "__object_type";
    private const string markerScriptType = "__script_type";

    private static void RestoreMarker(GameObjectDto dto, string markerKey, bool hadMarker, System.Text.Json.JsonElement markerValue)
    {
        // Only markers written by WriteSubtypeMarker are guarded: unregistered
        // subtypes have no marker and SaveExtra stays free to use its own keys.
        if (!hadMarker) return;
        if (dto.Extra.TryGetValue(markerKey, out var current) && current.GetRawText() == markerValue.GetRawText())
            return;
        dto.Extra[markerKey] = markerValue;
        AtherizLogger.LogError($"SaveExtra overwrote {markerKey} for object {dto.Id}; marker restored.");
    }

    // Shared writer for the __script_type / __object_type persistence markers: the
    // RegisteredNameFor + SerializeToElement + error-log shape is identical for both.
    // Both marker NAMES are preserved — they are the on-disk persistence format.
    // The per-type version rides a sibling "<marker>_version" key (default 1 for
    // old rows); FromDto hands it to the instance OnLoadMigrate hook.
    private static void WriteSubtypeMarker(GameObject obj, GameObjectDto dto, string markerKey, string kindWord, string savedAs)
    {
        var t = obj.GetType();
        string? registered = RegisteredNameFor(t);
        if (registered is not null)
        {
            dto.Extra[markerKey] = JsonOptions.ToElement(registered);
            dto.Extra[markerKey + "_version"] = JsonOptions.ToElement(obj.PersistedTypeVersion);
        }
        else
        {
            bool transient;
            lock (_subtypeLock) { transient = _transientTypes.Contains(t); }
            if (!transient)
                AtherizLogger.LogError($"Unregistered {kindWord} subtype {t.FullName} (id {obj.Id}) saved as base {savedAs}; register it via GameObject.RegisterPersistedSubtype to preserve the subtype.");
        }
    }

    private static List<LockDefDto> BuildLockDefs(GameObject obj)
    {
        var entries = obj.GetLockEntriesSnapshot();
        List<LockDefDto> defs = new(entries.Count);
        foreach (var kv in entries)
            defs.Add(new LockDefDto { Name = kv.Key, Policies = kv.Value.Select(e => e.Policy).ToList() });
        return defs;
    }

    public static GameObject FromDto(GameObjectDto dto)
    {
        // Read-only view for subtype markers: the old code swapped
        // dto.Extra to a stripped clone and restored it in `finally`, so two
        // threads loading the same dto instance raced — one thread's clone
        // stripped the other's markers (silent subtype downgrade) and restore
        // interleaving leaked clones. Now the input is never mutated: loads
        // run on a one-off record copy whose Extra is the stripped clone.
        JsonElement savedObjectType = default;
        bool hasObjectType = false;
        JsonElement savedScriptType = default;
        bool hasScriptType = false;
        var originalExtra = dto.Extra;
        Dictionary<string, JsonElement>? clone = null;
        if (originalExtra is not null)
        {
            if (originalExtra.TryGetValue("__object_type", out var ot))
            {
                savedObjectType = ot;
                hasObjectType = true;
            }
            if (originalExtra.TryGetValue("__script_type", out var te))
            {
                savedScriptType = te;
                hasScriptType = true;
            }
            if (hasObjectType || hasScriptType)
            {
                clone = new Dictionary<string, JsonElement>(originalExtra);
                clone.Remove("__object_type");
                clone.Remove("__script_type");
            }
        }
        var view = dto with { Extra = clone ?? originalExtra ?? new Dictionary<string, JsonElement>() };
        return FromDtoCore(view, savedObjectType, hasObjectType, savedScriptType, hasScriptType);
    }

    private static GameObject FromDtoCore(GameObjectDto dto, JsonElement savedObjectType, bool hasObjectType, JsonElement savedScriptType, bool hasScriptType)
    {
        // Explicit subtype registry only (F004): a registered key restores the subtype,
        // anything else loads as its base kind with a loud log — save data is
        // never allowed to pick a type to instantiate.
        // The sibling "<marker>_version" key (absent = 1, old rows) is handed to
        // the fresh instance OnLoadMigrate hook; base-kind fallback loads get no
        // hook (there is no subtype instance to migrate).
        if (hasObjectType)
        {
            string? typeName = savedObjectType.ValueKind == JsonValueKind.String ? savedObjectType.GetString() : null;
            if (!string.IsNullOrEmpty(typeName))
            {
                if (TryCreateSubtype(typeName!, out var inst) && inst is not null)
                {
                    // Game-registered factories construct normally (their draw
                    // is inherent to the Func<GameObject> shape); the stored
                    // id is adopted immediately below so the row still loads
                    // under its own id with a matching hash snapshot.
                    if (inst is Node subNode)
                    {
                        Coord subCoord = ExtractCoord(dto);
                        try { ObjectRegistry.RemoveObject(subNode); } catch (Exception ex) { AtherizLogger.LogError($"Subtype cleanup failed for {typeName}.", ex); }
                        subNode.SetIdRaw(dto.Id);
                        GameObject.ApplyDtoFields(inst, dto, isNodeOverride: true);
                        subNode.Coord = subCoord;
                        inst.IsNode = true;
                        inst.OnLoadMigrate(ReadMarkerVersion(dto.Extra, "__object_type_version"));
                        return inst;
                    }
                    inst.SetIdRaw(dto.Id);
                    GameObject.ApplyDtoFields(inst, dto, null);
                    inst.OnLoadMigrate(ReadMarkerVersion(dto.Extra, "__object_type_version"));
                    return inst;
                }
                AtherizLogger.LogError($"Unknown __object_type '{typeName}' for object {dto.Id}; loading as base {dto.Type}.");
            }
        }
        // Kind dispatch through the single EntityKind classify point: same
        // precedence as the old if-chain (script, channel, account, node).
        return Atheriz.Core.Persistence.Dto.EntityKinds.Classify(dto) switch
        {
            Atheriz.Core.Persistence.Dto.EntityKind.Script => LoadScript(dto, savedScriptType, hasScriptType),
            Atheriz.Core.Persistence.Dto.EntityKind.Channel => LoadChannel(dto),
            Atheriz.Core.Persistence.Dto.EntityKind.Account => Account.FromDto(dto),
            Atheriz.Core.Persistence.Dto.EntityKind.Node => LoadNode(dto),
            _ => LoadPlain(dto!),
        };
    }

    // Sibling "<marker>_version" key for OnLoadMigrate: absent or
    // non-numeric means version 1.
    private static int ReadMarkerVersion(Dictionary<string, JsonElement> extra, string versionKey)
    {
        if (extra.TryGetValue(versionKey, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) && v > 0)
            return v;
        return 1;
    }

    // Script branch: preserve IsScript and subtype for hook fidelity (faithful to dill subclass preservation)
    private static GameObject LoadScript(GameObjectDto dto, JsonElement savedScriptType, bool hasScriptType)
    {
        // Restore the concrete Script subclass only for explicitly registered types.
        if (hasScriptType)
        {
            string? typeName = savedScriptType.ValueKind == JsonValueKind.String ? savedScriptType.GetString() : null;
            if (!string.IsNullOrEmpty(typeName))
            {
                if (TryCreateSubtype(typeName!, out var scoped) && scoped is not null)
                {
                    // Same game-factory note as the object branch above: the
                    // stored id is adopted immediately below.
                    scoped.SetIdRaw(dto.Id);
                    GameObject.ApplyDtoFields(scoped, dto, null);
                    scoped.IsScript = true;
                    scoped.OnLoadMigrate(ReadMarkerVersion(dto.Extra, "__script_type_version"));
                    return scoped;
                }
                AtherizLogger.LogError($"Unknown __script_type '{typeName}' for object {dto.Id}; loading as base script.");
            }
        }
        var s = Script.CreateForLoad(dto.Id);
        GameObject.ApplyDtoFields(s, dto, null);
        s.IsScript = true;
        return s;
    }

    // Channel branch: Type=="channel" -> create Channel instance and restore history
    private static GameObject LoadChannel(GameObjectDto dto)
    {
        var ch = Channel.CreateForLoad(dto.Id);
        GameObject.ApplyDtoFields(ch, dto, null);
        ch.IsChannel = true;
        // Restore history if present; listeners intentionally not restored (excluded per __getstate__)
        if (dto.Extra is not null && dto.Extra.TryGetValue("history", out var he))
        {
            try
            {
                ch.RestoreHistory(ParseChannelHistory(he));
            }
            catch (Exception ex2) { AtherizLogger.LogError($"Channel {dto.Id} history unrestorable; starting empty.", ex2); }
        }
        // Clear IsModified after load? Original __setstate__ sets modified false via SaveObjects? Keep as per DTO
        return ch;
    }

    // Node branch: if IsNode or Type=="node", instantiate Node (preserves Coord via Location)
    private static GameObject LoadNode(GameObjectDto dto)
    {
        Coord coord = ExtractCoord(dto);
        var node = Node.CreateForLoad(dto.Id, coord);
        node.Desc = dto.Desc;
        node.IsModified = dto.IsModified;
        node.IsNode = true;
        GameObject.ApplyDtoFields(node, dto, isNodeOverride: true);
        return node;
    }

    private static GameObject LoadPlain(GameObjectDto dto)
    {
        GameObject o = GameObject.CreateForLoad(dto.Id);
        GameObject.ApplyDtoFields(o, dto, isNodeOverride: null);
        return o;
    }

    /// <summary>Coord for node instantiation: Location, else limbo origin.</summary>
    internal static Coord ExtractCoord(GameObjectDto dto)
    {
        if (dto.Location is LocationRef.CoordLocation cl) return cl.Coord;
        return new Coord("limbo", 0, 0, 0);
    }

    public static Dto.SaveOperation GetSaveOperation(GameObject obj)
        => GetSaveOperationCore(obj, clearing: false);

    public static Dto.SaveOperation GetSaveOperationClearing(GameObject obj)
        => GetSaveOperationCore(obj, clearing: true);

    private static Dto.SaveOperation GetSaveOperationCore(GameObject obj, bool clearing)
        => new(obj.Id, BuildSaveJson(obj, clearing));

    private static string BuildSaveJson(GameObject obj, bool clearing)
    {
        return BuildSaveJson(obj, obj.ToDtoUnsafeInternal, clearing);
    }

    /// <summary>
    /// Shared peek-shape save core for holders with custom DTO snapshots
    /// (Channel history, Account extras): the snapshot runs under the write
    /// hold, JSON encoding runs after release, and a failed encode restores
    /// the flag. Callers pass only their differing snapshot body.
    /// </summary>
    public static string BuildSaveJson(GameObject obj, Func<GameObjectDto> snapshotUnderLock, bool clearing)
    {
        // Single save-serialization core (mirrors Python get_save_ops).
        // Snapshot the DTO under the write lock, then encode AFTER release:
        // serializing large Extra/history held all readers for the whole
        // encode . Flag handling unchanged (non-clearing restores
        // afterwards; clearing leaves false, restored only on error).
        obj.SyncRoot.EnterWriteLock();
        bool had = obj.GetIsModifiedRawNoLock();
        obj.SetIsModifiedRawNoLock(false);
        GameObjectDto dto;
        bool snapshotOk = false;
        try
        {
            dto = snapshotUnderLock();
            if (clearing) dto.IsModified = false;
            snapshotOk = true;
        }
        finally
        {
            if (!clearing || !snapshotOk) obj.SetIsModifiedRawNoLock(had);
            obj.SyncRoot.ExitWriteLock();
        }
        return EncodeSaveJson(obj, dto, had);
    }

    // Encode runs after lock release; a serialization failure must restore
    // the flag the snapshot cleared (both clearing and non-clearing paths).
    // OR with the current value — a mutation that dirtied the object
    // during the unlocked encode window must not be wiped by the stale
    // snapshot (a failed save must never clean concurrent work).
    private static string EncodeSaveJson(GameObject obj, GameObjectDto dto, bool had)
    {
        try { return GameObjectDtoSerializer.ToJson(dto); }
        catch
        {
            obj.SyncRoot.EnterWriteLock();
            try { obj.SetIsModifiedRawNoLock(had || obj.GetIsModifiedRawNoLock()); }
            finally { obj.SyncRoot.ExitWriteLock(); }
            throw;
        }
    }

    // History entries persist as [timestamp, sender, message] triples mirroring
    // the Python (timestamp, sender, message) tuples.
    private static List<ChannelHistoryEntry> ParseChannelHistory(JsonElement he)
    {
        List<ChannelHistoryEntry> entries = [];
        if (he.ValueKind != JsonValueKind.Array) return entries;
        foreach (var el in he.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.Array)
            {
                var parts = el.EnumerateArray().ToList();
                long ts = parts.Count > 0 && parts[0].ValueKind == JsonValueKind.Number && parts[0].TryGetInt64(out var t) ? t : 0;
                string sender = parts.Count > 1 && parts[1].ValueKind == JsonValueKind.String ? parts[1].GetString() ?? "" : "";
                string msg = parts.Count > 2 && parts[2].ValueKind == JsonValueKind.String ? parts[2].GetString() ?? "" : "";
                entries.Add(new ChannelHistoryEntry(ts, sender, msg));
            }
        }
        return entries;
    }
}
