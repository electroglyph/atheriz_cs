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
    /// One static table of accessors, wrapped once: values are still read per
    /// object on every call (never cached across objects).
    /// </summary>
    private static readonly (string name, Func<GameObject, object?> get, bool isProperty)[] BaseMemberTable =
    [
        ("Id", o => o.Id, true),
        ("Name", o => o.Name, true),
        ("Desc", o => o.Desc, true),
        ("Symbol", o => o.Symbol, true),
        ("MoveVerb", o => o.MoveVerb, true),
        ("PrivilegeLevel", o => o.PrivilegeLevel, true),
        ("IsPc", o => o.IsPc, true),
        ("IsNpc", o => o.IsNpc, true),
        ("IsItem", o => o.IsItem, true),
        ("IsMapable", o => o.IsMapable, true),
        ("IsContainer", o => o.IsContainer, true),
        ("IsScript", o => o.IsScript, true),
        ("IsTickable", o => o.IsTickable, true),
        ("IsAccount", o => o.IsAccount, true),
        ("IsChannel", o => o.IsChannel, true),
        ("IsNode", o => o.IsNode, true),
        ("IsModified", o => o.IsModified, true),
        ("IsDeleted", o => o.IsDeleted, true),
        ("IsConnected", o => o.IsConnected, true),
        ("IsTemporary", o => o.IsTemporary, true),
        ("IsBanned", o => o.IsBanned, true),
        ("CanHear", o => o.CanHear, true),
        ("Quelled", o => o.Quelled, true),
        ("MapEnabled", o => o.MapEnabled, true),
        ("LastMapTime", o => o.LastMapTime, true),
        ("Gender", o => o.Gender, true),
        ("TickSeconds", o => o.TickSeconds, true),
        ("Location", o => o.Location, true),
        ("Home", o => o.Home, true),
        ("session", o => o.Session, true),
        ("NoFollow", o => o.NoFollow, true),
        ("Following", o => o.Following, true),
        ("GroupChannel", o => o.GroupChannel, true),
        ("external_cmdset", o => o.ExternalCmdSet, true),
        ("internal_cmdset", o => o.InternalCmdSet, true),
        ("last_touched_by", o => o.LastTouchedBy, true),
        ("Tags", o => o.TagsSnapshot, false),
        ("Aliases", o => o.Aliases, true),
        ("_contents", o => o.ContentsSnapshot, false),
        ("followers", o => o.FollowersSnapshot, false),
        ("scripts", o => o.ScriptsSnapshot, false),
        ("Channels", o => o.ChannelsSnapshot, false),
        ("locks", o => o.GetLockPoliciesSnapshot(), false),
        ("extra", o => o.GetExtraSnapshot(), false),
    ];

    public static IEnumerable<(string name, object? value, bool isProperty)> BaseMembers(GameObject o)
    {
        foreach (var (name, get, isProperty) in BaseMemberTable)
        {
            object? value;
            try { value = get(o); }
            catch { value = "<error>"; }
            yield return (name, value, isProperty);
        }
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
            var single = ObjectRegistry.GetSingle(id);
            if (single is not null) return $"#{id} ({single.Name})";
        }
        catch (Exception) { }
        return $"#{id}";
    }

    // Shared collect-and-join core for the followers/scripts/_contents hints:
    // gathers int ids (plus int-parseable strings, as the followers hint
    // always has), skipping anything else. False when nothing renderable was
    // found (non-enumerable or empty); with strictInts, also false on the
    // first non-int so scripts/_contents can fall through to the generic
    // renderer exactly as before instead of rendering a partial set.
    internal static bool TryCollectIds(object? value, out List<int> ids)
        => TryCollectIds(value, strictInts: false, out ids);

    internal static bool TryCollectIds(object? value, bool strictInts, out List<int> ids)
    {
        ids = [];
        if (value is not System.Collections.IEnumerable en) return false;
        foreach (var e in en)
        {
            if (e is int i) { ids.Add(i); continue; }
            if (strictInts) return false;
            if (int.TryParse(e?.ToString(), out var pi)) ids.Add(pi);
        }
        return ids.Count > 0;
    }

    internal static string FormatIdSet(List<int> ids)
        => "{" + string.Join(", ", ids.Select(ExpandId)) + "}";

    // Empty-enumerable check for the strict id-set hints: a throwing
    // enumerable counts as non-empty (the old single-pass loop's exception
    // fell through to the generic renderer, never to "set()").
    private static bool IsEmpty(System.Collections.IEnumerable en)
    {
        try
        {
            var it = en.GetEnumerator();
            try { return !it.MoveNext(); }
            finally { (it as IDisposable)?.Dispose(); }
        }
        catch { return false; }
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
                if (val is System.Collections.IEnumerable)
                    return TryCollectIds(val, out var followerIds) ? FormatIdSet(followerIds) : "set()";
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
                var name = ObjectRegistry.GetSingle(id)?.Name;
                return name is not null ? $"{id} ({name})" : id.ToString();
            }
            catch (Exception) { }
            return val?.ToString() ?? "None";
        }
        // The scripts and _contents blocks were identical (strict ints-only
        // collect; empty renders "set()"; non-int members fall through to the
        // generic renderer below instead of rendering a partial set).
        if (hint is "scripts" or "_contents")
        {
            if (val is null) return "set()";
            try
            {
                if (val is System.Collections.IEnumerable en)
                {
                    if (TryCollectIds(en, strictInts: true, out var memberIds)) return FormatIdSet(memberIds);
                    if (IsEmpty(en)) return "set()";
                }
            }
            catch (Exception) { }
            // fallback
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
