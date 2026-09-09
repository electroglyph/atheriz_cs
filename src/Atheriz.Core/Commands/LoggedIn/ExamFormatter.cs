// Typed examine formatter: explicit per-type member lists + value rendering.
// Replaces the reflection field/property walk (vars(target) equivalent) with
// curated public members. Game-code subclasses extend the dump by overriding
// GameObject.GetExamMembers.
using System.Runtime.CompilerServices;

namespace Atheriz.Core.Commands.LoggedIn;

public static class ExamFormatter
{
    /// <summary>
    /// Base member list for any GameObject. Names double as FormatValue hints
    /// (snake_case keys like "locks"/"followers"/"session" drive the special
    /// renderers below); isProperty mirrors the old "[property]" marker.
    /// </summary>
    public static IEnumerable<(string name, object? value, bool isProperty)> BaseMembers(GameObject o)
    {
        object? Safe(Func<object?> f) { try { return f(); } catch { return "<error>"; } }
        yield return ("Id", Safe(() => (object?)o.Id), true);
        yield return ("Name", Safe(() => (object?)o.Name), true);
        yield return ("Desc", Safe(() => (object?)o.Desc), true);
        yield return ("Symbol", Safe(() => (object?)o.Symbol), true);
        yield return ("MoveVerb", Safe(() => (object?)o.MoveVerb), true);
        yield return ("PrivilegeLevel", Safe(() => (object?)o.PrivilegeLevel), true);
        yield return ("IsPc", Safe(() => (object?)o.IsPc), true);
        yield return ("IsNpc", Safe(() => (object?)o.IsNpc), true);
        yield return ("IsItem", Safe(() => (object?)o.IsItem), true);
        yield return ("IsMapable", Safe(() => (object?)o.IsMapable), true);
        yield return ("IsContainer", Safe(() => (object?)o.IsContainer), true);
        yield return ("IsScript", Safe(() => (object?)o.IsScript), true);
        yield return ("IsTickable", Safe(() => (object?)o.IsTickable), true);
        yield return ("IsAccount", Safe(() => (object?)o.IsAccount), true);
        yield return ("IsChannel", Safe(() => (object?)o.IsChannel), true);
        yield return ("IsNode", Safe(() => (object?)o.IsNode), true);
        yield return ("IsModified", Safe(() => (object?)o.IsModified), true);
        yield return ("IsDeleted", Safe(() => (object?)o.IsDeleted), true);
        yield return ("IsConnected", Safe(() => (object?)o.IsConnected), true);
        yield return ("IsTemporary", Safe(() => (object?)o.IsTemporary), true);
        yield return ("IsBanned", Safe(() => (object?)o.IsBanned), true);
        yield return ("CanHear", Safe(() => (object?)o.CanHear), true);
        yield return ("Quelled", Safe(() => (object?)o.Quelled), true);
        yield return ("MapEnabled", Safe(() => (object?)o.MapEnabled), true);
        yield return ("LastMapTime", Safe(() => (object?)o.LastMapTime), true);
        yield return ("Gender", Safe(() => (object?)o.Gender), true);
        yield return ("TickSeconds", Safe(() => (object?)o.TickSeconds), true);
        yield return ("Location", Safe(() => (object?)o.Location), true);
        yield return ("Home", Safe(() => (object?)o.Home), true);
        yield return ("session", Safe(() => (object?)o.Session), true);
        yield return ("NoFollow", Safe(() => (object?)o.NoFollow), true);
        yield return ("Following", Safe(() => (object?)o.Following), true);
        yield return ("GroupChannel", Safe(() => (object?)o.GroupChannel), true);
        yield return ("external_cmdset", Safe(() => (object?)o.ExternalCmdSet), true);
        yield return ("internal_cmdset", Safe(() => (object?)o.InternalCmdSet), true);
        yield return ("last_touched_by", Safe(() => (object?)o.LastTouchedBy), true);
        yield return ("Tags", Safe(() => (object?)o.TagsSnapshot), false);
        yield return ("Aliases", Safe(() => (object?)o.Aliases), true);
        yield return ("_contents", Safe(() => (object?)o.ContentsSnapshot), false);
        yield return ("followers", Safe(() => (object?)o.FollowersSnapshot), false);
        yield return ("scripts", Safe(() => (object?)o.ScriptsSnapshot), false);
        yield return ("Channels", Safe(() => (object?)o.ChannelsSnapshot), false);
        yield return ("locks", Safe(() => (object?)o.GetLockPoliciesSnapshot()), false);
        yield return ("extra", Safe(() => (object?)o.GetExtraSnapshot()), false);
    }

