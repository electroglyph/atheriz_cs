// Port of atheriz/commands/loggedin/set.py:11-243 helpers (PROTECTED_ATTRIBUTES + _resolve_target + FindProp)
using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Commands.LoggedIn;

public static class SetHelper
{
    public static readonly HashSet<string> Protected = new(StringComparer.Ordinal)
    {
        "id","session","lock","locks","access","internal_cmdset","external_cmdset","scripts","hooks","channels","followers","following","is_pc","is_npc","is_item","is_container","is_mapable","is_account","is_channel","is_node","is_script","is_connected","is_deleted","is_modified","is_temporary","is_tickable","_is_tickable","password","logged_in","characters","privilege_level","quelled","is_banned","ban_reason","location","home","_contents","group_channel","contents","tags","name"
    };

    public static GameObject? ResolveTarget(GameObject caller, string s)
    {
        // Delegate to CommandHelpers for faithful dedup (handles #id/me/here/contents + loc fallback)
        return Commands.CommandHelpers.ResolveObject(caller, s);
    }

    // Explicit settable-property map (replaces the reflective FindProp).
    // Key: accepted spelling — exact CLR name, lower-snake form, or "_" + snake
    // (mirroring the old lookup: case-sensitive exact, then snake->PascalCase).
    // A null setter marks a known-but-read-only member: SetAttr throws
    // InvalidOperationException exactly like a get-only property did.
    // Conversion goes through Convert.ChangeType with the member type, so success
    // and failure modes (FormatException/InvalidCastException paths in SetCommand)
    // are unchanged.
    // NOTE vs the old code: exact-case flag spellings ("IsPc") used to slip past
    // IsProtected (which compares snake forms) and toggle flags. Flags are now
    // uniformly read-only through `set` (use the quell/build commands instead);
    // superuser-only narrowing for is_* is intentional hardening.
    private sealed record SetterEntry(Type ValueType, Action<GameObject, object?>? Set);

    private static void Put(Dictionary<string, SetterEntry> m, Type t, Action<GameObject, object?>? set, params string[] names)
    {
        var e = new SetterEntry(t, set);
        foreach (var n in names) m[n] = e;
    }

    private static readonly Dictionary<string, SetterEntry> BaseSetters = BuildBase();
    private static readonly Dictionary<string, SetterEntry> NodeSetters = BuildSub(BuildBase, m =>
    {
        Put(m, typeof(string), (o, v) => ((Node)o).Theme = (string)v!, "Theme", "theme", "_theme");
        Put(m, typeof(string), (o, v) => ((Node)o).LegendDesc = (string)v!, "LegendDesc", "legend_desc", "_legend_desc");
        Put(m, typeof(double), (o, v) => ((Node)o).OpenAttenuation = (double)v!, "OpenAttenuation", "open_attenuation", "_open_attenuation");
        Put(m, typeof(double), (o, v) => ((Node)o).EnclosedAttenuation = (double)v!, "EnclosedAttenuation", "enclosed_attenuation", "_enclosed_attenuation");
        Put(m, typeof(double), (o, v) => ((Node)o).AmbientSoundLevel = (double)v!, "AmbientSoundLevel", "ambient_sound_level", "_ambient_sound_level");
        Put(m, typeof(Coord), (o, v) => ((Node)o).Coord = (Coord)v!, "Coord", "coord", "_coord");
        Put(m, typeof(List<NodeLink>), (o, v) => ((Node)o).Links = (List<NodeLink>)v!, "Links", "links", "_links");
        Put(m, typeof(Dictionary<string, string>), (o, v) => ((Node)o).Nouns = (Dictionary<string, string>)v!, "Nouns", "nouns", "_nouns");
        Put(m, typeof(HashSet<int>), null, "ScriptsSet", "scripts_set", "_scripts_set");
    });
    private static readonly Dictionary<string, SetterEntry> ChannelSetters = BuildSub(BuildBase, m =>
    {
        Put(m, typeof(int), (o, v) => ((Channel)o).CreatedBy = (int)v!, "CreatedBy", "created_by", "_created_by");
        Put(m, typeof(IReadOnlySet<int>), null, "Listeners", "listeners", "_listeners");
        Put(m, typeof(IReadOnlyList<string>), null, "History", "history", "_history");
        Put(m, typeof(Atheriz.Core.Commands.Command), null, "Command", "command", "_command");
    });
    private static readonly Dictionary<string, SetterEntry> AccountSetters = BuildSub(BuildBase, m =>
    {
        Put(m, typeof(IReadOnlyList<int>), null, "Characters", "characters", "_characters");
        Put(m, typeof(string), (o, v) => ((Account)o).BanReason = (string)v!, "BanReason", "ban_reason", "_ban_reason");
        Put(m, typeof(string), null, "PasswordHash", "password_hash", "_password_hash");
        Put(m, typeof(bool), null, "LoggedIn", "logged_in", "_logged_in");
    });
    private static readonly Dictionary<string, SetterEntry> ScriptSetters = BuildSub(BuildBase, m =>
    {
        Put(m, typeof(GameObject), null, "Child", "child", "_child");
    });

