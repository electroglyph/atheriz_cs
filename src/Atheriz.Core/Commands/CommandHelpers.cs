// Port of atheriz/objects/contents.py:search + atheriz/commands/loggedin/delete.py:47-65 + ban.py helpers (dedup)
using System.Diagnostics.CodeAnalysis;

namespace Atheriz.Core.Commands;

public static class CommandHelpers
{
    /// <summary>
    /// Single shared implementation of the puppet guard repeated across every logged-in
    /// command. Returns true with <paramref name="puppet"/> set when <paramref name="caller"/>
    /// is a puppeted character; otherwise sends <paramref name="denyMessage"/> (unless null)
    /// and returns false.
    /// </summary>
    public static bool RequirePuppet(IMessageTarget caller, [NotNullWhen(true)] out GameObject? puppet, string? denyMessage = "You can't do that.")
    {
        if (caller is GameObject g) { puppet = g; return true; }
        puppet = null;
        if (!string.IsNullOrEmpty(denyMessage)) caller.Msg(denyMessage);
        return false;
    }
    /// <summary>
    /// Shared "local verbs" scan: external cmdsets from location contents, then
    /// inventory (mirrors inputfuncs.py loc.contents + puppet.contents order).
    /// Single home for the scan repeated in dispatch, help, and none-suggest.
    /// </summary>
    public static IEnumerable<CmdSet> LocalVerbSets(GameObject go)
    {
        var loc = go.ResolveLocationObject();
        if (loc is not null)
        {
            foreach (var id in loc.ContentsSnapshot)
            {
                var o = ObjectRegistry.Get(id).FirstOrDefault();
                if (o?.ExternalCmdSet is not null) yield return o.ExternalCmdSet;
            }
            // the location's OWN set too — dispatch consults it
            // (CommandDispatcher locObj.ExternalCmdSet check), so Help/None
            // suggestions must include it or they disagree with dispatch.
            // Yielded last to preserve dispatch precedence (contents first).
            if (loc.ExternalCmdSet is not null) yield return loc.ExternalCmdSet;
        }
        foreach (var id in go.ContentsSnapshot)
        {
            var o = ObjectRegistry.Get(id).FirstOrDefault();
            if (o?.ExternalCmdSet is not null) yield return o.ExternalCmdSet;
        }
    }
    /// <summary>
    /// Port of <c>atheriz/objects/contents.py:search</c> fallback + <c>delete.py:47-65</c> coord handling.
    /// Handles #id (global), "me", "here", coord "(area,x,y,z)", then caller search + loc fallback if view allowed.
    /// </summary>
    public static List<GameObject> SearchWithFallback(GameObject caller, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];
        string raw = name.Trim();
        // Handle parenthesized coord like "(area,0,0,0)" -> strip parens for detection
        string inner = raw;
        if (inner.StartsWith("(", StringComparison.Ordinal) && inner.EndsWith(")", StringComparison.Ordinal)) inner = inner[1..^1];
        // Coord detection: 4 parts with last 3 ints
        if (inner.Contains(","))
        {
            var parts = inner.Split(',').Select(p => p.Trim()).ToList();
            if (parts is [var areaName, var xs, var ys, var zs] && int.TryParse(xs, out var x) && int.TryParse(ys, out var y) && int.TryParse(zs, out var z))
            {
                var coord = new Coord(areaName, x, y, z);
                var node = ObjectRegistry.FilterBy(o => o is Node n && n.Coord.Equals(coord)).FirstOrDefault() as GameObject;
                if (node is not null) return [node];
                return [];
            }
        }
        if (raw.Equals("me", StringComparison.OrdinalIgnoreCase)) return [caller];
        if (raw.Equals("here", StringComparison.OrdinalIgnoreCase))
        {
            var locHere = caller.ResolveLocationObject();
            return locHere is not null ? [locHere] : [];
        }
        if (raw.StartsWith("#", StringComparison.Ordinal))
        {
            if (!int.TryParse(raw[1..], out var id)) return [];
            var objs = ObjectRegistry.Get(id);
            if (objs.Count == 0) return [];
            return [objs[0]];
        }
        // Standard search via caller + loc fallback — use virtual Search so mocks work (mirrors python caller.search)
        var matches = caller.Search(raw, true, caller);
        if (matches.Count == 0)
        {
            var loc = caller.ResolveLocationObject();
            if (loc is not null && loc.Access(caller, "view"))
            {
                if (loc is Node node) matches = node.Search(raw, true, caller);
                else matches = ContentUtils.Search(loc, raw, id => ObjectRegistry.Get(id).FirstOrDefault(), true, caller);
            }
        }
        return matches;
    }

    /// <summary>
    /// Helper for searching within a specific container (loc). Port of loc.search fallback without caller search.
    /// </summary>
    public static List<GameObject> SearchIn(GameObject container, string query, GameObject? looker = null)
    {
        if (container is Node n) return n.Search(query, true, looker);
        return ContentUtils.Search(container, query, id => ObjectRegistry.Get(id).FirstOrDefault(), true, looker);
    }

    /// <summary>
    /// Port of helper <c>ResolveTarget</c> pattern across Exam/Delete/Follow/Puppet/Give/Set.
    /// Returns single filtered object or null with messaging. Filter optional (e.g., IsPc).
    /// </summary>
    public static GameObject? ResolveObject(GameObject caller, string query, Func<GameObject, bool>? filter = null)
    {
        var list = SearchWithFallback(caller, query);
        if (filter is not null) list = list.Where(filter).ToList();
        if (list.Count == 0)
        {
            if (query.StartsWith("#", StringComparison.Ordinal))
            {
                if (!int.TryParse(query[1..], out var id))
                {
                    caller.Msg("Invalid ID format. Use #<number>.");
                    return null;
                }
                // Distinguish between global not found vs filtered not found: use generic message
                caller.Msg($"No object found with ID {id}.");
                return null;
            }
            MsgNoMatchFound(caller, query);
            return null;
        }
        if (list.Count > 1)
        {
            MsgMultipleMatchesColon(caller, query);
            foreach (var m in list) caller.Msg($"  #{m.Id} {m.Name}");
            return null;
        }
        return list[0];
    }

    // ----- Centralized message dialects -----
    // The Python originals spell these differently per command; behavior is
    // preserved exactly — one home for the literals, no unification.
    public static void MsgNo(IMessageTarget go) => go.Msg("No.");
    public static void MsgNowhere(IMessageTarget go) => go.Msg("You are nowhere.");
    public static void MsgNowhereExclaim(IMessageTarget go) => go.Msg("You are nowhere!");
    public static void MsgInvalidLocation(IMessageTarget go) => go.Msg("You have an invalid location.");
    public static void MsgObjectNotFound(IMessageTarget go) => go.Msg("Object not found.");
    public static string FormatNoMatchFound(string name) => $"No match found for '{name}'.";
    public static void MsgNoMatchFound(IMessageTarget go, string name) => go.Msg(FormatNoMatchFound(name));
    public static void MsgMultipleMatches(IMessageTarget go, string name) => go.Msg($"Multiple matches for '{name}'.");
    public static void MsgMultipleMatchesColon(IMessageTarget go, string name) => go.Msg($"Multiple matches for '{name}':");
    public static void MsgMultipleMatchesFound(IMessageTarget go, string name) => go.Msg($"Multiple matches found for '{name}'.");
    public static string FormatMultipleMatchesIdList(IEnumerable<GameObject> matches)
        => $"Multiple matches: {string.Join(", ", matches.Select(m => $"#{m.Id} {m.Name}"))}. Use #id to pick one.";
}
