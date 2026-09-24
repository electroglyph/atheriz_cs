namespace Atheriz.Core.Commands.LoggedIn;

public sealed class DropCommand : LoggedInCommand
{
    public override string Key => "drop";
    public override string Desc => "Drop an object.";
    protected override bool AllowMissingArgs => true;
    protected override void SetupParser(GameArgumentParser parser) { parser.AddArgument("object", nargs: "REMAINDER", help: "object to drop or all"); }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        // Extract drop name robustly
        string? dropName = null;
        // The parser defines a single positional dest ("object"); live input
        // never carries other keys, so no fallback dests are read here.
        var lst = pa.GetList("object");
        if (lst.Count > 0) dropName = string.Join(" ", lst).Trim();
        if (string.IsNullOrWhiteSpace(dropName)) { go.Msg(PrintHelp()); return; }
        var loc = go.ResolveLocationObject();
        if (loc is null) { go.Msg("You can't drop something here!"); return; }
        // Intentional: Drop checks the "put" lock, not "drop" — verbatim atheriz/commands/loggedin/drop.py:26.
        if (!loc.Access(go, "put")) { go.Msg("You can't drop something here!"); return; }
        dropName = dropName!.Trim();
        List<GameObject> excludeSelf = [go];
        if (dropName == "all")
        {
            var contents = ObjectRegistry.Get(go.ContentsSnapshot);
            foreach (var obj in contents)
            {
                if (!obj.AtPreDrop(go)) continue;
                if (!obj.MoveTo(loc)) { go.Msg($"You can't drop {obj.Name}."); continue; }
                ContentUtils.EmitToLocation(loc, $"{go.Name} dropped {obj.Name}.", fromObj: go, exclude: excludeSelf);
                go.Msg($"You dropped: {obj.Name}");
                obj.AtDrop(go);
            }
            return;
        }
        var found = go.Search(dropName, true, go);
        if (found.Count == 0) { CommandHelpers.MsgObjectNotFound(go); return; }
        foreach (var f in found)
        {
            if (!f.AtPreDrop(go)) continue;
            if (!f.MoveTo(loc)) { go.Msg($"You can't drop {f.Name}."); continue; }
            ContentUtils.EmitToLocation(loc, $"{go.Name} dropped {f.Name}.", fromObj: go, exclude: excludeSelf);
            go.Msg($"You dropped: {f.Name}");
            f.AtDrop(go);
        }
    }
}