    private static Dictionary<string, SetterEntry> BuildSub(Func<Dictionary<string, SetterEntry>> buildBase, Action<Dictionary<string, SetterEntry>> add)
    {
        var m = buildBase();
        add(m);
        return m;
    }

    private static Dictionary<string, SetterEntry> BuildBase()
    {
        var m = new Dictionary<string, SetterEntry>(StringComparer.Ordinal);
        Put(m, typeof(string), (o, v) => o.Desc = (string)v!, "Desc", "desc", "_desc");
        Put(m, typeof(string), (o, v) => o.Symbol = (string)v!, "Symbol", "symbol", "_symbol");
        Put(m, typeof(string), (o, v) => o.MoveVerb = (string)v!, "MoveVerb", "move_verb", "_move_verb");
        Put(m, typeof(string), (o, v) => o.Gender = (string)v!, "Gender", "gender", "_gender");
        Put(m, typeof(string), (o, v) => o.Name = (string)v!, "Name", "name", "_name");
        Put(m, typeof(bool), (o, v) => o.Quelled = (bool)v!, "Quelled", "quelled", "_quelled");
        Put(m, typeof(bool), (o, v) => o.MapEnabled = (bool)v!, "MapEnabled", "map_enabled", "_map_enabled");
        Put(m, typeof(bool), (o, v) => o.NoFollow = (bool)v!, "NoFollow", "no_follow", "_no_follow");
        Put(m, typeof(double), (o, v) => o.TickSeconds = (double)v!, "TickSeconds", "tick_seconds", "_tick_seconds");
        Put(m, typeof(double), (o, v) => o.LastMapTime = (double?)v!, "LastMapTime", "last_map_time", "_last_map_time");
        Put(m, typeof(int), (o, v) => o.Following = (int?)v!, "Following", "following", "_following");
        Put(m, typeof(Privilege), (o, v) => o.PrivilegeLevel = (Privilege)v!, "PrivilegeLevel", "privilege_level", "_privilege_level");
        Put(m, typeof(Session), (o, v) => o.Session = (Session)v!, "Session", "session", "_session");
        Put(m, typeof(CmdSet), (o, v) => o.ExternalCmdSet = (CmdSet)v!, "ExternalCmdSet", "external_cmdset", "_external_cmdset");
        Put(m, typeof(CmdSet), (o, v) => o.InternalCmdSet = (CmdSet)v!, "InternalCmdSet", "internal_cmdset", "_internal_cmdset");
        // Canonical location refs are known but never set/unset directly
        // (lowercase spellings redirect via the move/teleport guard in
        // SetCommand/UnsetCommand before reaching this map; the canonical
        // spelling resolves here and lands in the read-only bucket).
        Put(m, typeof(LocationRef), null, "Location", "location", "_location");
        Put(m, typeof(LocationRef), null, "Home", "home", "_home");
        // Known-but-read-only members (get-only properties / snapshots / locks).
        Put(m, typeof(int), null, "Id", "id", "_id");
        foreach (var f in new[] { "IsPc", "IsNpc", "IsItem", "IsMapable", "IsContainer", "IsScript", "IsTickable", "IsAccount", "IsChannel", "IsNode", "IsModified", "IsDeleted", "IsConnected", "IsTemporary", "IsBanned", "CanHear" })
            Put(m, typeof(bool), null, f, ToSnake(f), "_" + ToSnake(f));
        Put(m, typeof(bool), null, "IsSuperUser", "is_superuser", "_is_superuser");
        Put(m, typeof(bool), null, "IsBuilder", "is_builder", "_is_builder");
        Put(m, typeof(object), null, "SyncRoot", "sync_root", "_sync_root");
        Put(m, typeof(object), null, "TagsSnapshot", "tags_snapshot", "_tags_snapshot");
        Put(m, typeof(object), null, "ContentsSnapshot", "contents_snapshot", "_contents_snapshot");
        Put(m, typeof(object), null, "ScriptsSnapshot", "scripts_snapshot", "_scripts_snapshot");
        Put(m, typeof(object), null, "ChannelsSnapshot", "channels_snapshot", "_channels_snapshot");
        Put(m, typeof(object), null, "FollowersSnapshot", "followers_snapshot", "_followers_snapshot");
        Put(m, typeof(object), null, "PuppetStack", "puppet_stack", "_puppet_stack");
        return m;
    }

