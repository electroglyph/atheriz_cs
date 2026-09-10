using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Search term splitting: precomputed per-search word sets match the old
// per-call splits — multi-word names/aliases, case folding, and stemming.
[Collection("Ported")]
public class SearchTermSplitTests
{
    private static (GameObject room, Func<int, GameObject?> resolver) Setup()
    {
        var room = GameObject.Create("room");
        room.IsContainer = true;
        var sword = GameObject.Create("Long Sword");
        sword.Aliases = ["Big Blade"];
        var shield = GameObject.Create("shield");
        room.AddContent(sword.Id);
        room.AddContent(shield.Id);
        var dict = new Dictionary<int, GameObject> { [sword.Id] = sword, [shield.Id] = shield };
        GameObject? R(int id) => dict.TryGetValue(id, out var o) ? o : null;
        return (room, R);
    }

    [Fact]
    public void EachWord_OfMultiwordName_Matches()
    {
        var (room, R) = Setup();
        Assert.Single(ContentUtils.Search(room, "long", R));
        Assert.Single(ContentUtils.Search(room, "sword", R));
    }

    [Fact]
    public void EachWord_OfMultiwordAlias_Matches()
    {
        var (room, R) = Setup();
        Assert.Single(ContentUtils.Search(room, "big", R));
        Assert.Single(ContentUtils.Search(room, "blade", R));
    }

    [Fact]
    public void MultiwordQuery_RequiresAllWords()
    {
        var (room, R) = Setup();
        var both = ContentUtils.Search(room, "long sword", R);
        Assert.Single(both);
        Assert.Empty(ContentUtils.Search(room, "long shield", R));
    }

    [Fact]
    public void Lookup_IsCaseInsensitive()
    {
        var (room, R) = Setup();
        Assert.Single(ContentUtils.Search(room, "SWORD", R));
        Assert.Single(ContentUtils.Search(room, "Blade", R));
    }
}
