// The mapedit launch_draw room payload carries every room property the
// webclient room editor shows: coords, name, description, and exits.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class DrawCommandRoomNameTests
{
    private sealed class FakeConn : BaseConnection
    {
        public FakeConn(string host = "10.0.0.1") : base("test") { ClientHost = host; Session.Puppet = CreateBuilder(); }
        private static GameObject CreateBuilder() { var b = GameObject.Create("B", isPc: true); b.PrivilegeLevel = Privilege.Builder; return b; }
        public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null) { lock (Sent) Sent.Add((cmd, args ?? new(), kwargs ?? new())); }
        public override void Close() { }
        public List<(string Cmd, List<object?> Args, Dictionary<string, object?> Kw)> Sent = new();
    }

    private static void Reset()
    {
        MapEdit.Reset();
        InputFuncs.MapHandlerFactory = () => GlobalServices.GetMapHandler();
        InputFuncs.NodeHandlerFactory = () => NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler();
    }

    [Fact]
    public void DrawCommand_RoomPayload_IncludesNameDescAndExits()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mi = new MapInfo("TestArea");
        mi.SetPreCell((0, 0), new Atheriz.Core.Settings.AtherizSettings().RoomPlaceholder);
        var mh = GlobalServices.GetMapHandler();
        mh.SetMapInfo("TestArea", 0, mi);
        var room = new Node(new Coord("TestArea", 0, 0, 0));
        ObjectRegistry.AddObject(room);
        room.Desc = "A dusty hall.";
        room.AddLink(new NodeLink("North", new Coord("TestArea", 0, 1, 0), new List<string> { "n" }));
        var grid = new NodeGrid("TestArea", 0);
        grid.AddNode(room);
        var area = new NodeArea("TestArea");
        area.AddGrid(grid);
        var nh = new NodeHandler(autoLoad: false);
        nh.AddArea(area);
        NodeHandler.SetCurrent(nh);
        var callerNode = new Node(new Coord("TestArea", 3, 7, 0));
        ObjectRegistry.AddObject(callerNode);
        var conn = new FakeConn();
        var caller = GameObject.Create("Caller", isPc: true);
        caller.PrivilegeLevel = Privilege.Builder;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(callerNode.Coord);
        caller.Session = new Session(conn);
        caller.Session.Connection = conn;
        conn.Session.Puppet = caller;
        InputFuncs.MapHandlerFactory = () => mh;
        InputFuncs.NodeHandlerFactory = () => nh;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        var payload = conn.Sent.First(s => s.Cmd == "launch_draw").Args[1] as Dictionary<string, object?>;
        var rooms = payload!["rooms"] as List<Dictionary<string, object?>>;
        Assert.Single(rooms!);
        var r = rooms![0];
        Assert.Equal(0, r["x"]);
        Assert.Equal(0, r["y"]);
        // Unset display names arrive as blank, never as the coord —
        // the editor shows exactly what the server sent.
        Assert.Equal("", r["name"]);
        Assert.Equal("A dusty hall.", r["desc"]);
        var exits = r["exits"] as List<Dictionary<string, object?>>;
        Assert.Single(exits!);
        Assert.Equal("North", exits![0]["name"]);
    }

    [Fact]
    public void DrawCommand_RoomPayload_PrefersDisplayNameOverCoord()
    {
        using var env = GlobalTestEnv.Enter();
        Reset();
        var mi = new MapInfo("TestArea");
        mi.SetPreCell((0, 0), new Atheriz.Core.Settings.AtherizSettings().RoomPlaceholder);
        var mh = GlobalServices.GetMapHandler();
        mh.SetMapInfo("TestArea", 0, mi);
        var room = new Node(new Coord("TestArea", 0, 0, 0)) { DisplayName = "Grand Hall" };
        ObjectRegistry.AddObject(room);
        room.Desc = "A dusty hall.";
        var grid = new NodeGrid("TestArea", 0);
        grid.AddNode(room);
        var area = new NodeArea("TestArea");
        area.AddGrid(grid);
        var nh = new NodeHandler(autoLoad: false);
        nh.AddArea(area);
        NodeHandler.SetCurrent(nh);
        var callerNode = new Node(new Coord("TestArea", 3, 7, 0));
        ObjectRegistry.AddObject(callerNode);
        var conn = new FakeConn();
        var caller = GameObject.Create("Caller2", isPc: true);
        caller.PrivilegeLevel = Privilege.Builder;
        caller.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(callerNode.Coord);
        caller.Session = new Session(conn);
        caller.Session.Connection = conn;
        conn.Session.Puppet = caller;
        InputFuncs.MapHandlerFactory = () => mh;
        InputFuncs.NodeHandlerFactory = () => nh;
        var draw = new Atheriz.Core.Commands.LoggedIn.DrawCommand();
        draw.Run(caller, null);
        var payload = conn.Sent.First(s => s.Cmd == "launch_draw").Args[1] as Dictionary<string, object?>;
        var rooms = payload!["rooms"] as List<Dictionary<string, object?>>;
        Assert.Single(rooms!);
        Assert.Equal("Grand Hall", rooms![0]["name"]);
    }
}
