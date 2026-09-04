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

    public static PropertyInfo? FindProp(GameObject o, string attr)
    {
        var prop = o.GetType().GetProperty(attr, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop != null) return prop;
        var parts = attr.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var pascal = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
        return o.GetType().GetProperty(pascal, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
    }

    public static PropertyInfo? FindPropUnset(GameObject o, string attr) => FindProp(o, attr);

    public static bool HasAttr(GameObject o, string attr)
    {
        if (FindProp(o, attr) != null) return true;
        if (o.GetType().GetField(attr, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance) != null) return true;
        try
        {
            var f = typeof(GameObject).GetField("_extra", BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = f?.GetValue(o) as Dictionary<string, JsonElement>;
            if (dict != null && dict.ContainsKey(attr)) return true;
        }
        catch { }
        return false;
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
        try
        {
            var f = typeof(GameObject).GetField("_extra", BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = f?.GetValue(o) as Dictionary<string, JsonElement>;
            if (dict != null)
            {
                JsonElement je = JsonSerializer.SerializeToElement(val);
                dict[attr] = je;
                return;
            }
        }
        catch { }
        var f2 = o.GetType().GetField(attr, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (f2 != null) { f2.SetValue(o, val); return; }
        try
        {
            var pascalField = "_" + attr.Split('_').Select((p,i)=> i==0? p.ToLowerInvariant(): char.ToUpperInvariant(p[0])+p.Substring(1)).Aggregate((a,b)=>a+b);
            var f3 = o.GetType().GetField(pascalField, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f3 != null) { f3.SetValue(o, val); return; }
        }
        catch { }
        throw new InvalidOperationException();
    }

    public static bool TryRemoveExtra(GameObject target, string attr)
    {
        bool had = false;
        try
        {
            var f = target.GetType().GetField("_extra", BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = f?.GetValue(target) as System.Collections.IDictionary;
            if (dict != null && dict.Contains(attr)) had = true;
        }
        catch { }
        try { had |= ((dynamic)target).HasExtra(attr); } catch { }
        if (!had) return false;
        try { ((dynamic)target).RemoveExtra(attr); } catch { }
        try
        {
            var f = target.GetType().GetField("_extra", BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = f?.GetValue(target) as System.Collections.IDictionary;
            dict?.Remove(attr);
        }
        catch { }
        return true;
    }
}
