// Port of atheriz/commands/loggedin/get.py:87 + put.py:174 + drop.py:66
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class GetCommand : Command
{
    // Port of atheriz/commands/loggedin/get.py:10
    public override string Key => "get";
    public override string Desc => "Get an object.";
    protected override void SetupParser(GameArgumentParser parser) { parser.AddArgument("target", nargs: "*", help: "object to get, optionally 'from <container>'"); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { go.Msg(PrintHelp()); return; }
        var loc = go.ResolveLocationObject();
        if (loc == null) { CommandHelpers.MsgNo(go); return; }
        string? objName = null, sourceName = null;
        // The parser defines a single positional dest ("target"); live input
        // never carries other keys, so no fallback dests are read here.
        var tokens = pa.GetList("target");
        if (tokens.Count == 0) { go.Msg(PrintHelp()); return; }
        int fromIdx = -1;
        for (int i=0;i<tokens.Count;i++) if (tokens[i].Equals("from", StringComparison.OrdinalIgnoreCase)) { fromIdx=i; break; }
        if (fromIdx >= 0)
        {
            var objParts = tokens.Take(fromIdx).ToList();
            var srcParts = tokens.Skip(fromIdx+1).ToList();
            if (objParts.Count==0) { go.Msg(PrintHelp()); return; }
            objName = string.Join(" ", objParts);
            sourceName = srcParts.Count>0 ? string.Join(" ", srcParts) : null;
        }
        else
        {
            objName = string.Join(" ", tokens);
            sourceName = null;
        }
        if (string.IsNullOrWhiteSpace(objName)) { go.Msg(PrintHelp()); return; }
        // Handle "all" case
        if (objName == "all")
        {
            GameObject source;
            if (sourceName != null)
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
            List<GameObject> srcContents = source is Node ns ? ns.GetContents() : ObjectRegistry.Get(source.ContentsSnapshot.ToList());
            foreach (var obj in srcContents.ToList())
            {
                // Parity with the named path (view-filtered search): a hidden
                // item must not be swept up by `get all` (get.py:112-117).
                if (!obj.AtPreGet(go) || obj.Id == go.Id || !obj.Access(go, "view")) continue;
                if (!obj.MoveTo(go)) { go.Msg($"You can't get {obj.GetDisplayName(go)}."); continue; }
                var takeMapping = new Dictionary<string, object?> { ["giver"] = go, ["item"] = obj };
                if (loc is Node ln) ln.MsgContents("$You(giver) $conj(take) $obj(item).", fromObj: go, mapping: takeMapping, exclude: new List<GameObject> { go }, msgType: "get");
                else loc.MsgContents("$You(giver) $conj(take) $obj(item).", fromObj: go, mapping: takeMapping, exclude: new List<GameObject> { go }, msgType: "get");
                go.Msg($"You picked up: {obj.GetDisplayName(go)}");
                obj.AtGet(go);
            }
            return;
        }
        // Single object case
        if (sourceName != null)
        {
            var cont = CommandHelpers.SearchWithFallback(go, sourceName);
            if (cont.Count == 0) { go.Msg($"'{sourceName}' not found."); return; }
            var source = cont[0];
            if (!source.Access(go, "get")) { go.Msg("You can't take anything from there."); return; }
            var found = CommandHelpers.SearchIn(source, objName, go);
            if (found.Count == 0) { go.Msg($"'{objName}' not found in {source.Name}."); return; }
            foreach (var f in found)
            {
                if (!f.AtPreGet(go)) { go.Msg($"You can't get {f.Name}."); continue; }
                if (!f.MoveTo(go)) { go.Msg($"You can't get {f.Name}."); continue; }
                if (loc is Node ln) ln.MsgContents($"{go.Name} picked up {f.Name}.", exclude: new List<GameObject>{go}, fromObj: go);
                else loc.MsgContents($"{go.Name} picked up {f.Name}.", exclude: new List<GameObject>{go}, fromObj: go);
                go.Msg($"You picked up: {f.Name}");
                f.AtGet(go);
            }
        }
        else
        {
            if (!loc.Access(go, "get")) { go.Msg("You can't get something from here!"); return; }
            var found = CommandHelpers.SearchIn(loc, objName, go);
            if (found.Count == 0) { CommandHelpers.MsgObjectNotFound(go); return; }
            foreach (var f in found)
            {
                if (!f.AtPreGet(go)) { go.Msg($"You can't get {f.Name}."); continue; }
                if (!f.MoveTo(go)) { go.Msg($"You can't get {f.Name}."); continue; }
                if (loc is Node ln) ln.MsgContents($"{go.Name} picked up {f.Name}.", exclude: new List<GameObject>{go}, fromObj: go);
                else loc.MsgContents($"{go.Name} picked up {f.Name}.", exclude: new List<GameObject>{go}, fromObj: go);
                go.Msg($"You picked up: {f.Name}");
                f.AtGet(go);
            }
        }
    }
}
public sealed class PutCommand : Command
{
    // Port of atheriz/commands/loggedin/put.py:10
    public override string Key => "put";
    public override string Desc => "Put an object somewhere.";
    protected override void SetupParser(GameArgumentParser parser) { parser.AddArgument("args", nargs: "REMAINDER"); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var goCaller, PrintHelp())) return;
        string? objName = null;
        string? destName = null;
        // Port of put.py:25-42 — live ParsedArgs carry the `args` list.
        if (args is GameArgumentParser.ParsedArgs pa)
        {
            var tokens = pa.GetList("args");
            if (tokens.Count > 0)
            {
                int split = tokens.FindIndex(s => s.Equals("in", StringComparison.OrdinalIgnoreCase) || s.Equals("into", StringComparison.OrdinalIgnoreCase));
                if (split < 0) { caller.Msg(PrintHelp()); return; }
                var objParts = tokens.Take(split).ToList();
                var destParts = tokens.Skip(split + 1).ToList();
                if (objParts.Count == 0 || destParts.Count == 0) { caller.Msg(PrintHelp()); return; }
                objName = string.Join(" ", objParts);
                destName = string.Join(" ", destParts);
            }
        }
        if (string.IsNullOrWhiteSpace(objName) || string.IsNullOrWhiteSpace(destName)) { caller.Msg(PrintHelp()); return; }
        GameObject? loc = null;
        try { loc = goCaller.ResolveLocationObject(); } catch (Exception) { }
        List<GameObject> destList = new();
        try { destList = goCaller.Search(destName!, true, goCaller); } catch (Exception) { }
        if (destList.Count==0 && loc != null)
        {
            try { if (loc.Access(goCaller, "put")) destList = loc.Search(destName!, true, goCaller); } catch (Exception) { }
        }
        if (destList.Count==0) { caller.Msg($"'{destName}' not found."); return; }
        var destObj = destList[0];
        if (!destObj.IsContainer || !destObj.Access(goCaller, "put")) { caller.Msg($"You can't put anything in {destObj.Name}!"); return; }
        bool IsLoop(GameObject obj, GameObject destC)
        {
            var cur = destC;
            var seen = new HashSet<int>();
            while (cur != null && !cur.IsNode)
            {
                if (ReferenceEquals(cur, obj) || cur.Id == obj.Id) return true;
                if (!seen.Add(cur.Id)) return true;
                var nxt = cur.ResolveLocationObject();
                if (nxt == null || nxt.IsNode) break;
                cur = nxt;
            }
            return false;
        }
        if (objName == "all")
        {
            var contents = goCaller.ContentsSnapshot.Select(id => ObjectRegistry.Get(id).FirstOrDefault()).Where(o=>o!=null).Cast<GameObject>().ToList();
            foreach (var obj in contents.ToList())
            {
                if (obj.Id == destObj.Id) { caller.Msg($"You can't put {obj.Name} in {destObj.Name} - it would create a containment loop."); continue; }
                if (IsLoop(obj, destObj)) { caller.Msg($"You can't put {obj.Name} in {destObj.Name} - it would create a containment loop."); continue; }
                if (!obj.AtPrePut(goCaller, destObj)) continue;
                if (!obj.MoveTo(destObj)) { caller.Msg($"You can't put {obj.Name} in {destObj.Name}."); continue; }
                if (loc != null)
                {
                    try { loc.MsgContents($"{goCaller.Name} put {obj.Name} in {destObj.Name}.", fromObj: goCaller, mapping: null, exclude: new List<GameObject>{goCaller}); } catch (Exception) { }
                }
                caller.Msg($"You put {obj.Name} in {destObj.Name}.");
                obj.AtPut(goCaller, destObj);
            }
            return;
        }
        List<GameObject> foundObjs = new();
        try { foundObjs = goCaller.Search(objName, true, goCaller); } catch (Exception) { }
        if (foundObjs.Count==0) { CommandHelpers.MsgObjectNotFound(caller); return; }
        foreach (var obj in foundObjs)
        {
            if (obj.Id == destObj.Id) { caller.Msg($"You can't put {obj.Name} in {destObj.Name} - it would create a containment loop."); continue; }
            if (IsLoop(obj, destObj)) { caller.Msg($"You can't put {obj.Name} in {destObj.Name} - it would create a containment loop."); continue; }
            if (!obj.AtPrePut(goCaller, destObj)) { caller.Msg($"You can't put {obj.Name} in {destObj.Name}."); continue; }
            if (!obj.MoveTo(destObj)) { caller.Msg($"You can't put {obj.Name} in {destObj.Name}."); continue; }
            if (loc != null)
            {
                try { loc.MsgContents($"{goCaller.Name} put {obj.Name} in {destObj.Name}.", fromObj: goCaller, exclude: new List<GameObject>{goCaller}); } catch (Exception) { }
            }
            caller.Msg($"You put {obj.Name} in {destObj.Name}.");
            obj.AtPut(goCaller, destObj);
        }
    }
}
public sealed class DropCommand : Command
{
    // Port of atheriz/commands/loggedin/drop.py:10
    public override string Key => "drop";
    public override string Desc => "Drop an object.";
    protected override void SetupParser(GameArgumentParser parser) { parser.AddArgument("object", nargs: "REMAINDER", help: "object to drop or all"); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        // Extract drop name robustly
        string? dropName = null;
        if (pa != null)
        {
        // The parser defines a single positional dest ("object"); live input
        // never carries other keys, so no fallback dests are read here.
        var lst = pa.GetList("object");
        if (lst.Count > 0) dropName = string.Join(" ", lst).Trim();
        }
        if (string.IsNullOrWhiteSpace(dropName)) { go.Msg(PrintHelp()); return; }
        var loc = go.ResolveLocationObject();
        if (loc == null) { go.Msg("You can't drop something here!"); return; }
        // Intentional: Drop checks the "put" lock, not "drop" — verbatim atheriz/commands/loggedin/drop.py:26.
        if (!loc.Access(go, "put")) { go.Msg("You can't drop something here!"); return; }
        dropName = dropName!.Trim();
        if (dropName == "all")
        {
            var contents = ObjectRegistry.Get(go.ContentsSnapshot.ToList()).ToList();
            foreach (var obj in contents.ToList())
            {
                if (!obj.AtPreDrop(go)) continue;
                if (!obj.MoveTo(loc)) { go.Msg($"You can't drop {obj.Name}."); continue; }
                if (loc is Node ln) ln.MsgContents($"{go.Name} dropped {obj.Name}.", exclude: new List<GameObject>{go}, fromObj: go);
                else loc.MsgContents($"{go.Name} dropped {obj.Name}.", exclude: new List<GameObject>{go}, fromObj: go);
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
            if (loc is Node ln) ln.MsgContents($"{go.Name} dropped {f.Name}.", exclude: new List<GameObject>{go}, fromObj: go);
            else loc.MsgContents($"{go.Name} dropped {f.Name}.", exclude: new List<GameObject>{go}, fromObj: go);
            go.Msg($"You dropped: {f.Name}");
            f.AtDrop(go);
        }
    }
}
