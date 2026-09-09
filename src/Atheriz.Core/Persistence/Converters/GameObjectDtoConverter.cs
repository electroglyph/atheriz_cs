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
    private static readonly object _subtypeLock = new();
    private static readonly Dictionary<string, Func<GameObject>> _subtypeFactories = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, string> _subtypeNames = new();

    internal static void RegisterSubtype(string fullName, Type type, Func<GameObject> factory)
    {
        if (string.IsNullOrEmpty(fullName)) throw new ArgumentException("Subtype full name required.", nameof(fullName));
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(factory);
        lock (_subtypeLock)
        {
            _subtypeFactories[fullName] = factory;
            _subtypeNames[type] = fullName;
        }
    }

    internal static bool TryCreateSubtype(string fullName, out GameObject? instance)
    {
        lock (_subtypeLock) { if (_subtypeFactories.TryGetValue(fullName, out var f)) { instance = f(); return true; } }
        instance = null;
        return false;
    }

    private static string? RegisteredNameFor(Type t)
    {
        lock (_subtypeLock) { return _subtypeNames.TryGetValue(t, out var n) ? n : null; }
    }

    public static GameObjectDto BuildDto(GameObject obj)
    {
        // Snapshot via public/internal APIs — caller may hold lock; snapshots use recursion-safe reads
        LocationRef loc = obj.Location;
        string type = obj.IsAccount ? "account" : obj.IsChannel ? "channel" : obj.IsScript ? "script" : obj.IsNode ? "node" : "object";
        bool isNode = obj.IsNode;
        if (isNode && obj is Node n)
        {
            loc = new LocationRef.CoordLocation(n.Coord);
            type = "node";
        }

        bool serIsPc = obj.IsPc;
        var serPriv = obj.PrivilegeLevel;
        var puppet = obj.GetPuppetRestore();
        if (puppet is not null)
        {
            if (puppet.TryGetValue("is_pc", out var v) && v is bool b) serIsPc = b;
            if (puppet.TryGetValue("privilege_level", out var p))
            {
                if (p is Privilege priv) serPriv = priv;
                else if (p is int i) serPriv = (Privilege)i;
            }
        }

        var dto = new GameObjectDto
        {
            Id = obj.Id,
            SchemaVersion = 1,
            Type = type,
            Name = obj.Name,
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
            string? registered = RegisteredNameFor(obj.GetType());
            if (registered is not null)
            {
                dto.Extra["__script_type"] = JsonSerializer.SerializeToElement(registered, JsonOptions.Default);
            }
            else if (obj.GetType() != typeof(Script))
            {
                AtherizLogger.LogError($"Unregistered script subtype {obj.GetType().FullName} (id {obj.Id}) saved as base script; register it via GameObject.RegisterPersistedSubtype to preserve the subtype.");
            }
        }

        if (!obj.IsScript)
        {
            var t = obj.GetType();
            if (t != typeof(GameObject) && t != typeof(Node) && t != typeof(Script) && t != typeof(Channel) && t != typeof(Account))
            {
                string? registered = RegisteredNameFor(t);
                if (registered is not null)
                {
                    dto.Extra["__object_type"] = JsonSerializer.SerializeToElement(registered, JsonOptions.Default);
                }
                else
                {
                    AtherizLogger.LogError($"Unregistered object subtype {t.FullName} (id {obj.Id}) saved as base {type}; register it via GameObject.RegisterPersistedSubtype to preserve the subtype.");
                }
            }
        }

        return dto;
    }

    private static List<LockDefDto> BuildLockDefs(GameObject obj)
    {
        var policies = obj.GetLockPoliciesSnapshot();
        return obj.GetLocksSnapshot().Select(kv =>
        {
            policies.TryGetValue(kv.Key, out var pols);
            var names = pols is not null && pols.Count == kv.Value.Count ? pols : Enumerable.Repeat(LockPolicies.Custom, kv.Value.Count);
            return new LockDefDto { Name = kv.Key, Policy = string.Join("|", names) };
        }).ToList();
    }

    public static GameObject FromDto(GameObjectDto dto)
    {
        // Copy-on-read for subtype markers: the branches below strip
        // __object_type / __script_type so they don't leak into obj.Extra, but
        // the markers are restored in finally — a double-load of the same DTO
        // instance keeps its subtype the second time, and the caller's dict is
        // never left mutated.
        JsonElement savedObjectType = default;
        bool hasObjectType = false;
        JsonElement savedScriptType = default;
        bool hasScriptType = false;
        if (dto.Extra is not null)
        {
            if (dto.Extra.TryGetValue("__object_type", out var ot))
            {
                savedObjectType = ot;
                hasObjectType = true;
                dto.Extra.Remove("__object_type");
            }
            if (dto.Extra.TryGetValue("__script_type", out var te))
            {
                savedScriptType = te;
                hasScriptType = true;
                dto.Extra.Remove("__script_type");
            }
        }
        try
        {
            return FromDtoCore(dto, savedObjectType, hasObjectType, savedScriptType, hasScriptType);
        }
        finally
        {
            if (dto.Extra is not null)
            {
                if (hasObjectType) dto.Extra["__object_type"] = savedObjectType;
                if (hasScriptType) dto.Extra["__script_type"] = savedScriptType;
            }
        }
    }

    private static GameObject FromDtoCore(GameObjectDto dto, JsonElement savedObjectType, bool hasObjectType, JsonElement savedScriptType, bool hasScriptType)
    {
        // Explicit subtype registry only (F004): a registered full name restores the subtype,
        // anything else (including old AssemblyQualifiedName markers) loads as its base kind
        // with a loud log — save data is never allowed to pick a type to instantiate.
        if (hasObjectType)
        {
            string? typeName = savedObjectType.ValueKind == JsonValueKind.String ? savedObjectType.GetString() : null;
            if (!string.IsNullOrEmpty(typeName))
            {
                if (TryCreateSubtype(typeName!, out var inst) && inst is not null)
                {
                    if (inst is Node subNode)
                    {
                        Coord subCoord = ExtractCoord(dto);
                        try { ObjectRegistry.RemoveObject(subNode); } catch (Exception ex) { AtherizLogger.LogError($"Subtype cleanup failed for {typeName}.", ex); }
                        subNode.SetIdRaw(dto.Id);
                        GameObject.ApplyDtoFields(inst, dto, isNodeOverride: true);
                        subNode.Coord = subCoord;
                        inst.IsNode = true;
                        return inst;
                    }
                    inst.SetIdRaw(dto.Id);
                    GameObject.ApplyDtoFields(inst, dto, null);
                    return inst;
                }
                AtherizLogger.LogError($"Unknown __object_type '{typeName}' for object {dto.Id}; loading as base {dto.Type}.");
            }
        }
        // Script branch: preserve IsScript and subtype for hook fidelity (faithful to dill subclass preservation)
        if (string.Equals(dto.Type, "script", StringComparison.OrdinalIgnoreCase))
        {
            // Restore the concrete Script subclass only for explicitly registered types.
            if (hasScriptType)
            {
                string? typeName = savedScriptType.ValueKind == JsonValueKind.String ? savedScriptType.GetString() : null;
                if (!string.IsNullOrEmpty(typeName))
                {
                    if (TryCreateSubtype(typeName!, out var scoped) && scoped is not null)
                    {
                        scoped.SetIdRaw(dto.Id);
                        GameObject.ApplyDtoFields(scoped, dto, null);
                        scoped.IsScript = true;
                        return scoped;
                    }
                    AtherizLogger.LogError($"Unknown __script_type '{typeName}' for object {dto.Id}; loading as base script.");
                }
            }
            var s = new Script();
            s.SetIdRaw(dto.Id);
            GameObject.ApplyDtoFields(s, dto, null);
            s.IsScript = true;
            return s;
        }
        // Channel branch: Type=="channel" -> create Channel instance and restore history
        if (string.Equals(dto.Type, "channel", StringComparison.OrdinalIgnoreCase))
        {
            var ch = new Channel();
            ch.SetIdRaw(dto.Id);
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
        // Account branch: Type=="account" -> create Account instance and restore extras (fixes invalid password / 0 known)
        if (string.Equals(dto.Type, "account", StringComparison.OrdinalIgnoreCase))
        {
            return Account.FromDto(dto);
        }
        // Node branch: if IsNode or Type=="node", instantiate Node (preserves Coord via Location)
        bool isNode = dto.IsNode || string.Equals(dto.Type, "node", StringComparison.OrdinalIgnoreCase);
        GameObject o;
        if (isNode)
        {
            Coord coord = ExtractCoord(dto);
            var node = Node.CreateForLoad(coord);
            node.SetIdRaw(dto.Id);
            node.Desc = dto.Desc;
            node.IsModified = dto.IsModified;
            node.IsNode = true;
            o = node;
            GameObject.ApplyDtoFields(o, dto, isNodeOverride: true);
            return o;
        }
        o = new GameObject();
        o.SetIdRaw(dto.Id);
        GameObject.ApplyDtoFields(o, dto, isNodeOverride: null);
        return o;
    }

    /// <summary>Coord for node instantiation: Location first, then Extra "Coord", then limbo origin.</summary>
    internal static Coord ExtractCoord(GameObjectDto dto)
    {
        if (dto.Location is LocationRef.CoordLocation cl) return cl.Coord;
        if (dto.Extra is not null && dto.Extra.TryGetValue("Coord", out var ce))
        {
            try { return JsonSerializer.Deserialize<Coord>(ce.GetRawText(), JsonOptions.Default)!; }
            catch (Exception ex) { AtherizLogger.LogError($"Bad Extra Coord for object {dto.Id}; using limbo origin.", ex); }
        }
        return new Coord("limbo", 0, 0, 0);
    }

    public static (string Sql, object[] Params) GetSaveOps(GameObject obj)
        => ("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", [obj.Id, BuildSaveJson(obj, clearing: false)]);

    public static (string Sql, object[] Params) GetSaveOpsClearing(GameObject obj)
        => ("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", [obj.Id, BuildSaveJson(obj, clearing: true)]);

    private static string BuildSaveJson(GameObject obj, bool clearing)
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
            dto = obj.ToDtoUnsafeInternal();
            if (clearing) dto.IsModified = false;
            snapshotOk = true;
        }
        catch
        {
            obj.SetIsModifiedRawNoLock(had);
            throw;
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
    private static string EncodeSaveJson(GameObject obj, GameObjectDto dto, bool had)
    {
        try { return GameObjectDtoSerializer.ToJson(dto); }
        catch
        {
            obj.SyncRoot.EnterWriteLock();
            try { obj.SetIsModifiedRawNoLock(had); }
            finally { obj.SyncRoot.ExitWriteLock(); }
            throw;
        }
    }

    // History entries persist as [timestamp, sender, message] triples mirroring
    // the Python (timestamp, sender, message) tuples. Pre-triple saves stored
    // plain strings; those restore as sender-less entries (timestamp 0).
    private static List<ChannelHistoryEntry> ParseChannelHistory(JsonElement he)
    {
        List<ChannelHistoryEntry> entries = [];
        if (he.ValueKind != JsonValueKind.Array) return entries;
        foreach (var el in he.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                entries.Add(new ChannelHistoryEntry(0, "", el.GetString() ?? ""));
            }
            else if (el.ValueKind == JsonValueKind.Array)
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
