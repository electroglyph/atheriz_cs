// Port of atheriz/commands/loggedin/set.py:11-243 helpers (PROTECTED_ATTRIBUTES + _resolve_target + FindProp)
using System.Reflection;
using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

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

    // Faithful to Python hasattr/getattr: attribute lookup is case-SENSITIVE.
    // Exact property name first, then all-lowercase snake_case mapped to
    // PascalCase (is_pc -> IsPc). Mixed-case spellings (Is_Pc, Quelled) do NOT
    // resolve: like Python's setattr creating a junk attribute, they fall
    // through to the extras store instead of hitting the real property.
    public static PropertyInfo? FindProp(GameObject o, string attr)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var prop = o.GetType().GetProperty(attr, flags);
        if (prop != null) return prop;
        if (!IsLowerSnake(attr)) return null;
        var parts = attr.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var pascal = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
        return o.GetType().GetProperty(pascal, flags);
    }

    private static bool IsLowerSnake(string s) =>
        s.Length > 0 && s.All(c => char.IsLower(c) || char.IsDigit(c) || c == '_');

    // Canonical privilege names have no settable property (get-only IsBuilder/
    // IsSuperUser) but must still be guarded case-insensitively.
    private static readonly HashSet<string> ProtectedCanonical =
        new(StringComparer.OrdinalIgnoreCase) { "is_builder", "is_superuser" };

    // Port of set.py:75-76 _is_protected, hardened: the guard itself is
    // case-insensitive (frozen set is Ordinal like Python's frozenset, so
    // compare explicitly). Resolution stays case-sensitive (FindProp): unknown
    // spellings fall through to extras like Python's setattr junk attribute.
    public static bool IsProtected(string attr) =>
        attr.StartsWith("_") ||
        Protected.Any(p => p.Equals(attr, StringComparison.OrdinalIgnoreCase)) ||
        ProtectedCanonical.Contains(attr);

    public static PropertyInfo? FindPropUnset(GameObject o, string attr) => FindProp(o, attr);

    public static bool HasAttr(GameObject o, string attr)
    {
        if (FindProp(o, attr) != null) return true;
        if (o.GetType().GetField(attr, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance) != null) return true;
        // Typed extra check (F001: no _extra reflection).
        return o.HasExtra(attr);
    }

    public static void SetAttr(GameObject o, string attr, object? val)
    {
        var prop = FindProp(o, attr);
        if (prop != null && prop.CanWrite)
        {
            var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            object? conv = val == null ? null : Convert.ChangeType(val, targetType);
            prop.SetValue(o, conv);
            return;
        }
        if (prop != null && !prop.CanWrite) throw new InvalidOperationException();
        // Typed extra store (F001: no _extra reflection). Property lookup above stays
        // reflective on purpose: `set` assigns arbitrary attrs like Python setattr.
        JsonElement je = JsonSerializer.SerializeToElement(val);
        o.SetExtraJson(attr, je);
        return;
    }

    public static bool TryRemoveExtra(GameObject target, string attr)
    {
        // Typed (F001: no _extra reflection, no dynamic).
        if (!target.HasExtra(attr)) return false;
        return target.TryRemoveExtraJson(attr);
    }
}
