
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class GetCommand : Command
{
    public override string Key => "get";
    public override string Desc => "Get an object.";
    protected override void SetupParser(GameArgumentParser parser) { parser.AddArgument(ParsedArgKeys.Target, nargs: "*", help: "object to get, optionally 'from <container>'"); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        if (!this.RequireParsedArgs(caller, args, out var pa)) return;
        var loc = go.ResolveLocationObject();
        if (loc is null) { CommandHelpers.MsgNo(go); return; }
        string? objName = null, sourceName = null;
        // The parser defines a single positional dest (ParsedArgKeys.Target);
        // live input never carries other keys, so no fallback dests are read here.
        var tokens = pa.GetList(ParsedArgKeys.Target);
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
