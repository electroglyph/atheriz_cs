using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Ambiguous-name search order: fast-path/indexed consistency, stable
// first-match-wins, and single-recording of objects resolved twice — the
// HashSet only accelerates the duplicate guard, never the order.
[Collection("Ported")]
public class SearchOrderTests
{
    private static (GameObject room, Func<int, GameObject?> resolver) SetupGuards()
    {
        var room = GameObject.Create("room");
        room.IsContainer = true;
        var g1 = GameObject.Create("guard");
        var g2 = GameObject.Create("guard");
        room.AddContent(g1.Id);
        room.AddContent(g2.Id);
        var dict = new Dictionary<int, GameObject> { [g1.Id] = g1, [g2.Id] = g2 };
        GameObject? R(int id) => dict.TryGetValue(id, out var o) ? o : null;
        return (room, R);
    }

    [Fact]
    public void FastPath_MatchesIndexedFirst()
    {
        // The count==1 fast path returns the same object as an explicit
        // index-1 pick: both are first-match-wins over the same iteration.
        var (room, R) = SetupGuards();
        var fast = ContentUtils.Search(room, "guard", R);
        var first = ContentUtils.Search(room, "guard 1", R);
        Assert.Single(fast);
        Assert.Single(first);
        Assert.Equal(fast[0], first[0]);
    }

    [Fact]
    public void IndexedPicks_AreDistinctAndCoverAll()
    {
        var (room, R) = SetupGuards();
        var first = ContentUtils.Search(room, "guard 1", R);
        var second = ContentUtils.Search(room, "guard 2", R);
        Assert.Single(first);
        Assert.Single(second);
        Assert.NotEqual(first[0].Id, second[0].Id);
        var all = ContentUtils.Search(room, "all guard", R);
        Assert.Equal(2, all.Count);
        Assert.Contains(first[0], all);
        Assert.Contains(second[0], all);
    }

    [Fact]
    public void SameObjectResolvedTwice_RecordedOnce()
    {
        // Two content ids resolving to the same object (aliased ids) record
        // it once — the duplicate guard the HashSet accelerates.
        var room = GameObject.Create("room");
        room.IsContainer = true;
        var g = GameObject.Create("guard");
        room.AddContent(9001);
        room.AddContent(9002);
        GameObject? R(int id) => id is 9001 or 9002 ? g : null;
        var all = ContentUtils.Search(room, "all guard", R, recursive: false);
        Assert.Single(all);
        Assert.Equal(g, all[0]);
    }

    [Fact]
    public void AliasMatch_JoinsSameResultSet()
    {
        var room = GameObject.Create("room");
        room.IsContainer = true;
        var g1 = GameObject.Create("guard");
        var g2 = GameObject.Create("watchman");
        g2.Aliases = ["guard"];
        room.AddContent(g1.Id);
        room.AddContent(g2.Id);
        var dict = new Dictionary<int, GameObject> { [g1.Id] = g1, [g2.Id] = g2 };
        GameObject? R(int id) => dict.TryGetValue(id, out var o) ? o : null;
        var all = ContentUtils.Search(room, "all guard", R);
        Assert.Equal(2, all.Count);
        var one = ContentUtils.Search(room, "guard", R);
        Assert.Single(one);
        Assert.Contains(one[0], all);
    }
}
