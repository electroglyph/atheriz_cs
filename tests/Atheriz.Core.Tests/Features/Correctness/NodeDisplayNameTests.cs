// Pins for the node display name: builders see "{name} {coord}", everyone
// else sees "{name}" where the coord line used to be (coord fallback when
// no custom name is set).
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class NodeDisplayNameTests
{
    private static string StripAnsi(string input) =>
        System.Text.RegularExpressions.Regex.Replace(input, @"\x1b\[[0-9;]*m", "");

    private static Node Room(string area, string? displayName = null)
    {
        var room = new Node(new Coord(area, 0, 0, 0));
        if (displayName is not null) room.DisplayName = displayName;
        return room;
    }

    private static GameObject Looker(Privilege privilege) =>
        GameObject.Create("looker-" + Guid.NewGuid().ToString("N"), isPc: true, privilege: privilege);

    [Fact]
    public void DisplayName_DefaultsToEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        Assert.Equal("", new Node(new Coord("disp", 0, 0, 0)).DisplayName);
    }

    [Fact]
    public void GetDisplayName_NonBuilder_SeesDisplayName()
    {
        using var env = GlobalTestEnv.Enter();
        var room = Room("disp", "Grand Hall");
        Assert.Equal("Grand Hall\n", room.GetDisplayName(Looker(Privilege.Guest)));
    }

    [Fact]
    public void GetDisplayName_NonBuilder_FallsBackToCoord()
    {
        using var env = GlobalTestEnv.Enter();
        var room = Room("disp");
        Assert.Equal(room.Coord.ToString() + "\n", room.GetDisplayName(Looker(Privilege.Guest)));
    }

    [Fact]
    public void GetDisplayName_NullLooker_SeesEffectiveName()
    {
        using var env = GlobalTestEnv.Enter();
        var room = Room("disp", "Grand Hall");
        Assert.Equal("Grand Hall\n", room.GetDisplayName(null));
        Assert.Equal(Room("disp2").Coord.ToString() + "\n", Room("disp2").GetDisplayName(null));
    }

    [Fact]
    public void GetDisplayName_Builder_SeesNamePlusCoord()
    {
        using var env = GlobalTestEnv.Enter();
        var room = Room("disp", "Grand Hall");
        var stripped = StripAnsi(room.GetDisplayName(Looker(Privilege.Builder)));
        Assert.Equal($"Grand Hall ({room.Coord.Area},{room.Coord.X},{room.Coord.Y},{room.Coord.Z})\n", stripped);
    }

    [Fact]
    public void GetDisplayName_BuilderNoDisplayName_SeesCoordLine()
    {
        using var env = GlobalTestEnv.Enter();
        var room = Room("disp");
        var stripped = StripAnsi(room.GetDisplayName(Looker(Privilege.Admin)));
        Assert.Equal($"({room.Coord.Area},{room.Coord.X},{room.Coord.Y},{room.Coord.Z})\n", stripped);
    }

    [Fact]
    public void ReturnAppearance_NonBuilder_ShowsRoomName()
    {
        using var env = GlobalTestEnv.Enter();
        var room = new Node(new Coord("disp", 0, 0, 0), desc: "A plain room.") { DisplayName = "Grand Hall" };
        ObjectRegistry.AddObject(room);
        var looker = Looker(Privilege.Guest);
        ObjectRegistry.AddObject(looker);
        var appearance = room.ReturnAppearance(looker);
        Assert.Contains("Grand Hall\n", appearance);
        Assert.Contains("A plain room.", appearance);
        Assert.DoesNotContain(room.Coord.ToString(), appearance);
    }
}