    private static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
        { "access", "lock", "password", "secret_token", "secret" };

    public static List<(string name, object? value, bool isProperty)> Collect(GameObject target)
    {
        return target.GetExamMembers()
            .Where(m => !Ignore.Contains(m.name)
                && !m.name.Contains("password", StringComparison.OrdinalIgnoreCase)
                && !m.name.Contains("secret", StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.name)
            .ToList();
    }

    // getattr(val, name, None) equivalent — a throwing accessor
    // yields default instead of aborting the render.
    private static T? SafeGet<T>(Func<T> f)
    {
        try { return f(); }
        catch (Exception) { return default; }
    }
    private static string ExpandId(int id)    {
        try
        {
            var res = ObjectRegistry.Get(id);
            if (res.Count > 0) return $"#{id} ({res[0].Name})";
        }
        catch (Exception) { }
        return $"#{id}";
    }

    private static string LambdaSource(Delegate fn)
    {
        try { return fn.Method.ToString() ?? fn.Method.Name; } catch { return "<callable>"; }
    }

    public static object FormatValue(object? val, string? hint)
    {
        if (hint is not null && (hint.Contains("password", StringComparison.OrdinalIgnoreCase) || hint.Contains("secret", StringComparison.OrdinalIgnoreCase))) return "<hidden>";
        if (hint == "internal_cmdset") return "<hidden>";
        if (hint == "external_cmdset")
        {
            if (val is null) return "None";
            if (val is CmdSet cs)
            {
                HashSet<int> seen = [];
                List<string> keys = [];
                foreach (var cmd in cs.GetAll())
                {
                    if (cmd is null) continue;
                    if (!seen.Add(RuntimeHelpers.GetHashCode(cmd))) continue;
                    keys.Add(cmd.Key);
                }
                return keys.Count > 0 ? "[" + string.Join(", ", keys) + "]" : "[]";
            }
            return "<hidden>";
        }
        if (hint == "followers")
        {
            if (val is null) return "set()";
            try
            {
                if (val is System.Collections.IEnumerable en)
                {
                    List<int> ids = [];
                    foreach (var e in en) if (e is int i) ids.Add(i);
                    else if (int.TryParse(e?.ToString(), out var pi)) ids.Add(pi);
                    if (ids.Count == 0) return "set()";
                    return "{" + string.Join(", ", ids.Select(ExpandId)) + "}";
                }
            }
            catch (Exception) { }
            return "set()";
        }
        if (hint == "created_by" || hint == "last_touched_by")
        {
            if (val is int iv)
            {
                if (iv == -1) return "-1";
                return ExpandId(iv).Replace("#", "");
                // Python: f"{val} ({name})" if name else str(val)
                // ExpandId returns "#id (name)", we want "id (name)" to match python's "{val} ({name})"
            }
            // Fallback try convert
            try
            {
                int id = Convert.ToInt32(val);
                if (id == -1) return "-1";
                var name = ObjectRegistry.Get(id).FirstOrDefault()?.Name;
                return name is not null ? $"{id} ({name})" : id.ToString();
            }
            catch (Exception) { }
            return val?.ToString() ?? "None";
        }
        if (hint == "scripts")
        {
            if (val is null) return "set()";
            try
            {
                if (val is System.Collections.IEnumerable en)
                {
                    List<int> ids = [];
                    bool allInts = true;
                    int count = 0;
                    foreach (var e in en) { count++; if (e is int i) ids.Add(i); else { allInts = false; break; } }
                    if (count == 0) return "set()";
                    if (allInts) return "{" + string.Join(", ", ids.Select(ExpandId)) + "}";
                }
            }
            catch (Exception) { }
            // fallback
        }
        if (hint == "_contents")
        {
            if (val is null) return "set()";
            try
            {
                if (val is System.Collections.IEnumerable en)
                {
                    List<int> ids = [];
                    bool allInts = true;
                    int count = 0;
                    foreach (var e in en) { count++; if (e is int i) ids.Add(i); else { allInts = false; break; } }
                    if (count == 0) return "set()";
                    if (allInts) return "{" + string.Join(", ", ids.Select(ExpandId)) + "}";
                }
            }
            catch (Exception) { }
        }
        if (hint == "locks")
        {
            // Python returns list[str] where first is "" and rest are "lock: [lambda...]"
            var lines = new List<string> { "" };
            if (val is not null)
            {
                try
                {
                    if (val is System.Collections.IDictionary lockDict)
                    {
                        foreach (System.Collections.DictionaryEntry kv in lockDict)
                        {
                            string lockName = kv.Key?.ToString() ?? "";
                            var callables = kv.Value as System.Collections.IEnumerable;
                            List<string> bodies = [];
                            if (callables is not null)
                            {
                                foreach (var fn in callables)
                                {
                                    if (fn is Delegate d) bodies.Add(LambdaSource(d));
                                    else bodies.Add(fn?.ToString() ?? "<callable>");
                                }
                            }
                            lines.Add($"{lockName}: [{string.Join(", ", bodies)}]");
                        }
                    }
                }
                catch (Exception) { }
            }
            return lines;
        }
        if (hint == "session")
        {
            if (val is null) return "None";
            if (val is not Session sess) return val.ToString() ?? "Session()";
            List<string> parts = [];
            // getattr-style guards — one throwing accessor (e.g. a
            // puppet whose Name raises) must not abort the whole exam list.
            var acc = SafeGet(() => sess.Account);
            if (acc is not null) parts.Add($"account={SafeGet(() => acc.Name) ?? "?"} (#{SafeGet(() => acc.Id)})");
            var conn = SafeGet(() => sess.Connection);
            if (conn is not null) parts.Add($"conn={SafeGet(() => conn.ClientHost) ?? SafeGet(() => conn.SessionId) ?? "?"}");
            var puppet = SafeGet(() => sess.Puppet);
            if (puppet is not null) parts.Add($"puppet={SafeGet(() => puppet.Name) ?? "?"} (#{SafeGet(() => puppet.Id)})");
            var tw = SafeGet(() => sess.TermWidth); var th = SafeGet(() => sess.TermHeight);
            if (tw != 0 && th != 0) parts.Add($"w={tw}, h={th}");
            if (SafeGet(() => sess.ScreenReader)) parts.Add("sr=True");
            return parts.Count > 0 ? "Session(" + string.Join(", ", parts) + ")" : "Session()";
        }
        if (val is null) return "None";
        if (val is string s) return s;
        // Typed lock check: no type named RLock exists in C# (the name is a
        // leftover of the original RLock); the live lock type is
        // ReaderWriterLockSlim, matched here directly.
        if (val is System.Threading.ReaderWriterLockSlim) return "<RLock>";
        // dict handling before general IEnumerable
        if (val is System.Collections.IDictionary genDict)
        {
            List<string> items = [];
            foreach (System.Collections.DictionaryEntry kv in genDict)
            {
                var kf = FormatValue(kv.Key, null) as string ?? kv.Key?.ToString() ?? "";
                // Key-aware recursion: a nested "password"/"secret" key redacts
                // its value exactly like a top-level hint does.
                var vf = FormatValue(kv.Value, kv.Key?.ToString()) as string ?? kv.Value?.ToString() ?? "";
                items.Add($"{kf}: {vf}");
            }
            return "{" + string.Join(", ", items) + "}";
        }
        var valType2 = val.GetType();
        if (val is System.Collections.IEnumerable en2 && val is not string)
        {
            // Tuples render via ToString (matched by interface, not by name,
            // so unrelated generic types with Tuple in their name are left
            // to the element rendering below).
            if (val is ITuple) return val.ToString() ?? "<unprintable>";
            List<string> elems = [];
            foreach (var e in en2) elems.Add(FormatValue(e, null) as string ?? e?.ToString() ?? "");
            // Set check by generic definition: matches HashSet of any element
            // type without catching unrelated types that merely contain
            // HashSet in their name.
            if (valType2.IsGenericType && valType2.GetGenericTypeDefinition() == typeof(System.Collections.Generic.HashSet<>)) return "{" + string.Join(", ", elems) + "}";
            if (val is System.Array) return "(" + string.Join(", ", elems) + ")";
            return "[" + string.Join(", ", elems) + "]";
        }
        try { return val.ToString() ?? "<unprintable>"; } catch { return "<unprintable>"; }
    }
}
