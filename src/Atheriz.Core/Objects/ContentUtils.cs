using Atheriz.Core.Objects;

namespace Atheriz.Core.Objects;

/// <summary>
/// Port of <c>atheriz/objects/contents.py</c>.
/// </summary>
public static class ContentUtils
{
    private static readonly HashSet<string> SingularWords = new(StringComparer.OrdinalIgnoreCase)
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
    };

    // Port of settings.MAX_SEARCH_DEPTH — mutable for testing (mirrors monkeypatch in test_contents_search.py:340)
    public static int MaxSearchDepth = 100;

    private static bool TermMatches(GameObject obj, string term)
    {
        if (term is null) return false;
        string termL;
        try { termL = term.ToLowerInvariant(); } catch { return false; }
        string nameL;
        try { nameL = (obj.Name ?? "").ToLowerInvariant(); } catch { nameL = ""; }
        if (termL == nameL || nameL.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(termL))
            return true;
        List<string> aliases;
        try { aliases = obj.Aliases; } catch { aliases = []; }
        foreach (var alias in aliases)
        {
            if (alias is null) continue;
            string aliasL;
            try { aliasL = alias.ToLowerInvariant(); } catch { continue; }
            if (termL == aliasL || aliasL.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(termL))
                return true;
        }
        return false;
    }

    public static List<GameObject> FilterVisible(List<GameObject> objs, GameObject? looker)
    {
        if (looker is null) return objs;
        return objs.Where(o => o != looker && o.Access(looker, "view")).ToList();
    }

    public static List<GameObject> FilterContents(GameObject obj, Func<GameObject, bool> predicate)
    {
        var contents = obj.ContentsSnapshot.Select(id => Globals.ObjectRegistry.Get(id).FirstOrDefault()).Where(o => o != null).Cast<GameObject>().ToList();
        return contents.Where(predicate).ToList();
    }

    public static string GroupByName(List<GameObject> objs, GameObject? looker = null)
    {
        if (objs.Count == 0) return "";
        var groups = new Dictionary<string,int>(StringComparer.Ordinal);
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
        var result = new List<GameObject>();
        var ids = root.ContentsSnapshot;
        foreach (var id in ids)
        {
            if (!visited.Add(id)) continue;
            var o = resolver(id);
            if (o is null) continue;
            if (looker is not null && !o.Access(looker, "view")) continue;
            result.Add(o);
            if (o.IsContainer)
            {
                try { result.AddRange(GatherContents(o, resolver, visited, depth + 1, looker)); }
                catch { break; }
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

        var objs = recursive ? GatherContents(obj, resolver, looker: looker) : obj.ContentsSnapshot.Select(resolver).Where(o => o != null).Cast<GameObject>().ToList();
        if (looker is not null)
            objs = objs.Where(o => o.Access(looker, "view")).ToList();

        if (q.StartsWith("#"))
        {
            if (!int.TryParse(q[1..], out var id)) return [];
            foreach (var o in objs) if (o.Id == id) return [o];
            return [];
        }

        var split = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (split.Count == 0) return [];
        var optional = new List<string>();
        var required = new List<string>();
        var count = 1;
        var index = 0;
        var start = 0;
        var end = split.Count;
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

        var matches = new List<GameObject>();
        for (var i = 0; i < objs.Count; i++)
        {
            bool found = false;
            foreach (var s in required)
            {
                if (TermMatches(objs[i], s)) found = true;
                else { found = false; break; }
            }
            if (found)
            {
                if (count == 1 && index == 0) return [objs[i]];
                if (!matches.Contains(objs[i])) matches.Add(objs[i]);
                if (matches.Count == count && index == 0) return matches;
                continue;
            }
            foreach (var s in optional)
            {
                if (TermMatches(objs[i], s))
                {
                    if (count == 1 && index == 0) return [objs[i]];
                    if (!matches.Contains(objs[i])) matches.Add(objs[i]);
                    if (matches.Count == count && index == 0) return matches;
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
}
