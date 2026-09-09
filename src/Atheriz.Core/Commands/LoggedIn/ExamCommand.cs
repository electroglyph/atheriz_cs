// Port of atheriz/commands/loggedin/exam.py:265

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class ExamCommand : Command
{
    public override string Key => "examine";
    public override IReadOnlyList<string> Aliases => ["exam", "ex", "exa"];
    public override string Desc => "Examine an object to see its attributes.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("target", nargs: "?", help: "Object to examine (name or #id).");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        string? targetStr = pa?.GetString("target");
        GameObject? target = null;
        if (string.IsNullOrEmpty(targetStr))
        {
            target = go.ResolveLocationObject();
            if (target is null) { go.Msg("You are nowhere to examine."); return; }
        }
        else
        {
            target = CommandHelpers.ResolveObject(go, targetStr!);
            if (target is null) return;
        }
        if (target is Node nodeTarget)
        {
            string areaName;
            try
            {
                var nh = NodeHandler.GetCurrent();
                var area = nh?.GetArea(nodeTarget.Coord.Area);
                areaName = area?.Name ?? nodeTarget.Coord.Area;
            }
            catch { areaName = nodeTarget.Coord.Area; }
            go.Msg($"Examining Node at {nodeTarget.Coord} in area '{areaName}', z={nodeTarget.Coord.Z} (#{target.Id}):");
        }
        else go.Msg($"Examining {target.Name} (#{target.Id}):");
        var collected = ExamFormatter.Collect(target);
        Dictionary<string, object?> dict = [];
        HashSet<string> propNames = [];
        foreach (var (k, v, p) in collected) { dict[k] = v; if (p) propNames.Add(k); }
        var keysInOrder = dict.Keys.ToList();
        foreach (var key in keysInOrder)
        {
            var val = dict[key];
            var valOutput = FormatValue(val, key);
            string typeName = val?.GetType().Name ?? "null";
            string marker = propNames.Contains(key) ? " [property]" : "";
            if (valOutput is List<string> list)
            {
                go.Msg($"  {key}: {list[0]} ({typeName}{marker})");
                for (int i = 1; i < list.Count; i++) go.Msg($"    {list[i]}");
            }
            else
            {
                string valStr = valOutput as string ?? valOutput?.ToString() ?? "<unprintable>";
                go.Msg($"  {key}: {valStr} ({typeName}{marker})");
            }
        }
    }

    private static object FormatValue(object? val, string? hint) => ExamFormatter.FormatValue(val, hint);
}
