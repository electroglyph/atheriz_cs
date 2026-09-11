
using System.Collections.Frozen;

namespace Atheriz.Core.Objects;

/// <summary>
/// Port of <c>atheriz/objects/contents.py</c>.
/// </summary>
public static class ContentUtils
{
    private static readonly FrozenSet<string> SingularWords = new[]
    {
        "glass","grass","brass","class","mass","bass","pass","lass","crass",
        "bus","gas","plus","pus","thus","virus","campus","bonus","census",
        "octopus","walrus","compass","cactus","genus","genius","ignoramus",
        "apparatus","corpus","hippopotamus","platypus","rhinoceros","syllabus",
        "abacus","focus","lotus","fungus","nucleus","radius","stimulus",
        "axis","oasis","iris","basis","crisis","analysis","thesis","synopsis",
        "ellipsis","hypothesis","parenthesis","synthesis","diagnosis","prognosis",
        "chaos","cosmos","kudos","pathos","ethos",
        "atlas","alias","canvas","cannabis","ibis","asbestos",
        "lens","biceps","triceps","series","species","news",
        "measles","mumps","rabies","diabetes",
        "economics","politics","physics","mathematics","athletics","gymnastics",
        "barracks","chassis","precis",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Port of settings.MAX_SEARCH_DEPTH — mutable for testing (mirrors monkeypatch in test_contents_search.py:340)
    public static int MaxSearchDepth = 100;

    // Lowered word set backing search matching, computed once per object per
    // search (never cached across searches — names change). Membership here
    // reproduces the old per-term TermMatches exactly: single-word terms can
    // only match whole words, and a single-word name/alias is itself one word,
    // so the old full-string equality arms add nothing beyond word lookup.
    private static HashSet<string> LoweredSearchTerms(GameObject obj)
    {
        HashSet<string> terms = new(StringComparer.Ordinal);
        string nameL;
        try { nameL = (obj.Name ?? "").ToLowerInvariant(); } catch { nameL = ""; }
        foreach (var w in nameL.Split(' ', StringSplitOptions.RemoveEmptyEntries)) terms.Add(w);
        List<string> aliases;
        try { aliases = obj.Aliases; } catch { aliases = []; }
        foreach (var alias in aliases)
        {
            if (alias is null) continue;
            string aliasL;
            try { aliasL = alias.ToLowerInvariant(); } catch { continue; }
            foreach (var w in aliasL.Split(' ', StringSplitOptions.RemoveEmptyEntries)) terms.Add(w);
        }
        return terms;
    }

    public static List<GameObject> FilterVisible(List<GameObject> objs, GameObject? looker)
    {
        // The null-looker path returns a copy: handing out the input list by
        // reference lets a caller mutate the owner's collection through the
        // result.
        if (looker is null) return new List<GameObject>(objs);
        return objs.Where(o => o != looker && o.Access(looker, "view")).ToList();
    }

    public static List<GameObject> FilterContents(GameObject obj, Func<GameObject, bool> predicate)
    {
        return obj.ContentsSnapshot.Select(Globals.ObjectRegistry.GetSingle).OfType<GameObject>().Where(predicate).ToList();
    }

    public static string GroupByName(List<GameObject> objs, GameObject? looker = null)
    {
        if (objs.Count == 0) return "";
        // Case-insensitive grouping matches the case-folding search: "Sword"
        // and "sword" are found together, so they display as one stack.
        var groups = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in objs)
        {
            var name = looker is not null ? o.GetDisplayName(looker) : o.Name;
            groups.TryGetValue(name, out var c);
            groups[name] = c + 1;
        }
        return string.Join(", ", groups.Select(kv => kv.Value > 1 ? $"{kv.Key}({kv.Value})" : kv.Key));
    }

