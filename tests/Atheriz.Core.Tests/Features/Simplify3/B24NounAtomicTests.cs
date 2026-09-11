// Pins for U9 (atomic AddNounIfAbsent): the check and the insert share one
// write hold, so concurrent adds of the same new noun elect exactly one
// "Added" winner; the noun command keeps its byte-identical messages for the
// add and update paths on the real engine path.
using System.Collections.Concurrent;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B24NounAtomicTests
{
    private static Node NewRoom(string area)
    {
        var coord = new Coord(area, 0, 0, 0);
        var loc = new Node(coord);
        var nh = NodeHandler.GetCurrent() ?? new NodeHandler();
        NodeHandler.SetCurrent(nh);
        nh.AddNode(loc);
        return loc;
    }

    [Fact]
    public void AddNounIfAbsent_Absent_InsertsAndReturnsTrue()
    {
        using var env = GlobalTestEnv.Enter();
        var loc = NewRoom("nounatomic1");
        Assert.True(loc.AddNounIfAbsent("statue", "A statue"));
        Assert.Equal("A statue", loc.GetNoun("statue"));
    }

    [Fact]
    public void AddNounIfAbsent_Present_KeepsExistingAndReturnsFalse()
    {
        using var env = GlobalTestEnv.Enter();
        var loc = NewRoom("nounatomic2");
        loc.AddNoun("statue", "An old statue");
        Assert.False(loc.AddNounIfAbsent("STATUE", "A new statue"));
        Assert.Equal("An old statue", loc.GetNoun("statue"));
    }

    [Fact]
    public void AddNounIfAbsent_ConcurrentSameKey_ExactlyOneAdded()
    {
        using var env = GlobalTestEnv.Enter();
        var loc = NewRoom("nounatomic3");
        const int racers = 8;
        using var barrier = new Barrier(racers + 1);
        var errors = new ConcurrentQueue<Exception>();
        var added = new ConcurrentQueue<bool>();
        var threads = Enumerable.Range(0, racers).Select(_ =>
        {
            var thread = new Thread(() =>
            {
                try
                {
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                    added.Enqueue(loc.AddNounIfAbsent("statue", "A statue"));
                }
                catch (Exception ex) { errors.Enqueue(ex); }
            })
            { IsBackground = true };
            return thread;
        }).ToList();
        threads.ForEach(t => t.Start());
        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
        foreach (var thread in threads)
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "racer did not finish; possible deadlock");
        Assert.Empty(errors);
        Assert.Equal(racers, added.Count);
        Assert.Single(added, v => v);
        Assert.Equal("A statue", loc.GetNoun("statue"));
    }

    [Fact]
    public void NounCommand_AddThenUpdate_MessagesByteIdentical()
    {
        using var env = GlobalTestEnv.Enter();
        var loc = NewRoom("nounatomic4");
        var caller = PortedHelpers.MakeCaller("NounBuilder", builder: true);
        caller.Location = new LocationRef.CoordLocation(loc.Coord);

        var addArgs = new GameArgumentParser.ParsedArgs();
        addArgs["noun"] = "rock";
        addArgs["desc"] = new List<string> { "a", "stone" };
        new NounCommand().Run(caller, addArgs);
        Assert.Equal("a stone", loc.GetNoun("rock"));
        Assert.Contains(caller.PeekMessages(), m => m == "Added 'rock'.");

        caller.ClearMessages();
        var updateArgs = new GameArgumentParser.ParsedArgs();
        updateArgs["noun"] = "rock";
        updateArgs["desc"] = new List<string> { "a", "new", "stone" };
        new NounCommand().Run(caller, updateArgs);
        Assert.Equal("a new stone", loc.GetNoun("rock"));
        Assert.Contains(caller.PeekMessages(), m => m == "Updated 'rock'.");
    }
}
