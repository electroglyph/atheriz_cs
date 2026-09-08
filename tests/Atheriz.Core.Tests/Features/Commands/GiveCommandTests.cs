using Atheriz.Core;
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Giving resolves via caller search (inventory + room, like Python
// caller.search) and NPC recipients are valid even when disconnected
// (GiveCommand.cs:78 — no connected gate for NPCs).
[Collection("Ported")]
public class GiveCommandTests
{
    private static void RegisterAll(params GameObject[] objs)
    {
        foreach (var o in objs)
            if (ObjectRegistry.Get(o.Id).Count == 0)
                ObjectRegistry.AddObject(o);
    }

    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    [Fact]
    public void Give_OtherCharacter_RequiresPossession()
    {
        // Parity with Python (give.py:162): only inventory can be given —
        // room-ground characters are refused with "You don't have that."
        // , same path as items.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var giver = GameObject.Create("giver", isPc: true);
            var bob = GameObject.Create("bob", isPc: true);
            var alice = GameObject.Create("alice", isPc: true);
            RegisterAll(room, giver, bob, alice);
            giver.IsConnected = true;
            bob.IsConnected = true;
            alice.IsConnected = true;
            Assert.True(giver.MoveTo(room));
            Assert.True(bob.MoveTo(room));
            Assert.True(alice.MoveTo(room));
            giver.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(giver, "give bob to alice", immediate: true);
            RunJob(job);
            Assert.Contains("You don't have that.", giver.PeekMessages());
            Assert.Contains(bob.Id, room.ContentsSnapshot);
            Assert.DoesNotContain(bob.Id, alice.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Give_ToDisconnectedNpc_Succeeds()
    {
        // Shopkeeper NPCs are never connected, yet they must be able to receive gifts.
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            var giver = GameObject.Create("giver", isPc: true);
            var shopkeeper = GameObject.Create("shopkeeper", isNpc: true);
            var coin = GameObject.Create("coin");
            RegisterAll(room, giver, shopkeeper, coin);
            giver.IsConnected = true;
            Assert.True(giver.MoveTo(room));
            Assert.True(shopkeeper.MoveTo(room));
            Assert.True(coin.MoveTo(giver));
            giver.ClearMessages();
            var job = CommandDispatcher.DispatchLoggedIn(giver, "give coin to shopkeeper", immediate: true);
            RunJob(job);
            var msgs = string.Join("\n", giver.PeekMessages());
            Assert.Contains("You give coin to shopkeeper", msgs);
            Assert.Contains(coin.Id, shopkeeper.ContentsSnapshot);
            Assert.DoesNotContain(coin.Id, giver.ContentsSnapshot);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
