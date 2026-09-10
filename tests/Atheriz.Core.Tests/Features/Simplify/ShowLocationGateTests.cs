// Pins for the unified ShowLocation gate/render and Node.TryResolveLookTarget:
// the null/desc preamble stays ungated and first, Node and container
// locations share one view-gate + AtLook, and the noun/link fallback keeps
// gate-before-resolve order.
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class ShowLocationGateTests
{
    private static GameObject MakePuppet(Node? room, string desc = "")
    {
        var p = GameObject.Create("Seer", isPc: true, desc: desc);
        ObjectRegistry.AddObject(p);
        if (room is not null)
        {
            p.Location = new LocationRef.CoordLocation(room.Coord);
            room.AddObject(p);
        }
        else
        {
            p.Location = LocationRef.NullLocation.Instance;
        }
        p.ClearMessages();
        return p;
    }

    [Fact]
    public void Look_Nowhere_ShowsDesc_WhenPresent()
    {
        using var env = GlobalTestEnv.Enter();
        var p = MakePuppet(null, "A drifter.");
        new LookCommand().Run(p, new LookCommand().Parser!.ParseArgs([]));
        Assert.Contains(p.PeekMessages(), m => m == "A drifter.");
    }

    [Fact]
    public void Look_Nowhere_ShowsNowhere_WhenNoDesc()
    {
        using var env = GlobalTestEnv.Enter();
        var p = MakePuppet(null);
        new LookCommand().Run(p, new LookCommand().Parser!.ParseArgs([]));
        Assert.Contains(p.PeekMessages(), m => m == "You are nowhere.");
    }

    [Fact]
    public void Look_ContainerWithoutView_Refuses_BothTails()
    {
        using var env = GlobalTestEnv.Enter();
        var box = GameObject.Create("box", isContainer: true);
        ObjectRegistry.AddObject(box);
        box.AddLock("view", _ => false);
        var p = GameObject.Create("Seer", isPc: true);
        ObjectRegistry.AddObject(p);
        Assert.True(p.MoveTo(box, force: true, announce: false));
        p.ClearMessages();
        new LookCommand().Run(p, new LookCommand().Parser!.ParseArgs([]));
        Assert.Contains(p.PeekMessages(), m => m == "You can't see anything.");
    }

    [Fact]
    public void Look_NounFallback_Resolves_AfterGate()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord("lookgate1", 0, 0, 0));
        ObjectRegistry.AddObject(room);
        room.AddNoun("plaque", "A brass plaque.");
        var p = MakePuppet(room);
        // Direct target move pins the fallback outside SearchWithFallback.
        Assert.Equal("A brass plaque.", room.TryResolveLookTarget("plaque", p));
        Assert.Null(room.TryResolveLookTarget("nope", p));
        new LookCommand().Run(p, new LookCommand().Parser!.ParseArgs(["plaque"]));
        Assert.Contains(p.PeekMessages(), m => m == "A brass plaque.");
    }

    [Fact]
    public void Look_NounFallback_DeniedView_NeverResolves()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = new NodeHandler();
        NodeHandler.SetCurrent(nh);
        var room = new Node(new Coord("lookgate2", 0, 0, 0));
        ObjectRegistry.AddObject(room);
        room.AddNoun("plaque", "A brass plaque.");
        room.AddLock("view", _ => false);
        var p = MakePuppet(room);
        new LookCommand().Run(p, new LookCommand().Parser!.ParseArgs(["plaque"]));
        Assert.Contains(p.PeekMessages(), m => m == "No match found for 'plaque'.");
        Assert.DoesNotContain(p.PeekMessages(), m => m == "A brass plaque.");
    }
}
