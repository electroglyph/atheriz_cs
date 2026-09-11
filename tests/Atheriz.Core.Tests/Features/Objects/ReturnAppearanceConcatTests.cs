// Pins for ReturnAppearance building via Concat (Node.Links.cs): the normal
// appearance is exactly the part concatenation, and placeholder tokens inside
// user-controlled text emit literally instead of being re-replaced.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class ReturnAppearanceConcatTests
{
    private static string ConcatParts(Node room, GameObject looker) =>
        string.Concat(room.GetDisplayName(looker), room.GetDisplayDesc(looker), room.GetDisplayDoors(looker), room.GetDisplayExits(looker), room.GetDisplayCharacters(looker), room.GetDisplayThings(looker));

    [Fact]
    public void ReturnAppearance_NormalAppearance_EqualsConcatOfParts()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("appear", 0, 0, 0), desc: "A plain room.");
        ObjectRegistry.AddObject(room);
        room.AddLink(new NodeLink("north", new Coord("appear", 0, 1, 0)));
        var bob = GameObject.Create("Bob", isNpc: true);
        ObjectRegistry.AddObject(bob);
        room.AddObject(bob);
        var coin = GameObject.Create("coin", isItem: true);
        ObjectRegistry.AddObject(coin);
        room.AddObject(coin);
        var looker = GameObject.Create("looker");
        ObjectRegistry.AddObject(looker);

        string appearance = room.ReturnAppearance(looker);

        Assert.Equal(ConcatParts(room, looker), appearance);
        Assert.Contains("A plain room.", appearance);
        Assert.Contains("north", appearance);
        Assert.Contains("Bob", appearance);
        Assert.Contains("coin", appearance);
    }

    [Fact]
    public void ReturnAppearance_PlaceholderTokenInName_EmittedLiterally()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("appeartoken", 0, 0, 0), desc: "A hall marked {exits} in paint.");
        ObjectRegistry.AddObject(room);
        var token = GameObject.Create("{desc}", isItem: true);
        ObjectRegistry.AddObject(token);
        room.AddObject(token);
        var looker = GameObject.Create("looker");
        ObjectRegistry.AddObject(looker);

        string appearance = room.ReturnAppearance(looker);

        Assert.Equal(ConcatParts(room, looker), appearance);
        Assert.Contains("{desc}", appearance);
        Assert.Contains("{exits}", appearance);
    }

    [Fact]
    public void ReturnAppearance_NullLooker_SeesNothing()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("appearnull", 0, 0, 0), desc: "A plain room.");
        ObjectRegistry.AddObject(room);

        Assert.Equal("You see nothing here.", room.ReturnAppearance(null));
    }
}
