// Port of atheriz/commands/loggedin/give.py:189

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class GiveCommand : Command
{
    public override string Key => "give";
    public override string Desc => "Give an object to someone else.";
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("args", nargs: "*", help: "object to give, optionally 'to <target>'"); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        if (!this.RequireParsedArgs(caller, args, out var pa)) return;
        var loc = go.ResolveLocationObject();
        if (loc is null) { CommandHelpers.MsgNo(go); return; }
        // Single snapshot for the inventory checks below: no mutation occurs
        // before the first MoveTo, so one read covers all three sites.
        var inv = go.ContentsSnapshot;
        var tokens = pa.GetList("args");
        if (tokens.Count == 0) { go.Msg("Give it to whom?"); return; }
        string? objName = null, targetName = null;
        // Per-candidate search caches for the split loop below: every candidate
        // split is validated by real inventory + location searches, and the
        // winning pair is searched again when resolving obj/target afterwards —
        // the caches turn that re-search into a hit. Keyed separately per search
        // shape (inventory-only vs location); entries are only ever read, and
        // the loop performs no mutation between searches, so caching is sound.
        var invSearch = new Dictionary<string, List<GameObject>>(StringComparer.Ordinal);
        var locSearch = new Dictionary<string, List<GameObject>>(StringComparer.Ordinal);
        List<GameObject> InvSearch(string q)
        {
            if (!invSearch.TryGetValue(q, out var hit)) { hit = go.Search(q, true, go); invSearch[q] = hit; }
            return hit;
        }
        List<GameObject> LocSearch(string q)
        {
            if (!locSearch.TryGetValue(q, out var hit)) { hit = CommandHelpers.SearchIn(loc, q, go); locSearch[q] = hit; }
            return hit;
        }
        int toIdx = tokens.FindIndex(t => t.Equals("to", StringComparison.OrdinalIgnoreCase));
        if (toIdx >= 0)
        {
            var objParts = tokens.Take(toIdx).ToList();
            var tgtParts = tokens.Skip(toIdx + 1).ToList();
            if (objParts.Count == 0 || tgtParts.Count == 0) { go.Msg("Give it to whom?"); return; }
            objName = string.Join(" ", objParts);
            targetName = string.Join(" ", tgtParts);
        }
        else
        {
            if (tokens.Count < 2) { go.Msg("Give it to whom?"); return; }
            string? foundObj = null, foundTgt = null;
            for (int split = 1; split < tokens.Count; split++)
            {
                var candObj = string.Join(" ", tokens.Take(split));
                var candTgt = string.Join(" ", tokens.Skip(split));
                if (candObj.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    if (LocSearch(candTgt).Count > 0) { foundObj = candObj; foundTgt = candTgt; break; }
                    continue;
                }
                // Port of give.py:63 — caller.search is inventory-only.
                if (InvSearch(candObj).Count > 0)
                {
                    if (LocSearch(candTgt).Count > 0) { foundObj = candObj; foundTgt = candTgt; break; }
                }
            }
            if (foundObj is not null) { objName = foundObj; targetName = foundTgt; }
            else
            {
                var lastObj = string.Join(" ", tokens.Take(tokens.Count - 1));
                var lastTgt = tokens.Last();
                bool lastObjIsAll = lastObj.Equals("all", StringComparison.OrdinalIgnoreCase);
                bool lastObjInInv = CommandHelpers.SearchWithFallback(go, lastObj).Any(o => inv.Contains(o.Id));
                if (lastObjIsAll || lastObjInInv)
                { objName = lastObj; targetName = lastTgt; }
                else
                {
                    var firstObj = tokens[0];
                    var restTgt = string.Join(" ", tokens.Skip(1));
                    bool firstObjIsAll = firstObj.Equals("all", StringComparison.OrdinalIgnoreCase);
                    bool firstObjInInv = CommandHelpers.SearchWithFallback(go, firstObj).Any(o => inv.Contains(o.Id));
                    if (firstObjIsAll || firstObjInInv)
                    { objName = firstObj; targetName = restTgt; }
                    else { objName = lastObj; targetName = lastTgt; }
                }
            }
        }
        if (objName is null || targetName is null) { go.Msg("Give it to whom?"); return; }
        List<GameObject> tgtMatches = LocSearch(targetName);
        if (tgtMatches.Count == 0) { go.Msg($"Could not find '{targetName}' here."); return; }
        if (tgtMatches.Count > 1) { CommandHelpers.MsgMultipleMatchesFound(go, targetName); return; }
        var target = tgtMatches[0];
        if (target.Id == go.Id) { go.Msg("You already have that!"); return; }
        // No connectivity gate (give.py:142-157): search is view-filtered
        // only, so offline PCs that Python can give to stay reachable here.
        if (!target.IsContainer && !target.IsNpc && !target.IsPc) { go.Msg($"You can't give anything to {target.GetDisplayName(go)}."); return; }
        List<GameObject> objsToGive;
        if (objName == "all") objsToGive = ObjectRegistry.Get(inv);
        else
        {
            // Port of give.py:162 — caller.search is inventory-only: room
            // ground (or global #id) matches are NOT givable .
            // "You don't have that." is the verbatim refusal.
            objsToGive = InvSearch(objName);
            if (objsToGive.Count == 0) { go.Msg("You don't have that."); return; }
        }
        if (objsToGive.Count == 0) { go.Msg("You don't have that."); return; }
        bool givenAny = false;
        // Hoisted out of the loop: the announce path only reads the exclude
        // set, so one shared instance behaves like a fresh list per item.
        List<GameObject> giveExclude = [go, target];
        foreach (var obj in objsToGive)
        {
            // An item that IS the target cannot be moved into itself: report it
            // like the veto/move-fail paths instead of skipping silently.
            if (obj.Id == target.Id) { go.Msg($"You can't give {obj.Name} to itself."); continue; }
            // Port of give.py:172-177 — a veto reports, then skips the item.
            if (!obj.AtPreGive(go, target)) { go.Msg($"You can't give {obj.GetDisplayName(go)} to {target.GetDisplayName(go)}."); continue; }
            if (obj.MoveTo(target))
            {
                obj.AtGive(go, target);
                givenAny = true;
                go.Msg($"You give {obj.Name} to {target.Name}.");
                target.Msg($"{go.Name} gives you {obj.Name}.");
                ContentUtils.EmitToLocation(loc, $"{go.Name} gives {obj.Name} to {target.Name}.", fromObj: go, exclude: giveExclude);
            }
            else go.Msg($"You can't give {obj.Name} to {target.Name}.");
        }
        if (!givenAny && objName == "all") go.Msg("You have nothing to give.");
    }
}
