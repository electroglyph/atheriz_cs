
namespace Atheriz.Core.Commands;

// Shared target parsing behind the verbs: the `#id` fetch every verb spells
// identically (parse, fetch, two messages) plus the first-keyword partition
// behind give `to` / get `from` / put `in|into`. Per-verb gates stay at the
// call sites — ban's IsPc wording must not leak into puppet errors and vice
// versa — so only the truly shared shape lives here.
internal static class TargetResolution
{
    // Parses `#id` and fetches the object. The two messages are the ones
    // BanHelper.ResolveTarget and PuppetCommand.FindTarget already shared;
    // anything stricter (ban's IsPc, puppet's deleted gate) is the caller's.
    internal static (GameObject? Target, string? Error) ResolveById(GameObject caller, string query)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(query);
        if (!CommandHelpers.TryParseIdRef(query, out var id))
            return (null, "Invalid ID format. Use #<number>.");
        var found = ObjectRegistry.GetSingle(id);
        if (found is null)
            return (null, $"No object found with ID {id}.");
        return (found, null);
    }

    // Shared `#id`-or-name resolution behind BanHelper.ResolveTarget and
    // PuppetCommand.FindTarget. `#id` goes through ResolveById (its error is
    // returned verbatim); anything else goes through the same
    // SearchWithFallback path the puppet verb uses today, then the caller's
    // filter (a filtered-out match reports notFound, never a match list).
    // The multi-match message has two shapes: ban prints a header line plus
    // one `  #id name` line per match, puppet prints a single-line id list.
    // A null multiHeader selects the single-line shape; a set header selects
    // the header-plus-lines shape with `\n` separators (the caller sends one
    // message per line to keep the historical per-line shape).
    // The world-scope union below exists for ban: the gated local search
    // cannot see unplaced or offline player characters that ban resolves
    // today through its global exact-name search, so when a filter is given
    // the global exact-name index is unioned in first (registry order, then
    // local-only extras). Ban's filter also repeats the exact-name check, so
    // the union lands on exactly its old global result in the same order.
    // Unfiltered callers (puppet) keep the pure local search, unchanged.
    internal static (GameObject? Target, string? Error) ResolveObject(
        GameObject caller,
        string query,
        Func<GameObject, bool>? filter,
        string notFound,
        string? multiHeader = null)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(notFound);
        if (query.StartsWith("#", StringComparison.Ordinal))
        {
            var (found, err) = ResolveById(caller, query);
            if (err is not null) return (null, err);
            if (filter is not null && found is not null && !filter(found))
                return (null, notFound);
            return (found, null);
        }
        var matches = CommandHelpers.SearchWithFallback(caller, query);
        if (filter is not null)
        {
            var seen = new HashSet<int>();
            var union = new List<GameObject>();
            foreach (var o in ObjectRegistry.FilterBy(o => o.Name.Equals(query, StringComparison.OrdinalIgnoreCase)))
                if (seen.Add(o.Id)) union.Add(o);
            foreach (var m in matches)
                if (seen.Add(m.Id)) union.Add(m);
            matches = union.Where(filter).ToList();
        }
        if (matches.Count == 0) return (null, notFound);
        if (matches.Count > 1)
            return (null, multiHeader is not null
                ? multiHeader + string.Concat(matches.Select(m => $"\n  #{m.Id} {m.Name}"))
                : CommandHelpers.FormatMultipleMatchesIdList(matches));
        return (matches[0], null);
    }

    // Splits tokens at the first keyword match (case-insensitive, like each
    // verb's old FindIndex/loop did): `give a to b` -> ([a], [b]). False when
    // no token matches; emptiness checks stay with the callers because each
    // verb reports them differently ("Give it to whom?" vs PrintHelp).
    internal static bool SplitOnFirstKeyword(IReadOnlyList<string> tokens, out List<string> before, out List<string> after, params string[] keywords)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(keywords);
        before = [];
        after = [];
        for (int i = 0; i < tokens.Count; i++)
        {
            foreach (var k in keywords)
            {
                if (tokens[i].Equals(k, StringComparison.OrdinalIgnoreCase))
                {
                    before = tokens.Take(i).ToList();
                    after = tokens.Skip(i + 1).ToList();
                    return true;
                }
            }
        }
        return false;
    }
}