public sealed class GetCommand : LoggedInCommand
{
    public override string Key => "get";
    public override string Desc => "Get an object.";
    protected override void SetupParser(GameArgumentParser parser) { parser.AddArgument(ParsedArgKeys.Target, nargs: "*", help: "object to get, optionally 'from <container>'"); }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var loc = go.ResolveLocationObject();
        if (loc is null) { CommandHelpers.MsgNo(go); return; }
        string? objName = null, sourceName = null;
        // The parser defines a single positional dest (ParsedArgKeys.Target);
        // live input never carries other keys, so no fallback dests are read here.
        var tokens = pa.GetList(ParsedArgKeys.Target);
        if (tokens.Count == 0) { go.Msg(PrintHelp()); return; }
        if (TargetResolution.SplitOnFirstKeyword(tokens, out var objParts, out var srcParts, "from"))
        {
            if (objParts.Count == 0) { go.Msg(PrintHelp()); return; }
            objName = string.Join(" ", objParts);
            sourceName = srcParts.Count > 0 ? string.Join(" ", srcParts) : null;
        }
        else
        {
            objName = string.Join(" ", tokens);
            sourceName = null;
        }
        if (string.IsNullOrWhiteSpace(objName)) { go.Msg(PrintHelp()); return; }
        // Handle "all" case (case-insensitive: the search keyword is
        // lowercased, so `ALL` must bulk here instead of falling through
        // to the single-object path with different messages).
        if (objName.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            GameObject source;
            if (sourceName is not null)
            {
                var cont = CommandHelpers.SearchWithFallback(go, sourceName);
                if (cont.Count == 0) { go.Msg($"'{sourceName}' not found."); return; }
                source = cont[0];
                if (!source.Access(go, "get")) { go.Msg("You can't take anything from there."); return; }
            }
            else
            {
                if (!loc.Access(go, "get")) { go.Msg("You can't get something from here!"); return; }
                source = loc;
            }
            List<GameObject> srcContents = source is Node ns ? ns.GetContents() : ObjectRegistry.Get(source.ContentsSnapshot);
            // Hoisted out of the loop: the announce path only reads the
            // exclude set (it builds its own per-receiver state), so one
            // shared instance behaves the same as a fresh list per item.
            List<GameObject> excludeSelf = [go];
            foreach (var obj in srcContents)
            {
                // Parity with the named path (view-filtered search): a hidden
                // item must not be swept up by `get all` (get.py:112-117).
                if (!obj.AtPreGet(go) || obj.Id == go.Id || !obj.Access(go, "view")) continue;
                if (!obj.MoveTo(go)) { go.Msg($"You can't get {obj.GetDisplayName(go)}."); continue; }
                var takeMapping = new Dictionary<string, object?> { ["giver"] = go, ["item"] = obj };
                ContentUtils.EmitToLocation(loc, "$You(giver) $conj(take) $obj(item).", fromObj: go, mapping: takeMapping, exclude: excludeSelf, msgType: "get");
                go.Msg($"You picked up: {obj.GetDisplayName(go)}");
                obj.AtGet(go);
            }
            return;
        }
        // Single object case
        if (sourceName is not null)
        {
            var cont = CommandHelpers.SearchWithFallback(go, sourceName);
            if (cont.Count == 0) { go.Msg($"'{sourceName}' not found."); return; }
            var source = cont[0];
            if (!source.Access(go, "get")) { go.Msg("You can't take anything from there."); return; }
            var found = CommandHelpers.SearchIn(source, objName, go);
            if (found.Count == 0) { go.Msg($"'{objName}' not found in {source.Name}."); return; }
            List<GameObject> excludeSelf = [go];
            foreach (var f in found)
            {
                if (!f.AtPreGet(go)) { go.Msg($"You can't get {f.Name}."); continue; }
                if (!f.MoveTo(go)) { go.Msg($"You can't get {f.Name}."); continue; }
                ContentUtils.EmitToLocation(loc, $"{go.Name} picked up {f.Name}.", fromObj: go, exclude: excludeSelf);
                go.Msg($"You picked up: {f.Name}");
                f.AtGet(go);
            }
        }
        else
        {
            if (!loc.Access(go, "get")) { go.Msg("You can't get something from here!"); return; }
            var found = CommandHelpers.SearchIn(loc, objName, go);
            if (found.Count == 0) { CommandHelpers.MsgObjectNotFound(go); return; }
            List<GameObject> excludeSelf = [go];
            foreach (var f in found)
            {
                if (!f.AtPreGet(go)) { go.Msg($"You can't get {f.Name}."); continue; }
                if (!f.MoveTo(go)) { go.Msg($"You can't get {f.Name}."); continue; }
                ContentUtils.EmitToLocation(loc, $"{go.Name} picked up {f.Name}.", fromObj: go, exclude: excludeSelf);
                go.Msg($"You picked up: {f.Name}");
                f.AtGet(go);
            }
        }
    }
}

public sealed class GiveCommand : LoggedInCommand
{
    public override string Key => "give";
    public override string Desc => "Give an object to someone else.";
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument(ParsedArgKeys.Args, nargs: "*", help: "object to give, optionally 'to <target>'"); }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var loc = go.ResolveLocationObject();
        if (loc is null) { CommandHelpers.MsgNo(go); return; }
        // Single snapshot for the inventory checks below: no mutation occurs
        // before the first MoveTo, so one read covers all three sites.
        var inv = go.ContentsSnapshot;
        var tokens = pa.GetList(ParsedArgKeys.Args);
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
        if (TargetResolution.SplitOnFirstKeyword(tokens, out var objParts, out var tgtParts, "to"))
        {
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
// caller.search is inventory-only.
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
        if (objName.Equals("all", StringComparison.OrdinalIgnoreCase)) objsToGive = ObjectRegistry.Get(inv);
        else
        {
// caller.search is inventory-only: room
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
// a veto reports, then skips the item.
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
        if (!givenAny && objName.Equals("all", StringComparison.OrdinalIgnoreCase)) go.Msg("You have nothing to give.");
    }
}

public sealed class InventoryCommand : LoggedInCommand
{
    public override string Key => "inventory";
    public override IReadOnlyList<string> Aliases => ["i"];
    public override string Desc => "View your inventory.";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject p, string raw, CancellationToken ct)
    {
        var contents = Globals.ObjectRegistry.Get(p.ContentsSnapshot);
        if (contents.Count == 0) p.Msg("You are carrying nothing.");
        else
        {
            var names = ContentUtils.GroupByName(contents, p);
            p.Msg($"You are carrying: {names}");
        }
    }
}

