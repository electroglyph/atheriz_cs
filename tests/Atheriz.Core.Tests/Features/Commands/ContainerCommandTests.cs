using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Container commands: live put dispatch and give parity.
[Collection("Ported")]
public class ContainerCommandTests
{
    private static void RegisterAll(params GameObject[] objs)
    {
        foreach (var o in objs)
            if (ObjectRegistry.Get(o.Id).Count == 0)
                ObjectRegistry.AddObject(o);
    }

    // --- PutCommand live path ---

    [Fact]
    public void Put_RealDispatch_PutsObjectInContainer()
    {
        // Live `put x in y` dispatches through PutCommand and lands the object
        // in the destination container.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var puppet = GameObject.Create("hero");
            var bag = GameObject.Create("bag", isContainer: true);
            var coin = GameObject.Create("coin");
            RegisterAll(room, puppet, bag, coin);
            Assert.True(puppet.MoveTo(room));
            Assert.True(bag.MoveTo(room));
            Assert.True(coin.MoveTo(puppet));
            puppet.ClearMessages();
            var cmd = new PutCommand();
            var (func, caller, args) = cmd.Execute(puppet, "coin in bag");
            Assert.NotNull(func);
            func!(caller!, args);
            Assert.Contains(coin.Id, bag.ContentsSnapshot);
            Assert.DoesNotContain(coin.Id, puppet.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    // --- GiveCommand requires possession ---
    // Python parity (give.py:162 objs_to_give = caller.search(obj_name)):
    // caller.search covers the caller's INVENTORY only (contents.py search
    // gathers obj.contents), never the room. Room-ground items are refused
    // with "You don't have that." — giving them without pickup would skip
    // AtPreGet and the get lock.
    [Fact]
    public void Give_RoomObject_IsRefused()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var giver = GameObject.Create("giver", isPc: true);
            var box = GameObject.Create("box", isContainer: true);
            var coin = GameObject.Create("coin");
            RegisterAll(room, giver, box, coin);
            Assert.True(giver.MoveTo(room));
            Assert.True(box.MoveTo(room));
            Assert.True(coin.MoveTo(room));
            giver.ClearMessages();
            var cmd = new GiveCommand();
            var (func, caller, args) = cmd.Execute(giver, "coin to box");
            Assert.NotNull(func);
            func!(caller!, args);
            Assert.Contains("You don't have that.", giver.PeekMessages());
            Assert.Contains(coin.Id, room.ContentsSnapshot);
            Assert.DoesNotContain(coin.Id, box.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
