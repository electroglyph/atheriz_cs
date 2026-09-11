// Build EnsureLinks direction table: the n,s,e,w loop preserves evaluation
// order, so rooms still auto-link bidirectionally including placeholders.
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class BuildEnsureLinksTests
{
    [Fact]
    public void BuildCommand_BuildNorthRoom_CreatesBidirectionalLinks()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler();
        var mh = GlobalServices.GetMapHandler();
        try { nh.RemoveArea("buildlinks"); } catch { }
        var area = new NodeArea("buildlinks");
        var grid = new NodeGrid("buildlinks", 0);
        var start = new Node(new Coord("buildlinks", 0, 0, 0), desc: "Start");
        grid.Nodes[(0, 0)] = start;
        ObjectRegistry.AddObject(start);
        area.AddGrid(grid);
        nh.AddArea(area);
        NodeHandler.SetCurrent(nh);
        try
        {
            // Seed an east neighbor placeholder so the EnsureLinks table takes
            // the east row for the new room (forward link even with no node).
            var mi = mh.EnsureMapInfo("buildlinks", 0);
            mi.Lock.EnterWriteLock();
            try { mi.PreGrid[(1, 1)] = AtherizSettings.Global.RoomPlaceholder; }
            finally { mi.Lock.ExitWriteLock(); }
            var caller = GameObject.Create("build_linksbuilder", isPc: true, privilege: Privilege.Builder);
            ObjectRegistry.AddObject(caller);
            caller.Location = new LocationRef.CoordLocation(start.Coord);
            start.AddObject(caller);
            caller.ClearMessages();

            var pa = new GameArgumentParser.ParsedArgs();
            pa["n"] = true; pa["e"] = false; pa["s"] = false; pa["w"] = false;
            pa["u"] = false; pa["d"] = false; pa["x"] = false;
            pa["room"] = true; pa["road"] = false; pa["path"] = false;
            pa["desc"] = null; pa["single"] = false; pa["double"] = false;
            pa["round"] = false; pa["none"] = false; // none:true would set ch="" and skip the walls/EnsureLinks block under test
            new BuildCommand().Run(caller, pa);

            var created = nh.GetNode(new Coord("buildlinks", 0, 1, 0));
            Assert.NotNull(created);
            Assert.Contains(start.GetLinks(), l => l.Name == "north" && l.Coord.Equals(new Coord("buildlinks", 0, 1, 0)));
            Assert.Contains(created!.GetLinks(), l => l.Name == "south" && l.Coord.Equals(new Coord("buildlinks", 0, 0, 0)));
            // EnsureLinks east row (n,s,e,w order preserved): forward link exists.
            Assert.Contains(created.GetLinks(), l => l.Name == "east" && l.Coord.Equals(new Coord("buildlinks", 1, 1, 0)));
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
