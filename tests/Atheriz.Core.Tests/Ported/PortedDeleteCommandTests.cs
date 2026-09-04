// Port of atheriz/tests/test_delete_command.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedDeleteCommandTests
{
    private static GameObject MakeCaller(string name="Admin") => PortedHelpers.MakeCaller(name, Privilege.Builder);
    private static Node MakeRoom(string area="test", int x=0,int y=0,int z=0)
    {
        var coord = new Coord(area, x, y, z);
        var n = new Node(coord);
        NodeHandler.GetCurrent()?.AddNode(n);
        return n;
    }

    [Fact]
    public void DeleteInventoryItem()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller();
        var room = MakeRoom();
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord);
        room.AddObject(caller);
        var item = GameObject.Create("Apple"); ObjectRegistry.AddObject(item); item.MoveTo(caller);
        Assert.Contains(item.Id, caller.ContentsSnapshot);
        var cmd = new DeleteCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Apple" }));
        Assert.DoesNotContain(item.Id, caller.ContentsSnapshot);
        Assert.DoesNotContain(item.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        Assert.True(item.IsDeleted);
        var msg = GameUtils.StripAnsi(string.Join(" ", caller.PeekMessages()));
        Assert.Contains("Deleted Apple", msg);
    }

    [Fact]
    public void DeleteRoomItem()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller(); var room = MakeRoom("test2");
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var item = GameObject.Create("Sword"); ObjectRegistry.AddObject(item); item.MoveTo(room);
        Assert.Contains(item.Id, room.ContentsSnapshot);
        var cmd = new DeleteCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Sword" }));
        Assert.DoesNotContain(item.Id, room.ContentsSnapshot);
        Assert.DoesNotContain(item.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        var msg = GameUtils.StripAnsi(string.Join(" ", caller.PeekMessages()));
        Assert.Contains("Deleted Sword", msg);
    }

    private static string StripAnsi(string input) => System.Text.RegularExpressions.Regex.Replace(input, @"\x1b\[[0-9;]*m", "");
    [Fact]
    public void DeleteHere()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller(); var room = MakeRoom("test");
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var cmd = new DeleteCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "here" }));
        Assert.True(room.IsDeleted);
        var raw = string.Join(" ", caller.PeekMessages());
        System.Console.WriteLine($"RAW_MSG:{raw}");
        System.Console.WriteLine($"STRIPPED:{StripAnsi(raw)}");
        System.Console.WriteLine($"ISBUILDER:{caller.IsBuilder} PRIV:{caller.PrivilegeLevel} Q:{caller.Quelled}");
        System.Console.WriteLine($"ROOM_NAME:{room.Name} ROOM_DISPLAY:{room.GetDisplayName(caller)}");
        System.Console.WriteLine($"ROOM_ISBUILDER_DISPLAY:{room.GetDisplayName(caller)}");
        var msg = StripAnsi(raw);
        Assert.Contains("test,0,0,0", msg);
    }

    [Fact]
    public void DeleteByCoord()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller(); var room = MakeRoom("test");
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var cmd = new DeleteCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "(test,0,0,0)" }));
        Assert.True(room.IsDeleted);
        var msg = StripAnsi(string.Join(" ", caller.PeekMessages()));
        Assert.Contains("test,0,0,0", msg);
    }

    [Fact]
    public void DeleteRecursive()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller(); var room = MakeRoom("test5");
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var container = GameObject.Create("Chest", isContainer: true); ObjectRegistry.AddObject(container); container.MoveTo(room);
        var item = GameObject.Create("Gold"); ObjectRegistry.AddObject(item); item.MoveTo(container);
        var cmd = new DeleteCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Chest", "-r" }));
        Assert.DoesNotContain(container.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        Assert.DoesNotContain(item.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        var msg = GameUtils.StripAnsi(string.Join(" ", caller.PeekMessages()));
        Assert.Contains("2 objects total", msg);
    }

    [Fact]
    public void DeleteNonRecursive()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller(); var room = MakeRoom("test6");
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var container = GameObject.Create("Chest", isContainer: true); ObjectRegistry.AddObject(container); container.MoveTo(room);
        var item = GameObject.Create("Gold"); ObjectRegistry.AddObject(item); item.MoveTo(container);
        var cmd = new DeleteCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Chest" }));
        Assert.DoesNotContain(container.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        Assert.Contains(item.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        var loc = item.Location as Persistence.Dto.LocationRef.CoordLocation;
        Assert.NotNull(loc); Assert.Equal(room.Coord, loc!.Coord);
        Assert.Contains(item.Id, room.ContentsSnapshot);
        var msg = GameUtils.StripAnsi(string.Join(" ", caller.PeekMessages()));
        Assert.Contains("Deleted Chest", msg);
    }

    [Fact]
    public void DeletePermissionDenied()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller(); caller.PrivilegeLevel = Privilege.Builder;
        var room = MakeRoom("test7");
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var player = GameObject.Create("Player", privilege: Privilege.Player); ObjectRegistry.AddObject(player); player.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(player); player.ClearMessages();
        var item = GameObject.Create("Safe"); ObjectRegistry.AddObject(item); item.MoveTo(room);
        var cmd = new DeleteCommand();
        Assert.False(cmd.Access(player));
        item.AddLock("delete", _ => false);
        caller.ClearMessages();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "Safe" }));
        Assert.Contains(item.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        var msg = GameUtils.StripAnsi(string.Join(" ", caller.PeekMessages())).ToLowerInvariant();
        Assert.Contains("do not have permission", msg);
    }

    [Fact]
    public void DeleteNoMatch()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller(); var room = MakeRoom("test8");
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var cmd = new DeleteCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "nothing" }));
        var msg = GameUtils.StripAnsi(string.Join(" ", caller.PeekMessages()));
        Assert.Contains("No match found", msg);
    }

    [Fact]
    public void DeleteMultipleMatches()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = MakeCaller(); var room = MakeRoom("test9");
        caller.Location = new Persistence.Dto.LocationRef.CoordLocation(room.Coord); room.AddObject(caller);
        var i1 = GameObject.Create("key"); ObjectRegistry.AddObject(i1); i1.MoveTo(room);
        var i2 = GameObject.Create("key"); ObjectRegistry.AddObject(i2); i2.MoveTo(room);
        var cmd = new DeleteCommand();
        cmd.Run(caller, cmd.Parser!.ParseArgs(new[] { "keys" }));
        var msg = GameUtils.StripAnsi(string.Join(" ", caller.PeekMessages()));
        Assert.Contains("Multiple matches", msg);
        Assert.Contains(i1.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
        Assert.Contains(i2.Id, ObjectRegistry.FilterBy(_=>true).Select(o=>o.Id));
    }
}
