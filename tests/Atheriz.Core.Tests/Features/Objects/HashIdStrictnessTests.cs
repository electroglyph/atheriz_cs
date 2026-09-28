using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Container `#id` search accepts only plain-digit references: a sign or
// whitespace between `#` and the digits resolves nothing, matching the shared
// id parser the other verbs use.
[Collection("Ported")]
public class HashIdStrictnessTests
{
    private static (GameObject room, GameObject coin, Func<int, GameObject?> resolver) Setup()
    {
        var room = GameObject.Create("room");
        room.IsContainer = true;
        var coin = GameObject.Create("coin");
        room.AddContent(coin.Id);
        var dict = new Dictionary<int, GameObject> { [coin.Id] = coin };
        GameObject? R(int id) => dict.TryGetValue(id, out var o) ? o : null;
        return (room, coin, R);
    }

    [Fact]
    public void HashId_PlainDigits_Resolves()
    {
        var (room, coin, R) = Setup();
        var found = ContentUtils.Search(room, $"#{coin.Id}", R);
        Assert.Single(found);
        Assert.Same(coin, found[0]);
    }

    [Fact]
    public void HashId_WithSignOrWhitespace_ResolvesNothing()
    {
        var (room, coin, R) = Setup();
        Assert.Empty(ContentUtils.Search(room, $"#+{coin.Id}", R));
        Assert.Empty(ContentUtils.Search(room, $"# {coin.Id}", R));
        Assert.Empty(ContentUtils.Search(room, $"#\t{coin.Id}", R));
    }
}