    /// <summary>
    /// Recursively gather contents, descending into is_container.
    /// </summary>
    public static List<GameObject> GatherContents(GameObject root, Func<int, GameObject?> resolver, HashSet<int>? visited = null, int depth = 0, GameObject? looker = null)
    {
        visited ??= [];
        if (depth >= MaxSearchDepth) return [];
        List<GameObject> result = [];
        var ids = root.ContentsSnapshot;
        foreach (var id in ids)
        {
            if (!visited.Add(id)) continue;
            // One bad id must not abort the whole walk at any depth: the
            // nested call below is already failure-tolerant, so the top
            // level resolves under the same log-and-continue treatment.
            GameObject? o;
            try { o = resolver(id); }
            catch (Exception ex) { AtherizLogger.LogDebug($"Suppressed ContentUtils.GatherContents: {ex.Message}", "ContentUtils"); continue; }
            if (o is null) continue;
            if (looker is not null && !o.Access(looker, "view")) continue;
            result.Add(o);
            if (o.IsContainer)
            {
                // one container's resolver failure must not drop the
                // remaining siblings — log and continue, don't break.
                try { result.AddRange(GatherContents(o, resolver, visited, depth + 1, looker)); }
                catch (Exception ex) { AtherizLogger.LogDebug($"Suppressed ContentUtils.GatherContents: {ex.Message}", "ContentUtils"); }
            }
        }
        return result;
    }

    /// <summary>
    /// Port of <c>contents.search</c>. Returns list matching query.
    /// </summary>
    public static List<GameObject> Search(GameObject obj, string query, Func<int, GameObject?> resolver, bool recursive = true, GameObject? looker = null)
    {
        if (query is null) return [];
        string q;
        try { q = query.ToLowerInvariant().Trim(); } catch { return []; }
        if (q == "me") return [obj];

        var objs = recursive ? GatherContents(obj, resolver, looker: looker) : obj.ContentsSnapshot.Select(resolver).OfType<GameObject>().ToList();
        // The recursive walk already applied the looker view filter per
        // object; re-filter only the flat path, which resolves unfiltered.
        if (looker is not null && !recursive)
            objs = objs.Where(o => o.Access(looker, "view")).ToList();

        if (q.StartsWith("#", StringComparison.Ordinal))
        {
            if (!int.TryParse(q[1..], out var id)) return [];
            foreach (var o in objs) if (o.Id == id) return [o];
            return [];
        }

        var split = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (split.Length == 0) return [];
        List<string> optional = [];
        List<string> required = [];
        var count = 1;
        var index = 0;
        var start = 0;
        var end = split.Length;
        if (split[0] == "all")
        {
            count = 0;
            start = 1;
            if (start >= end) return new List<GameObject>(objs);
        }
        else if (int.TryParse(split[0], out var n0))
        {
            count = n0;
            start = 1;
        }
        if (end > start && int.TryParse(split[^1], out var n1))
        {
            index = n1;
            if (index < 1) return [];
            end -= 1;
        }
        for (var x = start; x < end; x++)
        {
            var token = split[x];
            if (SingularWords.Contains(token))
                required.Add(token);
            else if (token.Length > 3 && token.EndsWith("ies", StringComparison.Ordinal))
            {
                if (count == 1) count = 0;
                required.Add(token[..^3] + "y");
                optional.Add(token);
            }
            else if (token.Length > 2 && token.EndsWith("es", StringComparison.Ordinal))
            {
                if (count == 1) count = 0;
                optional.Add(token[..^2]);
                optional.Add(token[..^1]);
                optional.Add(token);
            }
            else if (token.Length > 1 && token.EndsWith("s", StringComparison.Ordinal))
            {
                if (count == 1) count = 0;
                required.Add(token[..^1]);
                optional.Add(token);
            }
            else if (token.Length > 1 && token.EndsWith("i", StringComparison.Ordinal))
            {
                if (count == 1) count = 0;
                required.Add(token[..^1] + "us");
                optional.Add(token);
            }
            else required.Add(token);
        }

        List<GameObject> matches = [];
        // Membership acceleration for the duplicate guard below: GameObject
        // equality is Id-based, so HashSet membership decides exactly what the
        // old List.Contains decided. Insertion order still comes from the
        // matches list iteration, keeping first-match-wins and indexed picks.
        HashSet<GameObject> seenMatches = [];
        List<GameObject>? RecordMatch(GameObject o)
        {
            if (count == 1 && index == 0) return [o];
            if (seenMatches.Add(o)) matches.Add(o);
            if (matches.Count == count && index == 0) return matches;
            return null;
        }
        for (var i = 0; i < objs.Count; i++)
        {
            var terms = LoweredSearchTerms(objs[i]);
            // An empty required set matches nothing (found stays false), as
            // before — All() alone would be vacuously true.
            bool found = required.Count > 0 && required.All(terms.Contains);
            if (found)
            {
                var done = RecordMatch(objs[i]);
                if (done is not null) return done;
                continue;
            }
            foreach (var s in optional)
            {
                if (terms.Contains(s))
                {
                    var done = RecordMatch(objs[i]);
                    if (done is not null) return done;
                    break;
                }
            }
        }
        if (count == 0)
        {
            if (index == 0) return matches;
            if (index <= matches.Count) return [matches[index - 1]];
            return [];
        }
        if (index == 0 && matches.Count > count) return matches[..count];
        if (index != 0 && index <= matches.Count) return [matches[index - 1]];
        if (index != 0 && index > matches.Count) return [];
        return matches;
    }

