// Door link repair: iterating the GetLinks snapshot directly (no second copy)
// still relinks both ends of a wrong-coord pair.
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class DoorLinkRepairTests
{
    [Fact]
    public void DoorCommand_RepairWrongCoord_RelinksToCorrectCoord()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var area = new NodeArea("doorrepair");
            var grid = new NodeGrid("doorrepair", 0);
            var start = new Node(new Coord("doorrepair", 0, 0, 0));
            grid.Nodes[(0, 0)] = start;
            ObjectRegistry.AddObject(start);
            var dest = new Node(new Coord("doorrepair", 2, 0, 0));
            grid.Nodes[(2, 0)] = dest;
            ObjectRegistry.AddObject(dest);
            area.AddGrid(grid);
            nh.AddArea(area);
            var caller = GameObject.Create("door_repairbuilder", isPc: true, privilege: Privilege.Builder);
            ObjectRegistry.AddObject(caller);
            Assert.True(caller.MoveTo(start));
            // Wrong-coord links on both ends force both repair loops.
            dest.AddLink(new NodeLink("west", new Coord("doorrepair", 9, 9, 0), new List<string> { "w" }));
            start.AddLink(new NodeLink("east", new Coord("doorrepair", 9, 9, 0), new List<string> { "e" }));
            caller.ClearMessages();

            var pa = new GameArgumentParser.ParsedArgs();
            pa["north"] = false; pa["south"] = false; pa["east"] = true; pa["west"] = false;
            pa["up"] = false; pa["down"] = false; pa["remove"] = false; pa["auto"] = true;
            pa["args"] = new List<string>();
            new DoorCommand().Run(caller, pa);

            var destWest = dest.GetLinks().Where(l => l.Name == "west").ToList();
            Assert.Single(destWest);
            Assert.Equal(new Coord("doorrepair", 0, 0, 0), destWest[0].Coord);
            var hereEast = start.GetLinks().Where(l => l.Name == "east").ToList();
            Assert.Single(hereEast);
            Assert.Equal(new Coord("doorrepair", 2, 0, 0), hereEast[0].Coord);
            var text = string.Join("\n", caller.PeekMessages());
            Assert.Contains("Removed link 'west'", text);
            Assert.Contains("Removed link 'east'", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
