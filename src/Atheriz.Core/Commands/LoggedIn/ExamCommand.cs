// Port of atheriz/commands/loggedin/exam.py:265
using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class ExamCommand : Command
{
    public override string Key => "examine";
    public override IReadOnlyList<string> Aliases => ["exam", "ex", "exa"];
    public override string Desc => "Examine an object to see its attributes.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("target", nargs: "?", help: "Object to examine (name or #id).");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        string? targetStr = pa?.GetString("target");
        GameObject? target = null;
        if (string.IsNullOrEmpty(targetStr))
        {
            target = go.ResolveLocationObject();
            if (target == null) { go.Msg("You are nowhere to examine."); return; }
        }
        else
        {
            target = CommandHelpers.ResolveObject(go, targetStr!);
            if (target == null) return;
        }
        if (target.IsNode && target is Node nodeTarget)
        {
            string areaName;
            try
            {
                var nh = NodeHandler.GetCurrent();
                var area = nh?.GetArea(nodeTarget.Coord.Area);
                areaName = area?.Name ?? nodeTarget.Coord.Area;
            }
            catch { areaName = nodeTarget.Coord.Area; }
            go.Msg($"Examining Node at {nodeTarget.Coord} in area '{areaName}', z={nodeTarget.Coord.Z} (#{target.Id}):");
        }
        else go.Msg($"Examining {target.Name} (#{target.Id}):");
        var ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "access", "lock", "password", "secret_token", "secret" };
        var dict = new Dictionary<string, object?>();
        var propNames = new HashSet<string>();
        // Collect fields via vars(target) equivalent
        foreach (var f in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            try { if (!f.Name.StartsWith("<")) dict[f.Name] = f.GetValue(target); } catch { dict[f.Name] = "<error>"; }
        }
        // Also include base type fields that may be private
        // Collect properties as [property] marker
        foreach (var c in GetMro(target.GetType()))
        {
            foreach (var kv in c.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (kv.GetIndexParameters().Length > 0) continue;
                string name = kv.Name;
                if (dict.ContainsKey(name)) continue;
                // Check if property defined in this type
                var getter = kv.GetMethod;
                if (getter == null) continue;
                // Consider it a property
                propNames.Add(name);
                try { dict[name] = kv.GetValue(target); } catch { dict[name] = "<error>"; }
            }
        }
        // Fallback: also check public properties not covered by MRO walk
        foreach (var p in target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            if (dict.ContainsKey(p.Name)) continue;
            propNames.Add(p.Name);
            try { dict[p.Name] = p.GetValue(target); } catch { dict[p.Name] = "<error>"; }
        }
        var sorted = dict.Keys.Where(k => !ignore.Contains(k) && !k.Contains("password", StringComparison.OrdinalIgnoreCase) && !k.Contains("secret", StringComparison.OrdinalIgnoreCase)).OrderBy(k => k).ToList();
        foreach (var key in sorted)
        {
            var val = dict[key];
            var valOutput = FormatValue(val, key);
            string typeName = val?.GetType().Name ?? "null";
            string marker = propNames.Contains(key) ? " [property]" : "";
            if (valOutput is List<string> list)
            {
                go.Msg($"  {key}: {list[0]} ({typeName}{marker})");
                for (int i = 1; i < list.Count; i++) go.Msg($"    {list[i]}");
            }
            else
            {
                string valStr = valOutput as string ?? valOutput?.ToString() ?? "<unprintable>";
                go.Msg($"  {key}: {valStr} ({typeName}{marker})");
            }
        }
    }

    private static IEnumerable<Type> GetMro(Type t)
    {
        var cur = t;
        while (cur != null)
        {
            yield return cur;
            cur = cur.BaseType;
        }
    }

    private static string ExpandId(int id)
    {
        try
        {
            var res = ObjectRegistry.Get(id);
            if (res.Count > 0) return $"#{id} ({res[0].Name})";
        } catch {}
        return $"#{id}";
    }

    private static string LambdaSource(Delegate fn)
    {
        try { return fn.Method.ToString() ?? fn.Method.Name; } catch { return "<callable>"; }
    }

    private static object FormatValue(object? val, string? hint)
    {
        if (hint != null && (hint.Contains("password", StringComparison.OrdinalIgnoreCase) || hint.Contains("secret", StringComparison.OrdinalIgnoreCase))) return "<hidden>";
        if (hint == "internal_cmdset") return "<hidden>";
        if (hint == "external_cmdset")
        {
            if (val == null) return "None";
            try
            {
                var m = val.GetType().GetMethod("GetAll");
                if (m != null)
                {
                    var all = m.Invoke(val, null) as System.Collections.IEnumerable;
                    if (all != null)
                    {
                        var seen = new HashSet<int>();
                        var keys = new List<string>();
                        foreach (var cmd in all)
                        {
                            if (cmd == null) continue;
                            var id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(cmd);
                            if (!seen.Add(id)) continue;
                            var kp = cmd.GetType().GetProperty("Key")?.GetValue(cmd) as string;
                            if (kp != null) keys.Add(kp);
                        }
                        return keys.Count > 0 ? "[" + string.Join(", ", keys) + "]" : "[]";
                    }
                }
            } catch {}
            return "<hidden>";
        }
        if (hint == "followers")
        {
            if (val == null) return "set()";
            try
            {
                if (val is System.Collections.IEnumerable en)
                {
                    var ids = new List<int>();
                    foreach (var e in en) if (e is int i) ids.Add(i);
                    else if (int.TryParse(e?.ToString(), out var pi)) ids.Add(pi);
                    if (ids.Count == 0) return "set()";
                    return "{" + string.Join(", ", ids.Select(ExpandId)) + "}";
                }
            } catch {}
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
                return name != null ? $"{id} ({name})" : id.ToString();
            } catch {}
            return val?.ToString() ?? "None";
        }
        if (hint == "scripts")
        {
            if (val == null) return "set()";
            try
            {
                if (val is System.Collections.IEnumerable en)
                {
                    var ids = new List<int>();
                    bool allInts = true;
                    int count = 0;
                    foreach (var e in en) { count++; if (e is int i) ids.Add(i); else { allInts = false; break; } }
                    if (count == 0) return "set()";
                    if (allInts) return "{" + string.Join(", ", ids.Select(ExpandId)) + "}";
                }
            } catch {}
            // fallback
        }
        if (hint == "_contents")
        {
            if (val == null) return "set()";
            try
            {
                if (val is System.Collections.IEnumerable en)
                {
                    var ids = new List<int>();
                    bool allInts = true;
                    int count = 0;
                    foreach (var e in en) { count++; if (e is int i) ids.Add(i); else { allInts = false; break; } }
                    if (count == 0) return "set()";
                    if (allInts) return "{" + string.Join(", ", ids.Select(ExpandId)) + "}";
                }
            } catch {}
        }
        if (hint == "locks")
        {
            // Python returns list[str] where first is "" and rest are "lock: [lambda...]"
            var lines = new List<string> { "" };
            if (val != null)
            {
                try
                {
                    if (val is System.Collections.IDictionary lockDict)
                    {
                        foreach (System.Collections.DictionaryEntry kv in lockDict)
                        {
                            string lockName = kv.Key?.ToString() ?? "";
                            var callables = kv.Value as System.Collections.IEnumerable;
                            var bodies = new List<string>();
                            if (callables != null)
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
                    else
                    {
                        var valType = val.GetType();
                        if (valType.IsGenericType && valType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                        {
                            foreach (var kv in (System.Collections.IEnumerable)val)
                            {
                                var kvType = kv.GetType();
                                var k = kvType.GetProperty("Key")?.GetValue(kv);
                                var v = kvType.GetProperty("Value")?.GetValue(kv);
                                string lockName = k?.ToString() ?? "";
                                var callables = v as System.Collections.IEnumerable;
                                var bodies = new List<string>();
                                if (callables != null)
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
                } catch {}
            }
            return lines;
        }
        if (hint == "session")
        {
            if (val == null) return "None";
            var parts = new List<string>();
            try
            {
                var accField = val.GetType().GetField("Account") ?? val.GetType().GetField("account");
                var accProp = val.GetType().GetProperty("Account") ?? val.GetType().GetProperty("account");
                var acc = accField?.GetValue(val) ?? accProp?.GetValue(val);
                if (acc != null)
                {
                    var name = acc.GetType().GetProperty("Name")?.GetValue(acc) as string ?? acc.GetType().GetField("Name")?.GetValue(acc) as string;
                    var id = acc.GetType().GetProperty("Id")?.GetValue(acc) ?? acc.GetType().GetField("Id")?.GetValue(acc);
                    if (name != null) parts.Add($"account={name} (#{id})");
                }
            } catch {}
            try
            {
                var connField = val.GetType().GetField("Connection") ?? val.GetType().GetField("connection");
                var connProp = val.GetType().GetProperty("Connection") ?? val.GetType().GetProperty("connection");
                var conn = connField?.GetValue(val) ?? connProp?.GetValue(val);
                if (conn != null)
                {
                    var host = conn.GetType().GetProperty("ClientHost")?.GetValue(conn) as string
                        ?? conn.GetType().GetField("ClientHost")?.GetValue(conn) as string
                        ?? conn.GetType().GetProperty("SessionId")?.GetValue(conn) as string
                        ?? conn.GetType().GetField("SessionId")?.GetValue(conn) as string
                        ?? conn.GetType().GetProperty("client_host")?.GetValue(conn) as string
                        ?? "?";
                    parts.Add($"conn={host}");
                }
            } catch {}
            try
            {
                var puppetField = val.GetType().GetField("Puppet") ?? val.GetType().GetField("puppet");
                var puppetProp = val.GetType().GetProperty("Puppet") ?? val.GetType().GetProperty("puppet");
                var puppet = puppetField?.GetValue(val) ?? puppetProp?.GetValue(val);
                if (puppet != null)
                {
                    var name = puppet.GetType().GetProperty("Name")?.GetValue(puppet) as string ?? puppet.GetType().GetField("_name")?.GetValue(puppet) as string;
                    if (name == null) name = puppet.GetType().GetProperty("Name")?.GetValue(puppet) as string;
                    var id = puppet.GetType().GetProperty("Id")?.GetValue(puppet) ?? puppet.GetType().GetField("_id")?.GetValue(puppet);
                    if (name != null) parts.Add($"puppet={name} (#{id})");
                }
            } catch {}
            try
            {
                var wField = val.GetType().GetField("TermWidth") ?? val.GetType().GetField("term_width");
                var wProp = val.GetType().GetProperty("TermWidth") ?? val.GetType().GetProperty("term_width");
                var hField = val.GetType().GetField("TermHeight") ?? val.GetType().GetField("term_height");
                var hProp = val.GetType().GetProperty("TermHeight") ?? val.GetType().GetProperty("term_height");
                var w = wField?.GetValue(val) ?? wProp?.GetValue(val);
                var h = hField?.GetValue(val) ?? hProp?.GetValue(val);
                if (w is int wi && h is int hi && wi != 0 && hi != 0) parts.Add($"w={wi}, h={hi}");
            } catch {}
            try
            {
                var srField = val.GetType().GetField("ScreenReader") ?? val.GetType().GetField("screenreader");
                var srProp = val.GetType().GetProperty("ScreenReader") ?? val.GetType().GetProperty("screenreader");
                var sr = srField?.GetValue(val) ?? srProp?.GetValue(val);
                if (sr is bool b && b) parts.Add("sr=True");
            } catch {}
            return parts.Count > 0 ? "Session(" + string.Join(", ", parts) + ")" : "Session()";
        }
        if (val == null) return "None";
        if (val is string s) return s;
        if (val.GetType().Name == "RLock") return "<RLock>";
        // dict handling before general IEnumerable
        if (val is System.Collections.IDictionary genDict)
        {
            var items = new List<string>();
            foreach (System.Collections.DictionaryEntry kv in genDict)
            {
                var kf = FormatValue(kv.Key, null) as string ?? kv.Key?.ToString() ?? "";
                var vf = FormatValue(kv.Value, null) as string ?? kv.Value?.ToString() ?? "";
                // For nested list case (locks) already handled above, so here string
                items.Add($"{kf}: {vf}");
            }
            return "{" + string.Join(", ", items) + "}";
        }
        var valType2 = val.GetType();
        if (valType2.IsGenericType && valType2.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var items = new List<string>();
            foreach (var kv in (System.Collections.IEnumerable)val)
            {
                var kvType = kv.GetType();
                var k = kvType.GetProperty("Key")?.GetValue(kv);
                var v = kvType.GetProperty("Value")?.GetValue(kv);
                var kf = FormatValue(k, null) as string ?? k?.ToString() ?? "";
                var vf = FormatValue(v, null) as string ?? v?.ToString() ?? "";
                items.Add($"{kf}: {vf}");
            }
            return "{" + string.Join(", ", items) + "}";
        }
        if (val is System.Collections.IEnumerable en2 && !(val is string))
        {
            if (valType2.IsGenericType && valType2.Name.Contains("Tuple")) return val.ToString() ?? "<unprintable>";
            if (valType2.GetProperty("_fields") != null) return val.ToString() ?? "<unprintable>";
            var elems = new List<string>();
            foreach (var e in en2) elems.Add(FormatValue(e, null) as string ?? e?.ToString() ?? "");
            if (val is System.Collections.Generic.HashSet<int> || val is System.Collections.Generic.HashSet<string> || val.GetType().Name.Contains("HashSet")) return "{" + string.Join(", ", elems) + "}";
            if (val is System.Array || val.GetType().Name.Contains("Tuple")) return "(" + string.Join(", ", elems) + ")";
            return "[" + string.Join(", ", elems) + "]";
        }
        try { return val.ToString() ?? "<unprintable>"; } catch { return "<unprintable>"; }
    }
}
