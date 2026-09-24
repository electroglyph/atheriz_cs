
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class PutCommand : Command
{
    public override string Key => "put";
    public override string Desc => "Put an object somewhere.";
    protected override void SetupParser(GameArgumentParser parser) { parser.AddArgument(ParsedArgKeys.Args, nargs: "REMAINDER"); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var goCaller, PrintHelp())) return;
        List<GameObject> excludeSelf = [goCaller];
        string? objName = null;
        string? destName = null;
// live ParsedArgs carry the `args` list.
        if (args is GameArgumentParser.ParsedArgs pa)
        {
            var tokens = pa.GetList(ParsedArgKeys.Args);
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
        if (destList.Count==0 && loc is not null)
        {
            try { if (loc.Access(goCaller, "put")) destList = loc.Search(destName!, true, goCaller); } catch (Exception) { }
        }
        if (destList.Count==0) { caller.Msg($"'{destName}' not found."); return; }
        var destObj = destList[0];
        if (!destObj.IsContainer || !destObj.Access(goCaller, "put")) { caller.Msg($"You can't put anything in {destObj.Name}!"); return; }
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
                if (obj.Id == destObj.Id) { caller.Msg($"You can't put {obj.Name} in {destObj.Name} - it would create a containment loop."); continue; }
                if (IsLoop(obj, destObj)) { caller.Msg($"You can't put {obj.Name} in {destObj.Name} - it would create a containment loop."); continue; }
                if (!obj.AtPrePut(goCaller, destObj)) continue;
                if (!obj.MoveTo(destObj)) { caller.Msg($"You can't put {obj.Name} in {destObj.Name}."); continue; }
                if (loc is not null)
                {
                    try { loc.MsgContents($"{goCaller.Name} put {obj.Name} in {destObj.Name}.", fromObj: goCaller, mapping: null, exclude: excludeSelf); } catch (Exception) { }
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
            if (loc is not null)
            {
                try { loc.MsgContents($"{goCaller.Name} put {obj.Name} in {destObj.Name}.", fromObj: goCaller, exclude: excludeSelf); } catch (Exception) { }
            }
            caller.Msg($"You put {obj.Name} in {destObj.Name}.");
            obj.AtPut(goCaller, destObj);
        }
    }
}