public sealed class PutCommand : LoggedInCommand
{
    public override string Key => "put";
    public override string Desc => "Put an object somewhere.";
    protected override bool AllowMissingArgs => true;
    protected override string? DenyMessage => PrintHelp();
    protected override void SetupParser(GameArgumentParser parser) { parser.AddArgument(ParsedArgKeys.Args, nargs: "REMAINDER"); }
    protected override void RunPuppet(GameObject goCaller, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        List<GameObject> excludeSelf = [goCaller];
        string? objName = null;
        string? destName = null;
// live ParsedArgs carry the `args` list.
        var tokens = pa.GetList(ParsedArgKeys.Args);
        if (tokens.Count > 0)
        {
            if (!TargetResolution.SplitOnFirstKeyword(tokens, out var objParts, out var destParts, "in", "into")) { goCaller.Msg(PrintHelp()); return; }
            if (objParts.Count == 0 || destParts.Count == 0) { goCaller.Msg(PrintHelp()); return; }
            objName = string.Join(" ", objParts);
            destName = string.Join(" ", destParts);
        }
        if (string.IsNullOrWhiteSpace(objName) || string.IsNullOrWhiteSpace(destName)) { goCaller.Msg(PrintHelp()); return; }
        GameObject? loc = null;
        try { loc = goCaller.ResolveLocationObject(); } catch (Exception) { }
        List<GameObject> destList = new();
        try { destList = goCaller.Search(destName!, true, goCaller); } catch (Exception) { }
        if (destList.Count==0 && loc is not null)
        {
            try { if (loc.Access(goCaller, "put")) destList = loc.Search(destName!, true, goCaller); } catch (Exception) { }
        }
        if (destList.Count==0) { goCaller.Msg($"'{destName}' not found."); return; }
        var destObj = destList[0];
        if (!destObj.IsContainer || !destObj.Access(goCaller, "put")) { goCaller.Msg($"You can't put anything in {destObj.Name}!"); return; }
        bool IsLoop(GameObject obj, GameObject destC)
        {
            var cur = destC;
            HashSet<int> seen = [];
            while (cur is not null && !cur.IsNode)
            {
                if (ReferenceEquals(cur, obj) || cur.Id == obj.Id) return true;
                if (!seen.Add(cur.Id)) return true;
                var nxt = cur.ResolveLocationObject();
                if (nxt is null || nxt.IsNode) break;
                cur = nxt;
            }
            return false;
        }
        if (objName == "all")
        {
            var contents = goCaller.ContentsSnapshot.Select(ObjectRegistry.GetSingle).OfType<GameObject>().ToList();
            foreach (var obj in contents)
            {
                if (obj.Id == destObj.Id) { goCaller.Msg($"You can't put {obj.Name} in {destObj.Name} - it would create a containment loop."); continue; }
                if (IsLoop(obj, destObj)) { goCaller.Msg($"You can't put {obj.Name} in {destObj.Name} - it would create a containment loop."); continue; }
                if (!obj.AtPrePut(goCaller, destObj)) continue;
                if (!obj.MoveTo(destObj)) { goCaller.Msg($"You can't put {obj.Name} in {destObj.Name}."); continue; }
                if (loc is not null)
                {
                    try { loc.MsgContents($"{goCaller.Name} put {obj.Name} in {destObj.Name}.", fromObj: goCaller, mapping: null, exclude: excludeSelf); } catch (Exception) { }
                }
                goCaller.Msg($"You put {obj.Name} in {destObj.Name}.");
                obj.AtPut(goCaller, destObj);
            }
            return;
        }
        List<GameObject> foundObjs = new();
        try { foundObjs = goCaller.Search(objName, true, goCaller); } catch (Exception) { }
        if (foundObjs.Count==0) { CommandHelpers.MsgObjectNotFound(goCaller); return; }
        foreach (var obj in foundObjs)
        {
            if (obj.Id == destObj.Id) { goCaller.Msg($"You can't put {obj.Name} in {destObj.Name} - it would create a containment loop."); continue; }
            if (IsLoop(obj, destObj)) { goCaller.Msg($"You can't put {obj.Name} in {destObj.Name} - it would create a containment loop."); continue; }
            if (!obj.AtPrePut(goCaller, destObj)) { goCaller.Msg($"You can't put {obj.Name} in {destObj.Name}."); continue; }
            if (!obj.MoveTo(destObj)) { goCaller.Msg($"You can't put {obj.Name} in {destObj.Name}."); continue; }
            if (loc is not null)
            {
                try { loc.MsgContents($"{goCaller.Name} put {obj.Name} in {destObj.Name}.", fromObj: goCaller, exclude: excludeSelf); } catch (Exception) { }
            }
            goCaller.Msg($"You put {obj.Name} in {destObj.Name}.");
            obj.AtPut(goCaller, destObj);
        }
    }
}
