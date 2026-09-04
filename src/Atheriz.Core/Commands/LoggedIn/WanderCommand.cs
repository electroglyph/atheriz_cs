// Port of atheriz/commands/loggedin/wander.py:75
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class WanderCommand : Command
{
    public override string Key => "wander";
    public override string Desc => "Spawn 10 NPCs to your location to wander around";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("count", nargs: "?", type: typeof(int), help: "Number of wanderers to spawn"); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        int count = 10;
        if (pa != null && pa["count"] is int iv) count = iv;
        else if (pa != null && int.TryParse(pa.GetString("count"), out var parsed)) count = parsed;
        var loc = go.ResolveLocationObject() as Node;
        if (loc == null) { go.Msg("You must be in a room to spawn wanderers."); return; }
        var nh = NodeHandler.GetCurrent();
        if (nh == null) { go.Msg("Could not find your current area."); return; }
        var area = nh.GetArea(loc.Coord.Area);
        if (area == null) { go.Msg("Could not find your current area."); return; }
        var grid = area.GetGrid(loc.Coord.Z);
        if (grid == null) { go.Msg("Could not find the grid for your current z-level."); return; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            var randomNode = grid.GetRandomNode();
            if (randomNode == null) continue;
            string name = $"Wanderer {Random.Shared.Next(1000, 9999)}";
            var npc = new WandererNpc(name);
            npc.MoveTo(randomNode);
        }
        sw.Stop();
        go.Msg($"Spawned {count} NPCs across area '{loc.Coord.Area}' in {sw.Elapsed.TotalMilliseconds:F2} milliseconds");
    }
    private sealed class WandererNpc : GameObject
    {
        public WandererNpc(string name)
        {
            Name = name;
            IsNpc = true;
            IsMapable = true;
            IsTickable = true;
            TickSeconds = 1.0;
            Id = IdGenerator.GetId();
            ObjectRegistry.AddObject(this);
        }
        public override void AtTick()
        {
            var loc = ResolveLocationObject() as Node;
            if (loc == null) return;
            var link = loc.GetRandomLink();
            if (link == null) return;
            var nh = NodeHandler.GetCurrent();
            var node = nh?.GetNode(link.Coord);
            if (node == null) return;
            var oldArea = loc.Coord.Area;
            var newArea = node.Coord.Area;
            if (oldArea != newArea)
            {
                try { AtherizLogger.LogWarning($"NPC {Name} (#{Id}) crossing areas: {loc.Coord} -> {node.Coord} via link '{link.Name}' (link.coord={link.Coord})"); } catch { }
                try { AtherizLogger.LogWarning($"Wanderer crossed area {oldArea} -> {newArea}"); } catch { }
            }
            MoveTo(node, toExit: link.Name);
        }
    }
}