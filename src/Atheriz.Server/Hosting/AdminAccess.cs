using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using System.Security.Claims;

namespace Atheriz.Server.Hosting;

// Privilege and attribute gates for the landing-page admin API. Mirrors the
// in-game `set`/`unset`/`delete` rules: builder-or-above to enter, edit only
// below your own level (admin edits anything), protected attrs need admin.
// Location/home are the exception: in-game `set` refuses them (direct
// assignment would corrupt containment), but over HTTP the entered string
// is parsed and applied with move semantics (move for location, store for
// home). Messages match the command strings so HTTP and in-game denials
// read the same.
public static class AdminAccess
{
    public static Account? GetAccount(HttpContext ctx)
    {
        var idRaw = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idRaw, out var id)) return null;
        return ObjectRegistry.GetSingle(id) as Account;
    }

    // An account acts with the best privilege among its characters.
    // The account row's own stored level is inert: grants live on
    // characters (InitialSetup seeds the character, never the account),
    // so nothing here reads it. Characterless accounts floor at Player,
    // matching the Account.Create default.
    public static Privilege EffectiveLevel(Account account)
    {
        var best = Privilege.Player;
        foreach (var cid in account.Characters)
        {
            var c = ObjectRegistry.GetSingle(cid);
            if (c is null || c.IsDeleted) continue;
            if (c.PrivilegeLevel > best) best = c.PrivilegeLevel;
        }
        return best;
    }

    public static bool IsBuilderOrAbove(Account account) => EffectiveLevel(account) >= Privilege.Builder;
    public static bool IsAdmin(Account account) => EffectiveLevel(account) >= Privilege.Admin;

    // HTTP login throttle: a host banned by failed attempts (in-game or over
    // HTTP) waits out the cooldown instead of burning more PBKDF2 rounds.
    // "?" means the remote address was unavailable; never throttle that.
    public static bool IsLoginThrottled(string host) =>
        host != "?" && ObjectRegistry.IsIpBanned(host);

    // Mirrors SetHelper.MoveGate (internal to Core): these attrs are never
    // assigned directly, in-game or over HTTP. Location/home are NOT in
    // this gate: in-game `set` refuses them through Core's own MoveGate,
    // while TryApplySet below parses and moves instead.
    internal static readonly HashSet<string> MoveGate = new(StringComparer.Ordinal)
        { "_contents", "group_channel", "contents" };

    // Structured values JSON cannot build (typed setters need real CLR
    // types): refuse over HTTP with a pointer to the game client.
    internal static readonly HashSet<string> ComplexAttrs = new(StringComparer.OrdinalIgnoreCase)
        { "session", "external_cmdset", "internal_cmdset", "coord", "links", "nouns" };

    // Same rule as SetCommand/DeleteCommand: below your level, or your own
    // avatar (the target == go self-edit bypass), or admin for anything.
    public static bool CanEdit(Account editor, GameObject target, out string? why)
    {
        if (IsAdmin(editor)) { why = null; return true; }
        bool isSelf = editor.Characters.Contains(target.Id);
        if (!isSelf && target.PrivilegeLevel >= EffectiveLevel(editor))
        {
            why = "You cannot modify an object of equal or higher privilege.";
            return false;
        }
        why = null;
        return true;
    }

    public static bool IsEditableProp(Account editor, GameObject target, string attr)
    {
        if (attr is "Id" or "id" or "_id") return false;
        if (target is Account && attr is "PrivilegeLevel" or "privilege_level" or "_privilege_level") return false;
        // Location/home edit through the move parser below (all three
        // ISettable spellings each), so the editor offers them. The
        // below-level gate still runs in TryApplySet; this is only the hint.
        if (IsLocationAttr(attr) || IsHomeAttr(attr)) return true;
        if (MoveGate.Contains(attr)) return false;
        if (ComplexAttrs.Contains(attr)) return false;
        if (SetHelper.IsProtected(attr) && !IsAdmin(editor)) return false;
        if (SetHelper.HasKnownProp(target, attr)) return target.IsSettableProperty(attr);
        return SetHelper.HasAttr(target, attr);
    }

    public static bool TryParsePrivilege(object? value, out Privilege privilege)
    {
        switch (value)
        {
            case Privilege p:
                privilege = p;
                return true;
            case string s when Enum.TryParse<Privilege>(s, ignoreCase: true, out var named)
                && Enum.IsDefined(typeof(Privilege), named):
                privilege = named;
                return true;
            case int i when Enum.IsDefined(typeof(Privilege), i):
                privilege = (Privilege)i;
                return true;
            case long l when Enum.IsDefined(typeof(Privilege), (int)l):
                privilege = (Privilege)(int)l;
                return true;
            default:
                privilege = default;
                return false;
        }
    }

    // JSON element to plain CLR, same shape as SetCommand's literal parser
    // (strings, ints, doubles, bools, null, lists, string-keyed dicts).
    public static object? FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt32(out var i) ? i
            : element.TryGetDouble(out var d) ? (object)d : element.GetRawText(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => element.EnumerateArray().Select(FromJson).ToList(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => FromJson(p.Value)),
        _ => element.GetRawText(),
    };

    public static bool TryApplySet(Account editor, GameObject target, string attr, object? value,
        out string? error, out bool isNew)
    {
        error = null;
        isNew = false;
        // Location moves the object, home only stores the marker: both
        // parse the entered string instead of assigning it (assignment
        // would corrupt containment, which is why in-game `set` refuses).
        if (IsLocationAttr(attr))
            return TryApplyLocationMove(editor, target, value, out error, out isNew);
        if (IsHomeAttr(attr))
            return TryApplyHome(editor, target, value, out error, out isNew);
        if (MoveGate.Contains(attr))
        {
            error = $"'{attr}' cannot be set directly; use move/teleport instead.";
            return false;
        }
        if (ComplexAttrs.Contains(attr))
        {
            error = $"'{attr}' cannot be edited here; use the game client.";
            return false;
        }
        if (SetHelper.IsProtected(attr) && !IsAdmin(editor))
        {
            error = $"'{attr}' is protected and cannot be set.";
            return false;
        }
        if (!CanEdit(editor, target, out var why)) { error = why; return false; }
        // Rename validation mirrors SetCommand: illegal names and collisions
        // are refused before anything is written.
        if (attr is "Name" or "name" or "_name" && value is not null)
        {
            string newName = value as string ?? value.ToString() ?? "";
            var nameErr = Validation.ValidateCharacterName(newName);
            if (nameErr is not null) { error = nameErr; return false; }
            bool sameAsSelf = target.IsPc && target.Name.Equals(newName, StringComparison.OrdinalIgnoreCase);
            if (!sameAsSelf && CreationValidation.PcNameExists(newName))
            {
                error = $"Character with this name ({newName}) already exists.";
                return false;
            }
        }
        if (attr is "PrivilegeLevel" or "privilege_level" or "_privilege_level")
        {
            // The account row's stored level feeds nothing (see
            // EffectiveLevel): refuse instead of writing a value that
            // would silently do nothing. Grants go on characters.
            if (target is Account)
            {
                error = "Accounts carry no privilege; grant it on one of its characters instead.";
                return false;
            }
            if (!TryParsePrivilege(value, out var priv))
            {
                error = $"'{attr}' cannot be set from text.";
                return false;
            }
            value = priv;
        }
        isNew = !SetHelper.HasAttr(target, attr);
        try
        {
            SetHelper.SetAttr(target, attr, value);
            target.IsModified = true;
        }
        catch (InvalidOperationException) { error = $"'{attr}' is a read-only attribute and cannot be set."; return false; }
        catch (InvalidCastException) { error = $"'{attr}' cannot be set from text."; return false; }
        catch (Exception ex) { error = $"Could not set '{attr}': {ex.Message}"; return false; }
        try { target.LastTouchedBy = editor.Id; } catch { }
        return true;
    }

    // All three ISettable spellings of the placement attrs. The editor
    // shows the canonical ("Location"/"Home") rows; PATCH matches whatever
    // spelling the row was rendered with.
    private static bool IsLocationAttr(string attr) => attr is "Location" or "location" or "_location";
    private static bool IsHomeAttr(string attr) => attr is "Home" or "home" or "_home";

    // Parses an admin-entered location/home string into a LocationRef:
    // nowhere words (or an empty/JSON-null editor value) clear to nowhere,
    // #id resolves to that object, anything else must be a coord in one of
    // the shapes the in-game `move` command accepts. Object ids resolve
    // here (unknown/deleted ids refuse); coords stay bare data so home can
    // store any well-formed marker while a location move resolves its node.
    private static bool TryParseLocationRef(object? value, string attr, out LocationRef dest, out string? error)
    {
        dest = LocationRef.NullLocation.Instance;
        error = null;
        if (value is LocationRef lr) { dest = lr; return true; }
        if (value is int i) return TryResolveObjectRef(i, out dest, out error);
        if (value is long l && l >= int.MinValue && l <= int.MaxValue)
            return TryResolveObjectRef((int)l, out dest, out error);
        if (value is null) return true;
        if (value is not string raw)
        {
            error = $"'{attr}' cannot be set from text.";
            return false;
        }
        raw = raw.Trim();
        if (raw.Length == 0
            || raw.Equals("null", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("nowhere", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;
        if (raw.StartsWith('#') && int.TryParse(raw[1..], out var id))
            return TryResolveObjectRef(id, out dest, out error);
        if (Coord.TryParse(raw, out var coord) || Coord.TryParseCommaOnly(raw, out coord))
        {
            dest = LocationRef.FromCoord(coord);
            return true;
        }
        error = $"'{attr}' must be a coord like 'limbo(0,0,0)', 'area x y z' or '(area,x,y,z)', an object id like '#12', or 'null'.";
        return false;
    }

    private static bool TryResolveObjectRef(int id, out LocationRef dest, out string? error)
    {
        dest = LocationRef.NullLocation.Instance;
        // Deleted ids count as missing (same as the #id search, which
        // excludes deleted objects): resolving one would only fail later
        // with a vaguer message.
        var obj = ObjectRegistry.GetSingle(id);
        if (obj is null || obj.IsDeleted) { error = $"No object found with ID {id}."; return false; }
        dest = new LocationRef.ObjectLocation(id);
        error = null;
        return true;
    }

    // Location edits move the object through MoveTo (force, like the
    // in-game `move` command): containment sets, hooks, and announces all
    // run. An unchanged value succeeds without touching anything.
    private static bool TryApplyLocationMove(Account editor, GameObject target, object? value,
        out string? error, out bool isNew)
    {
        error = null;
        isNew = false;
        if (target.IsNode)
        {
            error = "'location' cannot be set on a room; a room's position is its coord.";
            return false;
        }
        if (!TryParseLocationRef(value, "location", out var dest, out error)) return false;
        if (!CanEdit(editor, target, out var why)) { error = why; return false; }
        if (Equals(target.Location, dest)) return true;
        bool moved;
        if (dest is LocationRef.NullLocation)
            moved = target.MoveToNowhere(force: true);
        else if (dest is LocationRef.CoordLocation cl)
        {
            var node = ObjectRegistry.FindNodeByCoord(cl.Coord);
            if (node is null) { error = $"No node found at {cl.Coord}."; return false; }
            moved = target.MoveTo(node, force: true);
        }
        else if (dest is LocationRef.ObjectLocation ol)
        {
            var obj = ObjectRegistry.GetSingle(ol.ObjectId);
            if (obj is null || obj.IsDeleted) { error = $"No object found with ID {ol.ObjectId}."; return false; }
            moved = target.MoveTo(obj, force: true);
        }
        else
        {
            error = "'location' cannot be set from text.";
            return false;
        }
        if (!moved) { error = $"Could not move to {FormatLocationRef(dest)}."; return false; }
        try { target.LastTouchedBy = editor.Id; } catch { }
        return true;
    }

    // Home edits only store the respawn marker: no movement, no node
    // resolution (any well-formed coord is a valid marker).
    private static bool TryApplyHome(Account editor, GameObject target, object? value,
        out string? error, out bool isNew)
    {
        error = null;
        isNew = false;
        if (!TryParseLocationRef(value, "home", out var dest, out error)) return false;
        if (!CanEdit(editor, target, out var why)) { error = why; return false; }
        if (Equals(target.Home, dest)) return true;
        target.Home = dest;
        target.IsModified = true;
        try { target.LastTouchedBy = editor.Id; } catch { }
        return true;
    }

    // Round-trip display for location/home rows: the shown text parses back
    // through TryParseLocationRef (coord in ToString form, #id, null).
    private static string FormatLocationRef(LocationRef loc) => loc switch
    {
        LocationRef.ObjectLocation o => $"#{o.ObjectId}",
        LocationRef.CoordLocation c => c.Coord.ToString(),
        _ => "null",
    };

    public static bool TryRemoveExtra(Account editor, GameObject target, string attr, out string? error)
    {
        error = null;
        if (MoveGate.Contains(attr)) { error = $"'{attr}' cannot be removed directly."; return false; }
        if (ComplexAttrs.Contains(attr)) { error = $"'{attr}' cannot be edited here; use the game client."; return false; }
        if (SetHelper.IsProtected(attr) && !IsAdmin(editor))
        {
            error = $"'{attr}' is protected and cannot be removed.";
            return false;
        }
        if (!CanEdit(editor, target, out var why)) { error = why; return false; }
        if (target.IsKnownProperty(attr)) { error = $"'{attr}' is not an extra attribute."; return false; }
        if (!SetHelper.TryRemoveExtra(target, attr)) { error = $"No such attribute '{attr}'."; return false; }
        target.IsModified = true;
        try { target.LastTouchedBy = editor.Id; } catch { }
        return true;
    }

    public static string ObjectType(GameObject o) => o switch
    {
        Account => "account",
        Channel => "channel",
        Node => "room",
        _ when o.IsPc => "player",
        _ when o.IsNpc => "npc",
        _ when o.IsItem => "item",
        _ when o.IsScript => "script",
        _ => "object",
    };

    // Type is the editor kind the client should render: "bool" for a real
    // boolean value (a true/false dropdown), "text" for everything else.
    public sealed record PropDto(string Name, string Value, bool Editable, bool IsExtra, string Type);
    public sealed record ObjectDetailDto(int Id, string Name, string Type, int Privilege, List<PropDto> Props);
    public sealed record ObjectListItemDto(int Id, string Name, string Type, int Privilege, string Where);

    private static string FormatShort(object? formatted) => formatted switch
    {
        null => "None",
        string s => s,
        IEnumerable<string> lines => string.Join("\n", lines).TrimStart('\n'),
        _ => formatted.ToString() ?? "None",
    };

    // The level an object acts with: for accounts, the best privilege
    // among its characters (Player floor when there are none). The list
    // and detail headers report that instead of the raw stored field.
    // The PrivilegeLevel prop row still shows the stored value, now
    // read-only for accounts since writing it would change nothing.
    public static Privilege ShownPrivilege(GameObject o) =>
        o is Account a ? EffectiveLevel(a) : o.PrivilegeLevel;

    public static ObjectDetailDto ToDetail(Account viewer, GameObject target)
    {
        var props = new List<PropDto>();
        foreach (var (name, value, _) in ExamFormatter.Collect(target))
        {
            // Extras are enumerated individually below; the composite map
            // would only duplicate them.
            if (name == "extra") continue;
            // Location/home show the round-trip form (#id, coord, null)
            // instead of the record dump, so the value can be edited and
            // pasted back as-is.
            bool isPlace = name is "Location" or "Home";
            props.Add(new PropDto(
                name,
                isPlace && value is LocationRef place ? FormatLocationRef(place)
                    : FormatShort(ExamFormatter.FormatValue(value, name)),
                IsEditableProp(viewer, target, name),
                false,
                value is bool ? "bool" : "text"));
            // An account's stored level feeds nothing (see
            // EffectiveLevel), so when it differs from the acting level,
            // say so next to the stored row instead of letting the stored
            // value confuse. Both rows are read-only here: the stored one
            // because writing it would silently do nothing.
            if (target is Account
                && name == "PrivilegeLevel"
                && ShownPrivilege(target) is var acting && acting != target.PrivilegeLevel)
                props.Add(new PropDto("acts_as", acting.ToString().ToLowerInvariant(), false, false, "text"));
        }
        // Extras are real editable properties, not just a map dump: one row
        // per key, with the same secrecy redaction the examine list uses.
        foreach (var (key, element) in target.GetExtraSnapshot().OrderBy(kv => kv.Key))
        {
            if (key.Contains("password", StringComparison.OrdinalIgnoreCase)
                || key.Contains("secret", StringComparison.OrdinalIgnoreCase))
                continue;
            var extraVal = FromJson(element);
            props.Add(new PropDto(
                key,
                FormatShort(ExamFormatter.FormatValue(extraVal, key)),
                IsEditableProp(viewer, target, key),
                true,
                extraVal is bool ? "bool" : "text"));
        }
        return new ObjectDetailDto(target.Id, target.Name, ObjectType(target), (int)ShownPrivilege(target), props);
    }

    public static ObjectListItemDto ToListItem(GameObject o)
    {
        string where;
        try { where = o.Location.ToString() ?? ""; }
        catch { where = ""; }
        return new ObjectListItemDto(o.Id, o.Name, ObjectType(o), (int)ShownPrivilege(o), where);
    }
}
