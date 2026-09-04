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
            Locks = obj.GetLocksSnapshot().Select(kv => new LockDefDto { Name = kv.Key, Policy = string.Join("|", kv.Value.Select(f => f.Method.Name)) }).ToList(),
        };

        if (obj.IsScript)
        {
            try
            {
                string typeName = obj.GetType().AssemblyQualifiedName ?? obj.GetType().FullName ?? "";
                if (!string.IsNullOrEmpty(typeName) && typeName != typeof(Script).AssemblyQualifiedName && typeName != typeof(Script).FullName)
                {
                    dto.Extra["__script_type"] = JsonSerializer.SerializeToElement(typeName, JsonOptions.Default);
                }
            }
            catch { }
        }

        if (!obj.IsScript)
        {
            var t = obj.GetType();
            if (t != typeof(GameObject) && t != typeof(Node) && t != typeof(Script) && t != typeof(Channel) && t != typeof(Account))
            {
                try
                {
                    string typeName = t.AssemblyQualifiedName ?? t.FullName ?? "";
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        dto.Extra["__object_type"] = JsonSerializer.SerializeToElement(typeName, JsonOptions.Default);
                    }
                }
                catch { }
            }
        }

        return dto;
    }

    public static GameObject FromDto(GameObjectDto dto)
    {
        // Check for generic GameObject/Node subclass preservation via __object_type (e.g., DummyObj/DummyNode, AlarmObj)
        if (dto.Extra != null && dto.Extra.TryGetValue("__object_type", out var ot))
        {
            try
            {
                string? typeName = ot.ValueKind == JsonValueKind.String ? ot.GetString() : null;
                if (!string.IsNullOrEmpty(typeName))
                {
                    Type? t = Type.GetType(typeName!) ?? AppDomain.CurrentDomain.GetAssemblies().SelectMany(a=> { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } }).FirstOrDefault(x=> x.FullName==typeName || x.Name==typeName || x.AssemblyQualifiedName==typeName);
                    if (t != null && typeof(GameObject).IsAssignableFrom(t))
                    {
                        // For Node subclasses, delegate to Node branch logic after creating instance
                        if (typeof(Node).IsAssignableFrom(t))
                        {
                            // Create Node subclass instance with coord from DTO
                            Coord coord;
                            if (dto.Location is LocationRef.CoordLocation cl) coord = cl.Coord;
                            else if (dto.Extra != null && dto.Extra.TryGetValue("Coord", out var ce))
                            {
                                try { coord = JsonSerializer.Deserialize<Coord>(ce.GetRawText())!; } catch { coord = new Coord("limbo",0,0,0); }
                            }
                            else coord = new Coord("limbo",0,0,0);
                            GameObject? nodeInst = null;
                            try { nodeInst = (GameObject?)Activator.CreateInstance(t, new object[]{ coord }); } catch {}
                            if (nodeInst == null)
                            {
                                try { nodeInst = (GameObject?)Activator.CreateInstance(t, nonPublic:true); } catch {}
                            }
                            if (nodeInst == null) throw new InvalidOperationException($"Failed to create Node subclass {t.Name}");
                            dto.Extra!.Remove("__object_type");
                            try { ObjectRegistry.RemoveObject((Node)nodeInst); } catch {}
                            nodeInst.SetIdRaw(dto.Id);
                            GameObject.ApplyDtoFields(nodeInst, dto, isNodeOverride: true);
                            // Also ensure Coord set
                            if (nodeInst is Node nn) nn.Coord = coord;
                            nodeInst.IsNode = true;
                            return nodeInst;
                        }
                        else
                        {
                            GameObject? inst = null;
                            try { inst = (GameObject?)Activator.CreateInstance(t, nonPublic:true); } catch {}
                            if (inst == null) try { inst = (GameObject?)Activator.CreateInstance(t); } catch {}
                            if (inst == null) throw new InvalidOperationException($"Failed to create GameObject subclass {t.Name}");
                            dto.Extra!.Remove("__object_type");
                            inst.SetIdRaw(dto.Id);
                            GameObject.ApplyDtoFields(inst, dto, null);
                            return inst;
                        }
                    }
                }
            }
            catch { }
        }
        // Script branch: preserve IsScript and subtype for hook fidelity (faithful to dill subclass preservation)
        if (string.Equals(dto.Type, "script", StringComparison.OrdinalIgnoreCase))
        {
            // Try to preserve concrete Script subclass via Extra __script_type
            if (dto.Extra != null && dto.Extra.TryGetValue("__script_type", out var te))
            {
                try
                {
                    string? typeName = te.ValueKind == JsonValueKind.String ? te.GetString() : null;
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        Type? t = Type.GetType(typeName!) ?? AppDomain.CurrentDomain.GetAssemblies().SelectMany(a=> { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } }).FirstOrDefault(x=> x.FullName==typeName || x.Name==typeName);
                        if (t != null && typeof(Script).IsAssignableFrom(t))
                        {
                            var inst = (GameObject)Activator.CreateInstance(t, nonPublic:true)!;
                            // Remove subtype marker from Extra so it doesn't leak to user
                            dto.Extra!.Remove("__script_type");
                            inst.SetIdRaw(dto.Id);
                            GameObject.ApplyDtoFields(inst, dto, null);
                            inst.IsScript = true;
                            return inst;
                        }
                    }
                }
                catch { }
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
                    catch { }
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
            Coord coord;
            if (dto.Location is LocationRef.CoordLocation cl) coord = cl.Coord;
            else if (dto.Extra != null && dto.Extra.TryGetValue("Coord", out var ce))
            {
                try { coord = JsonSerializer.Deserialize<Coord>(ce.GetRawText())!; } catch { coord = new Coord("limbo",0,0,0); }
            }
            else coord = new Coord("limbo",0,0,0);
            var node = new Node(coord);
            try { ObjectRegistry.RemoveObject(node); } catch {}
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
        // locks from dto are declarative; for now ignore predicate restoration (need interpreter)
        return o;
    }

    public static (string Sql, object[] Params) GetSaveOps(GameObject obj)
    {
        // non-clearing: save flag state around serialization (mirrors Python get_save_ops)
        // Use raw IsModified access without re-entering Write lock to ensure exactly one tracker increment (test SaveUsesLock expects 1)
        bool had;
        string json;
        obj.IncrementTrackerInternal();
        obj.SyncRoot.EnterWriteLock();
        try
        {
            had = obj.GetIsModifiedRawNoLock();
            obj.SetIsModifiedRawNoLock(false);
            try { json = GameObjectDtoSerializer.ToJson(obj.ToDtoUnsafeInternal()); }
            finally { obj.SetIsModifiedRawNoLock(had); }
        }
        finally { obj.SyncRoot.ExitWriteLock(); }
        return ("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", [obj.Id, json]);
    }

    public static (string Sql, object[] Params) GetSaveOpsClearing(GameObject obj)
    {
        string json;
        bool had;
        obj.IncrementTrackerInternal();
        obj.SyncRoot.EnterWriteLock();
        try
        {
            had = obj.GetIsModifiedRawNoLock();
            obj.SetIsModifiedRawNoLock(false);
            try
            {
                var dto = obj.ToDtoUnsafeInternal();
                dto.IsModified = false;
                json = GameObjectDtoSerializer.ToJson(dto);
                obj.SetIsModifiedRawNoLock(false);
            }
            catch
            {
                obj.SetIsModifiedRawNoLock(had);
                throw;
            }
        }
        finally { obj.SyncRoot.ExitWriteLock(); }
        return ("INSERT OR REPLACE INTO objects (id, data) VALUES (?, ?)", [obj.Id, json]);
    }
}