    /// <summary>
    /// Location-announce dispatch preserving the Node/base MsgContents split:
    /// node locations use the live-contents overload (catch-all parser
    /// fallback), other locations use the registry-snapshot overload
    /// (ParsingError-only fallback). The branch is load-bearing — routing a
    /// node through the base overload would change delivery and error
    /// semantics — so this helper keeps dynamic dispatch behind one call.
    /// </summary>
    public static void EmitToLocation(GameObject loc, string? text, GameObject? fromObj = null, IDictionary<string, object?>? mapping = null, IEnumerable<GameObject>? exclude = null, string? msgType = null)
    {
        ArgumentNullException.ThrowIfNull(loc);
        if (loc is Node node)
            node.MsgContents(text, exclude: exclude, fromObj: fromObj, mapping: mapping, msgType: msgType);
        else
            loc.MsgContents(text, fromObj: fromObj, mapping: mapping, exclude: exclude, msgType: msgType);
    }

    /// <summary>
    /// Shared broadcast core behind <see cref="GameObject.MsgContents"/> and
    /// <see cref="Node.MsgContents"/>. Receivers stay caller-resolved (live
    /// contents vs registry snapshot differ per overload); the mapping copy,
    /// "you" default, exclude set, per-receiver parse, and delivery merge
    /// here. <paramref name="nodeSemantics"/> selects the preserved contract:
    /// node delivery falls back to the raw text on any parser failure and
    /// never throws (ignoring <paramref name="raiseErrors"/>), while object
    /// delivery falls back only on <see cref="FuncParser.ParsingError"/>,
    /// rethrows it when <paramref name="raiseErrors"/> is set, and lets any
    /// other parser exception propagate. Empty text delivers "" in both
    /// contracts (node delivery sends it outside the suppression guard, as
    /// before).
    /// </summary>
    public static void EmitToContents(IReadOnlyList<GameObject> receivers, GameObject host, string? text, GameObject? fromObj, IDictionary<string, object?>? mapping, IEnumerable<GameObject>? exclude, string? msgType, bool raiseErrors, bool nodeSemantics)
    {
        ArgumentNullException.ThrowIfNull(receivers);
        ArgumentNullException.ThrowIfNull(host);
        text ??= "";
        Dictionary<string, object?> map = mapping is not null
            ? new Dictionary<string, object?>(mapping, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        var you = fromObj ?? host;
        map.TryAdd("you", you);
        HashSet<GameObject>? excl = exclude is not null ? new HashSet<GameObject>(exclude) : null;
        string logContext = nodeSemantics ? "Node" : "GameObject";
        foreach (var receiver in receivers)
        {
            if (excl is not null && excl.Contains(receiver)) continue;
            string outMessage;
            try
            {
                outMessage = FuncParser.Parse(text, you, receiver, map, raiseErrors);
            }
            catch (Exception ex) when (ex is FuncParser.ParsingError || nodeSemantics)
            {
                if (raiseErrors && !nodeSemantics) throw;
                outMessage = text;
            }
            if (string.IsNullOrEmpty(outMessage) && nodeSemantics)
                receiver.Msg("", fromObj, null, false, msgType);
            else
            {
                try { receiver.Msg(outMessage, fromObj, null, false, msgType); }
                catch (Exception logEx) { AtherizLogger.LogDebug($"Suppressed {logContext}.MsgContents: " + logEx.Message, logContext); }
            }
        }
    }
}
