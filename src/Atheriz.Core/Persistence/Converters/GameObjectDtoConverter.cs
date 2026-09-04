using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Persistence.Converters;

/// <summary>
/// Persistence converter extracted from <c>GameObject.cs</c> god file (P1.5).
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
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        lock (_subtypeLock)
        {
            _subtypeFactories[fullName] = factory;
            _subtypeNames[type] = fullName;
        }
    }

    private static bool TryCreateSubtype(string fullName, out GameObject? instance)
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
        if (puppet != null)
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
            if (registered != null)
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
                if (registered != null)
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
            var names = pols != null && pols.Count == kv.Value.Count ? pols : Enumerable.Repeat(LockPolicies.Custom, kv.Value.Count);
            return new LockDefDto { Name = kv.Key, Policy = string.Join("|", names) };
        }).ToList();
    }

    public static GameObject FromDto(GameObjectDto dto)
    {
        // Explicit subtype registry only (F004): a registered full name restores the subtype,
        // anything else (including old AssemblyQualifiedName markers) loads as its base kind
        // with a loud log — save data is never allowed to pick a type to instantiate.
        if (dto.Extra != null && dto.Extra.TryGetValue("__object_type", out var ot))
        {
            string? typeName = ot.ValueKind == JsonValueKind.String ? ot.GetString() : null;
            dto.Extra.Remove("__object_type");
            if (!string.IsNullOrEmpty(typeName))
            {
                if (TryCreateSubtype(typeName!, out var inst) && inst != null)
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
            if (dto.Extra != null && dto.Extra.TryGetValue("__script_type", out var te))
            {
                string? typeName = te.ValueKind == JsonValueKind.String ? te.GetString() : null;
                dto.Extra.Remove("__script_type");
                if (!string.IsNullOrEmpty(typeName))
                {
                    if (TryCreateSubtype(typeName!, out var scoped) && scoped != null)
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
            if (dto.Extra != null && dto.Extra.TryGetValue("history", out var he))
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(he.GetRawText(), JsonOptions.Default);
                    if (list != null) ch.RestoreHistory(list);
                }
                catch
                {
                    // fallback: try as JsonElement array of strings
                    try
                    {
                        var list2 = new List<string>();
                        if (he.ValueKind == JsonValueKind.Array) foreach (var el in he.EnumerateArray()) if (el.ValueKind == JsonValueKind.String) list2.Add(el.GetString() ?? "");
                        ch.RestoreHistory(list2);
                    }
                    catch (Exception ex2) { AtherizLogger.LogError($"Channel {dto.Id} history unrestorable; starting empty.", ex2); }
                }
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
            var node = new Node(coord);
            try { ObjectRegistry.RemoveObject(node); } catch (Exception ex) { AtherizLogger.LogError($"Node cleanup failed for object {dto.Id}.", ex); }
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
        if (dto.Extra != null && dto.Extra.TryGetValue("Coord", out var ce))
        {
            try { return JsonSerializer.Deserialize<Coord>(ce.GetRawText())!; }
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
        // Use raw IsModified access without re-entering Write lock to ensure exactly one tracker increment (test SaveUsesLock expects 1).
        // Non-clearing restores the flag afterwards; clearing leaves it false (only restored on error).
        obj.IncrementTrackerInternal();
        obj.SyncRoot.EnterWriteLock();
        bool had = obj.GetIsModifiedRawNoLock();
        obj.SetIsModifiedRawNoLock(false);
        try
        {
            var dto = obj.ToDtoUnsafeInternal();
            if (clearing) dto.IsModified = false;
            return GameObjectDtoSerializer.ToJson(dto);
        }
        catch
        {
            obj.SetIsModifiedRawNoLock(had);
            throw;
        }
        finally
        {
            if (!clearing) obj.SetIsModifiedRawNoLock(had);
            obj.SyncRoot.ExitWriteLock();
        }
    }
}
