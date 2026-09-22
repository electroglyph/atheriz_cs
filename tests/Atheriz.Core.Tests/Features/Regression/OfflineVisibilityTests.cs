using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Regression;

// Offline PCs are invisible to regular players (no name, no Someone
// placeholder in room lists, unresolvable by search/examine); builders
// and above see them as "Name (offline)" and can examine them.
[Collection("Ported")]
public sealed class OfflineVisibilityTests
{
    private static (Node node, GameObject hero, GameObject viewer, GameObject builder) SetupRoom()
    {
        var node = new Node(new Coord("test", 0, 0, 0));
        ObjectRegistry.AddObject(node);
        var hero = GameObject.Create("hero", isPc: true);
        hero.IsConnected = false;
        PlaceIn(node, hero);
        var viewer = GameObject.Create("viewer", isPc: true);
        viewer.IsConnected = true;
        PlaceIn(node, viewer);
        var builder = GameObject.Create("builder", isPc: true, privilege: Privilege.Builder);
        builder.IsConnected = true;
        PlaceIn(node, builder);
        return (node, hero, viewer, builder);
    }

    private static void PlaceIn(Node node, GameObject o)
    {
        ObjectRegistry.AddObject(o);
        o.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(node.Coord);
        node.AddObject(o);
    }

    [Fact]
    public void RoomList_HidesOfflinePcFromRegular()
    {
        using var env = GlobalTestEnv.Enter();
        var (node, _, viewer, _) = SetupRoom();
        var chars = node.GetDisplayCharacters(viewer);
        Assert.DoesNotContain("hero", chars, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Someone", chars, StringComparison.Ordinal);
    }

    [Fact]
    public void RoomList_ShowsOfflinePcToBuilder()
    {
        using var env = GlobalTestEnv.Enter();
        var (node, _, _, builder) = SetupRoom();
        var chars = node.GetDisplayCharacters(builder);
        Assert.Contains("hero (offline)", chars, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_RegularCannotResolveOfflinePc_BuilderCan()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, hero, viewer, builder) = SetupRoom();
        Assert.Empty(CommandHelpers.SearchWithFallback(viewer, "hero"));
        Assert.Empty(CommandHelpers.SearchWithFallback(viewer, $"#{hero.Id}"));
        Assert.Contains(hero, CommandHelpers.SearchWithFallback(builder, "hero"));
        Assert.Contains(hero, CommandHelpers.SearchWithFallback(builder, $"#{hero.Id}"));
    }

    [Fact]
    public void AtLook_RegularDeniedWithoutName_BuilderSeesAppearance()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, hero, viewer, builder) = SetupRoom();
        var denied = viewer.AtLook(hero);
        Assert.StartsWith("You can't look at", denied, StringComparison.Ordinal);
        Assert.DoesNotContain("hero", denied, StringComparison.OrdinalIgnoreCase);
        var seen = builder.AtLook(hero);
        Assert.Contains("hero (offline)", seen, StringComparison.Ordinal);
    }
}
