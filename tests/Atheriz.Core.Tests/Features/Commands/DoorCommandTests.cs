using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// The door command accepts a bare direction word (n/s/e/w/u/d and long
// names), not just flag syntax.
[Collection("Ported")]
public sealed class DoorCommandTests
{
    [Fact]
    public void DoorCommand_BareDirectionWord_CreatesDoor()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        try
        {
            var area = new NodeArea("f2room");
            var grid = new NodeGrid("f2room", 0);
            var start = new Node(new Coord("f2room", 0, 0, 0));
            grid.Nodes[(0, 0)] = start;
            ObjectRegistry.AddObject(start);
            var dest = new Node(new Coord("f2room", 2, 0, 0));
            grid.Nodes[(2, 0)] = dest;
            ObjectRegistry.AddObject(dest);
            area.AddGrid(grid);
            nh.AddArea(area);
            var builder = GameObject.Create("f2builder", isPc: true, privilege: Privilege.Builder);
            ObjectRegistry.AddObject(builder);
            Assert.True(builder.MoveTo(start));
            builder.ClearMessages();

            var pa = new GameArgumentParser.ParsedArgs();
            pa["north"] = false; pa["south"] = false; pa["east"] = false; pa["west"] = false;
            pa["up"] = false; pa["down"] = false; pa["remove"] = false; pa["auto"] = true;
            pa["args"] = new List<string> { "east" };
            new DoorCommand().Run(builder, pa);

            var text = string.Join("\n", builder.PeekMessages());
            Assert.Contains("Created door at", text);
            Assert.DoesNotContain("You must specify a direction", text);
        }
        finally { NodeHandler.SetCurrent(null); }
    }
}
