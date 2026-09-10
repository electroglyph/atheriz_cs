// Pins for the location-announce contract shared with the Node overload:
// both MsgContents overloads deliver empty text as "", node delivery falls
// back to raw text on any parser failure (ignoring raiseErrors) while the
// base overload rethrows ParsingError when raiseErrors is set, and a real
// get-announce on a Node location still reaches room members but not the
// excluded actor (dynamic dispatch preserved).
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class ContainmentAnnounceContractTests
{
    private static (Node room, GameObject actor, GameObject watcher) SetupRoom(string area)
    {
        var room = new Node(new Coord(area, 0, 0, 0));
        ObjectRegistry.AddObject(room);
        var actor = GameObject.Create("Actor", isPc: true);
        var watcher = GameObject.Create("Watcher", isPc: true);
        ObjectRegistry.AddObject(actor);
        ObjectRegistry.AddObject(watcher);
        var loc = new Persistence.Dto.LocationRef.CoordLocation(room.Coord);
        actor.Location = loc;
        watcher.Location = loc;
        room.AddObject(actor);
        room.AddObject(watcher);
        actor.ClearMessages();
        watcher.ClearMessages();
        return (room, actor, watcher);
    }

    [Fact]
    public void EmitToContents_EmptyText_DeliversEmpty_ToBothContracts()
    {
        using var env = GlobalTestEnv.Enter();
        var host = GameObject.Create("Host");
        var receiver = GameObject.Create("Recv");
        ObjectRegistry.AddObject(host);
        ObjectRegistry.AddObject(receiver);
        receiver.ClearMessages();
        ContentUtils.EmitToContents([receiver], host, "", null, null, null, null, false, nodeSemantics: true);
        ContentUtils.EmitToContents([receiver], host, "", null, null, null, null, false, nodeSemantics: false);
        Assert.Equal(2, receiver.PeekMessages().Count(m => m == ""));
    }

    [Fact]
    public void EmitToContents_UnknownFunc_NodeFallsBack_BaseRethrowsWhenAsked()
    {
        using var env = GlobalTestEnv.Enter();
        var host = GameObject.Create("Host");
        var receiver = GameObject.Create("Recv");
        ObjectRegistry.AddObject(host);
        ObjectRegistry.AddObject(receiver);
        receiver.ClearMessages();
        // Node delivery never throws — raw text is delivered instead.
        ContentUtils.EmitToContents([receiver], host, "$foo(bar)", null, null, null, null, true, nodeSemantics: true);
        Assert.Contains("$foo(bar)", receiver.PeekMessages());
        // Base delivery rethrows the ParsingError when raiseErrors is set...
        Assert.Throws<FuncParser.ParsingError>(() =>
            ContentUtils.EmitToContents([receiver], host, "$foo(bar)", null, null, null, null, true, nodeSemantics: false));
        // ...and falls back to raw text otherwise.
        ContentUtils.EmitToContents([receiver], host, "$foo(bar)", null, null, null, null, false, nodeSemantics: false);
        Assert.Equal(2, receiver.PeekMessages().Count(m => m == "$foo(bar)"));
    }

    [Fact]
    public void EmitToContents_OverlongText_NodeFallsBack_BaseRethrowsWhenAsked()
    {
        using var env = GlobalTestEnv.Enter();
        var host = GameObject.Create("Host");
        var receiver = GameObject.Create("Recv");
        ObjectRegistry.AddObject(host);
        ObjectRegistry.AddObject(receiver);
        receiver.ClearMessages();
        string huge = new('x', FuncParser.MaxMessageSize + 1);
        ContentUtils.EmitToContents([receiver], host, huge, null, null, null, null, true, nodeSemantics: true);
        Assert.Contains(huge, receiver.PeekMessages());
        Assert.Throws<FuncParser.ParsingError>(() =>
            ContentUtils.EmitToContents([receiver], host, huge, null, null, null, null, true, nodeSemantics: false));
    }

    [Fact]
    public void GetCommand_OnNodeLocation_AnnouncesToRoom_ExcludingActor()
    {
        using var env = GlobalTestEnv.Enter();
        var (room, actor, watcher) = SetupRoom("announce1");
        var item = GameObject.Create("widget", isItem: true);
        ObjectRegistry.AddObject(item);
        item.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord);
        room.AddObject(item);
        var cmd = new GetCommand();
        cmd.Run(actor, cmd.Parser!.ParseArgs(["widget"]));
        Assert.Contains(actor.PeekMessages(), m => m.Contains("You picked up"));
        Assert.Contains(watcher.PeekMessages(), m => m.Contains("picked up"));
        Assert.DoesNotContain(actor.PeekMessages(), m => m.Contains("Actor picked up"));
    }
}
