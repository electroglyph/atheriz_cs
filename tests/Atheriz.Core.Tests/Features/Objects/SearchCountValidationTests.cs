using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Objects;

// A leading count that cannot name a positive quantity ("-1", "0") must stay
// an ordinary search term. Treating it as a count used to reach the slice
// return with a negative bound and throw instead of returning no match.
[Collection("Ported")]
public class SearchCountValidationTests
{
    private static (GameObject room, Func<int, GameObject?> resolver) SetupSwords()
    {
        var room = GameObject.Create("room");
        room.IsContainer = true;
        var first = GameObject.Create("sword");
        var second = GameObject.Create("sword");
        room.AddContent(first.Id);
        room.AddContent(second.Id);
        var dict = new Dictionary<int, GameObject> { [first.Id] = first, [second.Id] = second };
        GameObject? R(int id) => dict.TryGetValue(id, out var o) ? o : null;
        return (room, R);
    }

    [Fact]
    public void NegativeCount_ReturnsNoMatches()
    {
        var (room, R) = SetupSwords();
        Assert.Empty(ContentUtils.Search(room, "-1 sword", R));
    }

    [Fact]
    public void NegativeCount_BeforeIndexTerm_ReturnsNoMatches()
    {
        var (room, R) = SetupSwords();
        Assert.Empty(ContentUtils.Search(room, "-1 sword 2", R));
    }

    [Fact]
    public void PositiveCount_StillLimitsMatches()
    {
        var (room, R) = SetupSwords();
        Assert.Equal(2, ContentUtils.Search(room, "2 sword", R).Count);
    }

    [Fact]
    public void ZeroCount_ReturnsNoMatches()
    {
        var (room, R) = SetupSwords();
        Assert.Empty(ContentUtils.Search(room, "0 sword", R));
    }

    [Fact]
    public void BareZero_ReturnsNoMatches()
    {
        var (room, R) = SetupSwords();
        Assert.Empty(ContentUtils.Search(room, "0", R));
    }

    [Fact]
    public void AllKeyword_StillReturnsEveryMatch()
    {
        var (room, R) = SetupSwords();
        Assert.Equal(2, ContentUtils.Search(room, "all sword", R).Count);
    }

    [Fact]
    public void GetCommand_NegativeCount_ReportsObjectNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("negGet", 0, 0, 0));
        ObjectRegistry.AddObject(room);
        var caller = PortedHelpers.MakeCaller("Alice", Privilege.Player, room);
        var sword = GameObject.Create("sword");
        ObjectRegistry.AddObject(sword);
        sword.MoveTo(room);

        var cmd = new GetCommand();
        var (fn, target, pargs) = cmd.Execute(caller, "-1 sword", "get");
        if (fn is not null) fn(target!, pargs);
        else cmd.Run(caller, pargs);

        Assert.Contains("Object not found.", caller.PeekMessages());
        Assert.Contains(sword.Id, room.ContentsSnapshot);
    }

    [Fact]
    public void DropCommand_NegativeCount_ReportsObjectNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("negDrop", 0, 0, 0));
        ObjectRegistry.AddObject(room);
        var caller = PortedHelpers.MakeCaller("Alice", Privilege.Player, room);
        var sword = GameObject.Create("sword");
        ObjectRegistry.AddObject(sword);
        sword.MoveTo(caller);

        var cmd = new DropCommand();
        var (fn, target, pargs) = cmd.Execute(caller, "-1 sword", "drop");
        if (fn is not null) fn(target!, pargs);
        else cmd.Run(caller, pargs);

        Assert.Contains("Object not found.", caller.PeekMessages());
        Assert.Contains(sword.Id, caller.ContentsSnapshot);
    }

    [Fact]
    public void GetCommand_ZeroCount_ReportsObjectNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("zeroGet", 0, 0, 0));
        ObjectRegistry.AddObject(room);
        var caller = PortedHelpers.MakeCaller("Alice", Privilege.Player, room);
        var sword = GameObject.Create("sword");
        ObjectRegistry.AddObject(sword);
        sword.MoveTo(room);

        var cmd = new GetCommand();
        var (fn, target, pargs) = cmd.Execute(caller, "0 sword", "get");
        if (fn is not null) fn(target!, pargs);
        else cmd.Run(caller, pargs);

        Assert.Contains("Object not found.", caller.PeekMessages());
        Assert.Contains(sword.Id, room.ContentsSnapshot);
    }

    [Fact]
    public void DropCommand_ZeroCount_ReportsObjectNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("zeroDrop", 0, 0, 0));
        ObjectRegistry.AddObject(room);
        var caller = PortedHelpers.MakeCaller("Alice", Privilege.Player, room);
        var sword = GameObject.Create("sword");
        ObjectRegistry.AddObject(sword);
        sword.MoveTo(caller);

        var cmd = new DropCommand();
        var (fn, target, pargs) = cmd.Execute(caller, "0 sword", "drop");
        if (fn is not null) fn(target!, pargs);
        else cmd.Run(caller, pargs);

        Assert.Contains("Object not found.", caller.PeekMessages());
        Assert.Contains(sword.Id, caller.ContentsSnapshot);
    }
}