    private static string ToSnake(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static Dictionary<string, SetterEntry> MapFor(GameObject o) => o switch
    {
        Node => NodeSetters,
        Channel => ChannelSetters,
        Account => AccountSetters,
        Script => ScriptSetters,
        _ => BaseSetters,
    };

    private static SetterEntry? FindEntry(GameObject o, string attr) =>
        MapFor(o).TryGetValue(attr, out var e) ? e : null;

    // Faithful to Python hasattr/getattr: attribute lookup is case-SENSITIVE.
    // Exact property name first, then all-lowercase snake_case mapped to
    // PascalCase (is_pc -> IsPc). Mixed-case spellings (Is_Pc) do NOT
    // resolve: like Python's setattr creating a junk attribute, they fall
    // through to the extras store instead of hitting the real property.
    public static bool HasKnownProp(GameObject o, string attr) => FindEntry(o, attr) != null;

    // Canonical privilege names have no settable property (get-only IsBuilder/
    // IsSuperUser) but must still be guarded case-insensitively.
    private static readonly HashSet<string> ProtectedCanonical =
        new(StringComparer.OrdinalIgnoreCase) { "is_builder", "is_superuser" };

    // Port of set.py:75-76 _is_protected, hardened: the guard itself is
    // case-insensitive (frozen set is Ordinal like Python's frozenset, so
    // compare explicitly). Resolution stays case-sensitive (FindEntry): unknown
    // spellings fall through to extras like Python's setattr junk attribute.
    public static bool IsProtected(string attr) =>
        attr.StartsWith("_") ||
        Protected.Any(p => p.Equals(attr, StringComparison.OrdinalIgnoreCase)) ||
        ProtectedCanonical.Contains(attr);

    public static bool HasAttr(GameObject o, string attr)
    {
        if (FindEntry(o, attr) != null) return true;
        // Typed extra check (no _extra reflection).
        return o.HasExtra(attr);
    }

    public static void SetAttr(GameObject o, string attr, object? val)
    {
        var e = FindEntry(o, attr);
        if (e != null)
        {
            if (e.Set == null) throw new InvalidOperationException();
            var targetType = Nullable.GetUnderlyingType(e.ValueType) ?? e.ValueType;
            object? conv = val == null ? null : Convert.ChangeType(val, targetType);
            e.Set(o, conv);
            return;
        }
        // Typed extra store. Unknown spellings land here like Python's
        // setattr junk attribute.
        JsonElement je = JsonSerializer.SerializeToElement(val);
        o.SetExtraJson(attr, je);
        return;
    }

    public static bool TryRemoveExtra(GameObject target, string attr)
    {
        // Typed (no _extra reflection, no dynamic).
        if (!target.HasExtra(attr)) return false;
        return target.TryRemoveExtraJson(attr);
    }
}
