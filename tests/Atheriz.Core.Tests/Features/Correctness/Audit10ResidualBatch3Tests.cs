using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class Audit10ResidualBatch3Tests
{
    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    private static (Node RoomA, Node RoomB) TwoRooms(string area)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var coordA = new Coord(area, 0, 0, 0);
        var coordB = new Coord(area, 0, 1, 0);
        var roomA = new Node(coordA);
        var roomB = new Node(coordB);
        nh.AddNode(roomA);
        nh.AddNode(roomB);
        ObjectRegistry.AddObject(roomA);
        ObjectRegistry.AddObject(roomB);
        roomA.AddLink(new NodeLink("north", coordB));
        roomB.AddLink(new NodeLink("south", coordA));
        return (roomA, roomB);
    }

    [Fact]
    public void InstalledExit_ClosedUnlockedDoor_TraversesAndRecloses()
    {
        using var env = GlobalTestEnv.Enter();
        var (roomA, roomB) = TwoRooms("pin-traverse");
        var door = Door.Create(roomA.Coord, "north", roomB.Coord, "south", closed: true, locked: false);
        NodeHandler.GetCurrent()?.AddDoor(door);
        var pc = GameObject.Create("pin-traverse-pc", isPc: true, privilege: Privilege.Admin);
        ObjectRegistry.AddObject(pc);
        Assert.True(pc.MoveTo(roomA, force: true, announce: false));

        RunJob(CommandDispatcher.DispatchLoggedIn(pc, "north", immediate: true));

        Assert.Equal(roomB.Coord, Assert.IsType<Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation>(pc.Location).Coord);
        Assert.True(door.Closed);
        Assert.False(door.Locked);
    }

    [Fact]
    public void InstalledExit_LockedDoor_RefusesAndStays()
    {
        using var env = GlobalTestEnv.Enter();
        var (roomA, roomB) = TwoRooms("pin-refuse");
        var door = Door.Create(roomA.Coord, "north", roomB.Coord, "south", closed: true, locked: true);
        NodeHandler.GetCurrent()?.AddDoor(door);
        var pc = GameObject.Create("pin-refuse-pc", isPc: true, privilege: Privilege.Admin);
        ObjectRegistry.AddObject(pc);
        Assert.True(pc.MoveTo(roomA, force: true, announce: false));

        RunJob(CommandDispatcher.DispatchLoggedIn(pc, "north", immediate: true));

        Assert.Equal(roomA.Coord, Assert.IsType<Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation>(pc.Location).Coord);
        Assert.True(door.Closed);
        Assert.True(door.Locked);
    }

    [Fact]
    public void Shutdown_BlankTokenFile_RefusesWithoutPosting()
    {
        using var env = GlobalTestEnv.Enter();
        var origSecret = AtherizSettings.Global.SecretPath;
        var secretDir = Path.Combine(env.TempPath, "blanksecret");
        Directory.CreateDirectory(secretDir);
        File.WriteAllBytes(Path.Combine(secretDir, "admin.token"), []);
        AtherizSettings.Global.SecretPath = secretDir;
        try
        {
            ObjectRegistry.ClearAll();
            var admin = GameObject.Create("root", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            admin.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(admin, "shutdown", immediate: true);
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            // Give the fire-and-forget POST attempt (if any) no chance: the
            // blank token must refuse synchronously before any dial.
            var msgs = string.Join("\n", admin.PeekMessages());
            Assert.Contains("admin.token not found", msgs);
        }
        finally
        {
            AtherizSettings.Global.SecretPath = origSecret;
            ObjectRegistry.ClearAll();
        }
    }
}
