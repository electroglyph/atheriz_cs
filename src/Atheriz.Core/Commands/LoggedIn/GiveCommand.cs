// Port of atheriz/commands/loggedin/give.py:189
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class GiveCommand : Command
{
    public override string Key => "give";
    public override string Desc => "Give an object to someone else.";
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("args", nargs: "*", help: "object to give, optionally 'to <target>'"); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { go.Msg(PrintHelp()); return; }
        var loc = go.ResolveLocationObject();
        if (loc == null) { go.Msg("No."); return; }
        var tokens = pa.GetList("args");
        if (tokens.Count == 0) { go.Msg("Give it to whom?"); return; }
        string? objName = null, targetName = null;
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
                    var locMatches = CommandHelpers.SearchIn(loc, candTgt, go);
                    if (locMatches.Count > 0) { foundObj = candObj; foundTgt = candTgt; break; }
                    continue;
                }
                if (CommandHelpers.SearchWithFallback(go, candObj).Count > 0)
                {
                    var locMatches = CommandHelpers.SearchIn(loc, candTgt, go);
                    if (locMatches.Count > 0) { foundObj = candObj; foundTgt = candTgt; break; }
                }
            }
            if (foundObj != null) { objName = foundObj; targetName = foundTgt; }
            else
            {
                var lastObj = string.Join(" ", tokens.Take(tokens.Count - 1));
                var lastTgt = tokens.Last();
                bool lastObjIsAll = lastObj.Equals("all", StringComparison.OrdinalIgnoreCase);
                bool lastObjInInv = CommandHelpers.SearchWithFallback(go, lastObj).Any(o => go.ContentsSnapshot.Contains(o.Id));
                if (lastObjIsAll || lastObjInInv)
                { objName = lastObj; targetName = lastTgt; }
                else
                {
                    var firstObj = tokens[0];
                    var restTgt = string.Join(" ", tokens.Skip(1));
                    bool firstObjIsAll = firstObj.Equals("all", StringComparison.OrdinalIgnoreCase);
                    bool firstObjInInv = CommandHelpers.SearchWithFallback(go, firstObj).Any(o => go.ContentsSnapshot.Contains(o.Id));
                    if (firstObjIsAll || firstObjInInv)
                    { objName = firstObj; targetName = restTgt; }
                    else { objName = lastObj; targetName = lastTgt; }
                }
            }
        }
        if (objName == null || targetName == null) { go.Msg("Give it to whom?"); return; }
        List<GameObject> tgtMatches = CommandHelpers.SearchIn(loc, targetName, go);
        if (tgtMatches.Count == 0) { go.Msg($"Could not find '{targetName}' here."); return; }
        if (tgtMatches.Count > 1) { go.Msg($"Multiple matches found for '{targetName}'."); return; }
        var target = tgtMatches[0];
        if (target.Id == go.Id) { go.Msg("You already have that!"); return; }
        if ((target.IsPc || target.IsNpc) && !target.IsConnected) { go.Msg($"Could not find '{targetName}' here."); return; }
        if (!target.IsContainer && !target.IsNpc && !target.IsPc) { go.Msg($"You can't give anything to {target.GetDisplayName(go)}."); return; }
        List<GameObject> objsToGive;
        if (objName == "all") objsToGive = ObjectRegistry.Get(go.ContentsSnapshot.ToList());
        else
        {
            var found = CommandHelpers.SearchWithFallback(go, objName);
            objsToGive = found.ToList();
            if (objsToGive.Count == 0) { go.Msg("You don't have that."); return; }
        }
        if (objsToGive.Count == 0) { go.Msg("You don't have that."); return; }
        bool givenAny = false;
        foreach (var obj in objsToGive.ToList())
        {
            if (obj.Id == target.Id) continue;
            if (!obj.AtPreGive(go, target)) continue;
            if (obj.MoveTo(target))
            {
                obj.AtGive(go, target);
                givenAny = true;
                go.Msg($"You give {obj.Name} to {target.Name}.");
                target.Msg($"{go.Name} gives you {obj.Name}.");
                if (loc is Node ln) ln.MsgContents($"{go.Name} gives {obj.Name} to {target.Name}.", exclude: new List<GameObject> { go, target }, fromObj: go);
                else loc.MsgContents($"{go.Name} gives {obj.Name} to {target.Name}.", fromObj: go, exclude: new List<GameObject> { go, target });
            }
            else go.Msg($"You can't give {obj.Name} to {target.Name}.");
        }
        if (!givenAny && objName == "all") go.Msg("You have nothing to give.");
    }
}
