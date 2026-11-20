// dig: a builder-only single-room carve. Any direction always digs,
// the new ring copies the neighboring wall style (and color),
// and the resolved glyphs must enclose the room and join existing walls.
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class DigCommandTests
{
    private static (NodeHandler nh, MapHandler mh, Node start, GameObject caller) SetupWalled(
        string area, string wallStyle = "single", bool stampRing = true)
    {
        var nh = GlobalServices.GetNodeHandler();
        var mh = GlobalServices.GetMapHandler();
        try { nh.RemoveArea(area); } catch { }
        var nodeArea = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var start = new Node(new Coord(area, 0, 0, 0), desc: "Start");
        grid.AddNode(start);
        ObjectRegistry.AddObject(start);
        nodeArea.AddGrid(grid);
        nh.AddArea(nodeArea);
        NodeHandler.SetCurrent(nh);
        var sset = AtherizSettings.Global;
        string wall = wallStyle switch
        {
            "double" => sset.DoubleWallPlaceholder,
            "rounded" => sset.RoundedWallPlaceholder,
            _ => sset.SingleWallPlaceholder,
        };
        var mi = mh.EnsureMapInfo(area, 0);
        using (mi.BatchUpdate())
        {
            mi.UpdateGrid((0, 0), sset.RoomPlaceholder);
            if (stampRing) mi.PlaceWalls((0, 0), wall);
        }
        var caller = GameObject.Create("Digger", isPc: true);
        caller.PrivilegeLevel = Privilege.Builder;
        ObjectRegistry.AddObject(caller);
        caller.Location = new LocationRef.CoordLocation(start.Coord);
        start.AddObject(caller);
        caller.ClearMessages();
        return (nh, mh, start, caller);
    }

    private static string Joined(GameObject caller) => string.Join("\n", caller.PeekMessages());

    [Fact]
    public void Dig_HelpListsUnderBuildingBox()
    {
        // Behavioral pin for the category assignment: the rendered
        // overview shows dig inside the Building box next to Admin.
        Command[] cmds = [new ReloadCommand(), new DigCommand()];
        string boxes = HelpFormatter.Format(cmds, screenreader: false, termWidth: 80);
        var lines = boxes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int buildTop = Array.FindIndex(lines, l => l.Contains("─ Building "));
        Assert.True(buildTop >= 0);
        int buildBottom = Array.FindIndex(lines, buildTop, l => l.StartsWith("╰"));
        Assert.Contains(lines[buildTop..buildBottom], l => l.Contains("dig"));
    }

    [Fact]
    public void Dig_BareShowsHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, _, _, caller) = SetupWalled("dighelp");
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs([]));
        Assert.Contains("direction", Joined(caller));
    }

    [Fact]
    public void Dig_UnknownDirectionShowsHelpWithoutDigging()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, _, _, caller) = SetupWalled("digunknown");
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["sideways"]));
        Assert.Contains("direction", Joined(caller));
        Assert.Null(nh.GetNode(new Coord("digunknown", 1, 0, 0)));
    }

    [Fact]
    public void Dig_TwoDirectionsShowsHelpWithoutDigging()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, _, _, caller) = SetupWalled("digtwo");
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["n", "s"]));
        Assert.Contains("direction", Joined(caller));
        Assert.Null(nh.GetNode(new Coord("digtwo", 0, 1, 0)));
        Assert.Null(nh.GetNode(new Coord("digtwo", 0, -1, 0)));
    }

    [Fact]
    public void DigEast_CreatesRoomLinksMovesAndDescribes()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, _, start, caller) = SetupWalled("digeast");
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["east"]));
        var created = nh.GetNode(new Coord("digeast", 1, 0, 0));
        Assert.NotNull(created);
        Assert.Equal("A freshly dug area.", created!.Desc);
        Assert.Contains(start.GetLinks(), l => l.Name == "east" && l.Coord.Equals(created.Coord));
        Assert.Contains(start.GetLinks(), l => l.Name == "east" && l.Aliases.Contains("e"));
        Assert.Contains(created.GetLinks(), l => l.Name == "west" && l.Coord.Equals(start.Coord));
        Assert.Contains(created.GetLinks(), l => l.Name == "west" && l.Aliases.Contains("w"));
        Assert.Equal(created.Coord, ((LocationRef.CoordLocation)caller.Location).Coord);
        Assert.Contains("You dig east", Joined(caller));
    }

    [Theory]
    [InlineData("n", 0, 1, 0, "north", "south")]
    [InlineData("s", 0, -1, 0, "south", "north")]
    [InlineData("e", 1, 0, 0, "east", "west")]
    [InlineData("w", -1, 0, 0, "west", "east")]
    [InlineData("u", 0, 0, 1, "up", "down")]
    [InlineData("d", 0, 0, -1, "down", "up")]
    public void Dig_AllDirections_CreateLinkAndMove(string word, int dx, int dy, int dz, string link, string back)
    {
        using var env = GlobalTestEnv.Enter();
        string area = $"digall{word}";
        var (nh, _, start, caller) = SetupWalled(area);
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs([word]));
        var created = nh.GetNode(new Coord(area, dx, dy, dz));
        Assert.NotNull(created);
        Assert.Equal("A freshly dug area.", created!.Desc);
        Assert.Contains(start.GetLinks(), l => l.Name == link && l.Coord.Equals(created.Coord));
        Assert.Contains(created.GetLinks(), l => l.Name == back && l.Coord.Equals(start.Coord));
        Assert.Equal(created.Coord, ((LocationRef.CoordLocation)caller.Location).Coord);
    }

    [Theory]
    [InlineData("-e", 1, 0, 0)]
    [InlineData("--west", -1, 0, 0)]
    [InlineData("North", 0, 1, 0)]
    [InlineData("--south", 0, -1, 0)]
    [InlineData("UP", 0, 0, 1)]
    [InlineData("-d", 0, 0, -1)]
    public void Dig_DashLongAndCaseForms_ReachTheSameRoom(string word, int dx, int dy, int dz)
    {
        using var env = GlobalTestEnv.Enter();
        string area = $"digform{word.TrimStart('-').ToLowerInvariant()}";
        var (nh, _, _, caller) = SetupWalled(area);
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs([word]));
        var created = nh.GetNode(new Coord(area, dx, dy, dz));
        Assert.NotNull(created);
        Assert.Equal(created!.Coord, ((LocationRef.CoordLocation)caller.Location).Coord);
    }

    [Fact]
    public void Dig_OccupiedTargetWithoutExit_LinksMovesAndKeepsDesc()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, _, start, caller) = SetupWalled("digoccupied");
        var grid = nh.GetArea("digoccupied")!.GetGrid(0)!;
        var east = new Node(new Coord("digoccupied", 1, 0, 0), desc: "East");
        grid.AddNode(east);
        ObjectRegistry.AddObject(east);
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["e"]));
        Assert.Contains("you dig east", Joined(caller).ToLowerInvariant());
        Assert.Equal(new Coord("digoccupied", 1, 0, 0), ((LocationRef.CoordLocation)caller.Location).Coord);
        Assert.Contains(start.GetLinks(), l => l.Name == "east" && l.Coord.Equals(east.Coord));
        Assert.Contains(east.GetLinks(), l => l.Name == "west" && l.Coord.Equals(start.Coord));
        Assert.Equal("East", east.Desc);
        Assert.NotNull(nh.GetNode(new Coord("digoccupied", 1, 0, 0)));
    }

    [Fact]
    public void Dig_OccupiedTargetWithExit_RefusesWithoutMoving()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, _, start, caller) = SetupWalled("digoccupiedexit");
        var grid = nh.GetArea("digoccupiedexit")!.GetGrid(0)!;
        var east = new Node(new Coord("digoccupiedexit", 1, 0, 0), desc: "East");
        grid.AddNode(east);
        ObjectRegistry.AddObject(east);
        start.AddLinkIfAbsent("east", () => new NodeLink("east", east.Coord, ["e"]));
        east.AddLinkIfAbsent("west", () => new NodeLink("west", start.Coord, ["w"]));
        caller.ClearMessages();
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["e"]));
        Assert.Contains("already a room", Joined(caller).ToLowerInvariant());
        Assert.Equal(start.Coord, ((LocationRef.CoordLocation)caller.Location).Coord);
    }

    [Fact]
    public void Dig_VoidDigsWithoutRefusal()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, _, start, caller) = SetupWalled("digvoid", stampRing: false);
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["e"]));
        Assert.Contains("you dig east", Joined(caller).ToLowerInvariant());
        Assert.NotNull(nh.GetNode(new Coord("digvoid", 1, 0, 0)));
        Assert.Equal(new Coord("digvoid", 1, 0, 0), ((LocationRef.CoordLocation)caller.Location).Coord);
    }

    [Fact]
    public void Dig_NoLocationReportsValidLocation()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, _, _, caller) = SetupWalled("dignoloc");
        caller.Location = LocationRef.NullLocation.Instance;
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["e"]));
        Assert.Contains("valid location", Joined(caller).ToLowerInvariant());
    }

    [Fact]
    public void Dig_AccessRequiresBuilder()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new DigCommand();
        var builder = GameObject.Create("DigBuilder", isPc: true);
        builder.PrivilegeLevel = Privilege.Builder;
        Assert.True(cmd.Access(builder));
        var player = GameObject.Create("DigPlayer", isPc: true);
        player.PrivilegeLevel = Privilege.Player;
        Assert.False(cmd.Access(player));
    }

    [Fact]
    public void DigEast_SingleWalls_EncloseAndJoin()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, mh, _, caller) = SetupWalled("digsingle", "single");
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["e"]));
        var mi = mh.EnsureMapInfo("digsingle", 0);
        var post = mi.PostGrid;
        // New room renders open, ring resolves to single box art.
        Assert.Equal(" ", post[(0, 0)]);
        Assert.Equal(" ", post[(1, 0)]);
        Assert.Equal("┌", post[(-1, 1)]);
        Assert.Equal("─", post[(0, 1)]);
        Assert.Equal("─", post[(1, 1)]);
        Assert.Equal("┐", post[(2, 1)]);
        Assert.Equal("│", post[(-1, 0)]);
        Assert.Equal("│", post[(2, 0)]);
        Assert.Equal("└", post[(-1, -1)]);
        Assert.Equal("─", post[(0, -1)]);
        Assert.Equal("─", post[(1, -1)]);
        Assert.Equal("┘", post[(2, -1)]);
        // No placeholder survives the render.
        var sset = AtherizSettings.Global;
        string[] placeholders = [sset.SingleWallPlaceholder, sset.DoubleWallPlaceholder, sset.RoundedWallPlaceholder, sset.RoomPlaceholder, sset.PathPlaceholder, sset.RoadPlaceholder];
        Assert.DoesNotContain(post.Values.Select(GameUtils.StripAnsi), v => placeholders.Contains(v));
        // The old room cell keeps its floor marker in pre-grid.
        Assert.Equal(sset.RoomPlaceholder, mi.PreGrid[(0, 0)]);
        Assert.Equal(sset.RoomPlaceholder, mi.PreGrid[(1, 0)]);
    }

    [Fact]
    public void DigEast_DoubleWalls_CopyDoubleStyle()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, mh, _, caller) = SetupWalled("digdouble", "double");
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["e"]));
        var post = mh.EnsureMapInfo("digdouble", 0).PostGrid;
        Assert.Equal("╔", post[(-1, 1)]);
        Assert.Equal("═", post[(0, 1)]);
        Assert.Equal("═", post[(1, 1)]);
        Assert.Equal("╗", post[(2, 1)]);
        Assert.Equal("║", post[(2, 0)]);
        Assert.Equal("╚", post[(-1, -1)]);
        Assert.Equal("╝", post[(2, -1)]);
    }

    [Fact]
    public void DigEast_RoundedWalls_CopyRoundedCorners()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, mh, _, caller) = SetupWalled("diground", "rounded");
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["e"]));
        var post = mh.EnsureMapInfo("diground", 0).PostGrid;
        Assert.Equal("╭", post[(-1, 1)]);
        Assert.Equal("─", post[(0, 1)]);
        Assert.Equal("╮", post[(2, 1)]);
        Assert.Equal("╰", post[(-1, -1)]);
        Assert.Equal("╯", post[(2, -1)]);
    }

    [Fact]
    public void DigEast_FloorCell_UsesModalStyle()
    {
        // The dug-through cell holds floor (room marker, no node): the
        // gate passes and the surrounding single walls set the style.
        using var env = GlobalTestEnv.Enter();
        var (nh, mh, _, caller) = SetupWalled("digfloor", "single");
        var mi = mh.EnsureMapInfo("digfloor", 0);
        mi.UpdateGrid((1, 0), AtherizSettings.Global.RoomPlaceholder);
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["e"]));
        Assert.NotNull(nh.GetNode(new Coord("digfloor", 1, 0, 0)));
        var post = mi.PostGrid;
        Assert.Equal("┌", post[(-1, 1)]);
        Assert.Equal("┐", post[(2, 1)]);
    }

    [Fact]
    public void DigEast_Twice_ExtendsTheJoinedRow()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, mh, _, caller) = SetupWalled("digchain", "single");
        var parser = new DigCommand().Parser!;
        new DigCommand().Run(caller, parser.ParseArgs(["e"]));
        caller.ClearMessages();
        new DigCommand().Run(caller, parser.ParseArgs(["e"]));
        Assert.NotNull(nh.GetNode(new Coord("digchain", 2, 0, 0)));
        var post = mh.EnsureMapInfo("digchain", 0).PostGrid;
        Assert.Equal("┌", post[(-1, 1)]);
        Assert.Equal("─", post[(0, 1)]);
        Assert.Equal("─", post[(1, 1)]);
        Assert.Equal("─", post[(2, 1)]);
        Assert.Equal("┐", post[(3, 1)]);
        Assert.Equal(" ", post[(2, 0)]);
    }

    [Fact]
    public void DigUp_FreshLevel_DefaultsToRounded()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, mh, _, caller) = SetupWalled("digup", "single");
        var before = mh.EnsureMapInfo("digup", 0).PreGrid.Count;
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["u"]));
        var created = nh.GetNode(new Coord("digup", 0, 0, 1));
        Assert.NotNull(created);
        Assert.Equal("A freshly dug area.", created!.Desc);
        Assert.Equal(created.Coord, ((LocationRef.CoordLocation)caller.Location).Coord);
        // Fresh level has no style to copy: single line, rounded corners.
        var miUp = mh.EnsureMapInfo("digup", 1);
        Assert.Equal(AtherizSettings.Global.RoundedWallPlaceholder, miUp.PreGrid[(1, 1)]);
        Assert.Equal("╮", GameUtils.StripAnsi(miUp.PostGrid[(1, 1)]));
        Assert.Equal("╭", GameUtils.StripAnsi(miUp.PostGrid[(-1, 1)]));
        // The current level is untouched.
        Assert.Equal(before, mh.EnsureMapInfo("digup", 0).PreGrid.Count);
    }

    [Fact]
    public void DigDown_ChainedFreshLevels_StayRounded()
    {
        // Digging down twice from a double-walled map: neither fresh
        // level has a style to copy, so both default to single line
        // with rounded corners instead of carrying the double style down.
        using var env = GlobalTestEnv.Enter();
        var (nh, mh, _, caller) = SetupWalled("digdownchain", "double");
        var parser = new DigCommand().Parser!;
        new DigCommand().Run(caller, parser.ParseArgs(["d"]));
        caller.ClearMessages();
        new DigCommand().Run(caller, parser.ParseArgs(["d"]));
        Assert.NotNull(nh.GetNode(new Coord("digdownchain", 0, 0, -2)));
        var sset = AtherizSettings.Global;
        Assert.Equal(sset.RoundedWallPlaceholder, mh.EnsureMapInfo("digdownchain", -1).PreGrid[(1, 1)]);
        Assert.Equal(sset.RoundedWallPlaceholder, mh.EnsureMapInfo("digdownchain", -2).PreGrid[(1, 1)]);
    }

    [Fact]
    public void DigDown_FreshLevel_DefaultsToRoundedBox()
    {
        // Digging down from a double-walled map into a style-less level
        // falls back to single line with rounded corners, resolving a
        // joined box, not one corner repeated.
        using var env = GlobalTestEnv.Enter();
        var (nh, mh, _, caller) = SetupWalled("digdowndouble", "double");
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["d"]));
        Assert.NotNull(nh.GetNode(new Coord("digdowndouble", 0, 0, -1)));
        var sset = AtherizSettings.Global;
        var miDown = mh.EnsureMapInfo("digdowndouble", -1);
        Assert.Equal(sset.RoundedWallPlaceholder, miDown.PreGrid[(1, 1)]);
        var post = miDown.PostGrid;
        Assert.Equal("╭", GameUtils.StripAnsi(post[(-1, 1)]));
        Assert.Equal("─", GameUtils.StripAnsi(post[(0, 1)]));
        Assert.Equal("╮", GameUtils.StripAnsi(post[(1, 1)]));
        Assert.Equal("│", GameUtils.StripAnsi(post[(-1, 0)]));
        Assert.Equal("│", GameUtils.StripAnsi(post[(1, 0)]));
        Assert.Equal("╰", GameUtils.StripAnsi(post[(-1, -1)]));
        Assert.Equal("─", GameUtils.StripAnsi(post[(0, -1)]));
        Assert.Equal("╯", GameUtils.StripAnsi(post[(1, -1)]));
    }

    [Fact]
    public void DigInto_ResolvedMazeGlyph_StampsPlaceholderBox()
    {
        // Maze levels store resolved art (╬╝║) in pre-grid. Digging into
        // one must stamp the style's placeholder so the new ring renders
        // corners and straights in the right places, not one glyph copy.
        using var env = GlobalTestEnv.Enter();
        var (nh, mh, _, caller) = SetupWalled("digmazeart", "single", stampRing: false);
        var mi = mh.EnsureMapInfo("digmazeart", 0);
        mi.UpdateGrid((0, 0), AtherizSettings.Global.RoomPlaceholder);
        mi.SetPreCell((1, 0), "╝");
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["e"]));
        Assert.NotNull(nh.GetNode(new Coord("digmazeart", 1, 0, 0)));
        Assert.Equal(AtherizSettings.Global.DoubleWallPlaceholder, mi.PreGrid[(2, 1)]);
        var post = mi.PostGrid;
        Assert.Equal("╗", GameUtils.StripAnsi(post[(2, 1)]));
        Assert.Equal("═", GameUtils.StripAnsi(post[(1, 1)]));
        Assert.Equal("║", GameUtils.StripAnsi(post[(2, 0)]));
    }

    [Fact]
    public void DigUp_CopiesTargetMapStyle()
    {
        using var env = GlobalTestEnv.Enter();
        var (nh, mh, _, caller) = SetupWalled("digupstyle", "single");
        // A double-walled level above with no room at the dig column.
        var sset = AtherizSettings.Global;
        var other = new Node(new Coord("digupstyle", 5, 5, 1), desc: "Other");
        var area = nh.GetArea("digupstyle")!;
        var grid1 = new NodeGrid("digupstyle", 1);
        grid1.AddNode(other);
        ObjectRegistry.AddObject(other);
        area.AddGrid(grid1);
        var miUp = mh.EnsureMapInfo("digupstyle", 1);
        using (miUp.BatchUpdate())
        {
            miUp.UpdateGrid((5, 5), sset.RoomPlaceholder);
            miUp.PlaceWalls((5, 5), sset.DoubleWallPlaceholder);
        }
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["u"]));
        Assert.NotNull(nh.GetNode(new Coord("digupstyle", 0, 0, 1)));
        Assert.Equal(sset.DoubleWallPlaceholder, mh.EnsureMapInfo("digupstyle", 1).PreGrid[(1, 1)]);
        Assert.Equal("╗", GameUtils.StripAnsi(mh.EnsureMapInfo("digupstyle", 1).PostGrid[(1, 1)]));
        // The seeded room on the target level survives.
        Assert.NotNull(nh.GetNode(new Coord("digupstyle", 5, 5, 1)));
    }

    [Fact]
    public void DigEast_ColoredWalls_CopyColorAndKeepJoining()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, mh, _, caller) = SetupWalled("digcolor", "single");
        var sset = AtherizSettings.Global;
        string wrapped = GameUtils.WrapRgb(sset.SingleWallPlaceholder, (200, 100, 50), (0, 0, 0));
        var mi = mh.EnsureMapInfo("digcolor", 0);
        foreach (var key in mi.PreGrid.Keys.ToList())
        {
            if (mi.PreGrid[key] == sset.SingleWallPlaceholder) mi.SetPreCell(key, wrapped);
        }
        string core = GameUtils.StripAnsi(wrapped);
        string prefix = wrapped[..wrapped.IndexOf(core, StringComparison.Ordinal)];
        string suffix = wrapped[(wrapped.IndexOf(core, StringComparison.Ordinal) + core.Length)..];
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["e"]));
        // The new ring carries the exact wrapper bytes.
        Assert.Equal(wrapped, mi.PreGrid[(2, 1)]);
        Assert.Equal(wrapped, mi.PreGrid[(2, 0)]);
        // ...resolves to the same joined glyphs, wrapped.
        Assert.Equal(prefix + "┐" + suffix, mi.PostGrid[(2, 1)]);
        Assert.Equal("┌", GameUtils.StripAnsi(mi.PostGrid[(-1, 1)]));
        Assert.Equal("─", GameUtils.StripAnsi(mi.PostGrid[(0, 1)]));
        // A later full re-render keeps the color (placeholders stay
        // placeholders, so the wrapper survives PreRender).
        mi.PreRender();
        Assert.Equal(prefix + "┐" + suffix, mi.PostGrid[(2, 1)]);
    }

    [Fact]
    public void DigEast_PostGridOnlyMap_AllowsDig()
    {
        // Generated levels can hold post-grid art with an empty pre-grid;
        // the gate consults the drawn cells in that case.
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler();
        var mh = GlobalServices.GetMapHandler();
        const string area = "digpostonly";
        try { nh.RemoveArea(area); } catch { }
        var nodeArea = new NodeArea(area);
        var grid = new NodeGrid(area, 0);
        var start = new Node(new Coord(area, 0, 0, 0), desc: "Start");
        grid.AddNode(start);
        ObjectRegistry.AddObject(start);
        nodeArea.AddGrid(grid);
        nh.AddArea(nodeArea);
        NodeHandler.SetCurrent(nh);
        var post = new Dictionary<(int, int), string> { [(0, 0)] = " ", [(1, 0)] = "─", [(2, 0)] = " " };
        mh.SetMapInfo(area, 0, new MapInfo(area, null, post));
        var caller = GameObject.Create("DigPost", isPc: true);
        caller.PrivilegeLevel = Privilege.Builder;
        ObjectRegistry.AddObject(caller);
        caller.Location = new LocationRef.CoordLocation(start.Coord);
        start.AddObject(caller);
        caller.ClearMessages();
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["e"]));
        Assert.NotNull(nh.GetNode(new Coord(area, 1, 0, 0)));
        Assert.Equal(new Coord(area, 1, 0, 0), ((LocationRef.CoordLocation)caller.Location).Coord);
    }

    [Fact]
    public void Dig_ClassifyDigCell_SortsPlaceholdersGlyphsAndOther()
    {
        var sset = AtherizSettings.Global;
        Assert.Equal("single", DigCommand.ClassifyDigCell(sset.SingleWallPlaceholder, sset));
        Assert.Equal("double", DigCommand.ClassifyDigCell(sset.DoubleWallPlaceholder, sset));
        Assert.Equal("rounded", DigCommand.ClassifyDigCell(sset.RoundedWallPlaceholder, sset));
        Assert.Equal("rounded", DigCommand.ClassifyDigCell(sset.PathPlaceholder, sset));
        Assert.Equal("double", DigCommand.ClassifyDigCell(sset.RoadPlaceholder, sset));
        Assert.Equal("floor", DigCommand.ClassifyDigCell(sset.RoomPlaceholder, sset));
        Assert.Equal("shared", DigCommand.ClassifyDigCell("─", sset));
        Assert.Equal("single", DigCommand.ClassifyDigCell("┌", sset));
        Assert.Equal("shared", DigCommand.ClassifyDigCell("┼", sset));
        Assert.Equal("double", DigCommand.ClassifyDigCell("╬", sset));
        Assert.Equal("rounded", DigCommand.ClassifyDigCell("╭", sset));
        Assert.Equal("other", DigCommand.ClassifyDigCell(" ", sset));
        Assert.Equal("other", DigCommand.ClassifyDigCell("X", sset));
        // Heavy door glyphs are not diggable walls.
        Assert.Equal("other", DigCommand.ClassifyDigCell("━", sset));
        Assert.Equal("other", DigCommand.ClassifyDigCell("┃", sset));
    }

    private static void RunJob(CommandDispatcher.Job? job)
    {
        Assert.NotNull(job);
        job!.Func(job.Caller, job.Args);
    }

    [Fact]
    public void DigDown_PlacesPairedStairMarkers()
    {
        using var env = GlobalTestEnv.Enter();
        const string area = "digstairs";
        var (nh, mh, _, caller) = SetupWalled(area, "single");
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["d"]));
        Assert.NotNull(nh.GetNode(new Coord(area, 0, 0, -1)));
        var fromLegend = mh.EnsureMapInfo(area, 0).LegendEntries;
        var down = Assert.Single(fromLegend, e => e.Desc == "down");
        Assert.True(down.Coord is (0, 0) && down.Symbol is string ds
            && DigCommand.DownMarkerGlyphs.Contains(GameUtils.StripAnsi(ds)));
        var toLegend = mh.EnsureMapInfo(area, -1).LegendEntries;
        var up = Assert.Single(toLegend, e => e.Desc == "up");
        Assert.True(up.Coord is (0, 0) && up.Symbol is string us
            && DigCommand.UpMarkerGlyphs.Contains(GameUtils.StripAnsi(us)));
    }

    [Fact]
    public void DigUp_PlacesMirroredStairMarkers()
    {
        using var env = GlobalTestEnv.Enter();
        const string area = "digupstairs";
        var (nh, mh, _, caller) = SetupWalled(area, "single");
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["u"]));
        Assert.NotNull(nh.GetNode(new Coord(area, 0, 0, 1)));
        var fromLegend = mh.EnsureMapInfo(area, 0).LegendEntries;
        var up = Assert.Single(fromLegend, e => e.Desc == "up");
        Assert.True(up.Coord is (0, 0) && up.Symbol is string us
            && DigCommand.UpMarkerGlyphs.Contains(GameUtils.StripAnsi(us)));
        var toLegend = mh.EnsureMapInfo(area, 1).LegendEntries;
        var down = Assert.Single(toLegend, e => e.Desc == "down");
        Assert.True(down.Coord is (0, 0) && down.Symbol is string ds
            && DigCommand.DownMarkerGlyphs.Contains(GameUtils.StripAnsi(ds)));
    }

    [Fact]
    public void DigDown_MarkersMatchEachMapWallColor()
    {
        using var env = GlobalTestEnv.Enter();
        const string area = "digstaircolor";
        var (_, mh, _, caller) = SetupWalled(area, "single");
        var sset = AtherizSettings.Global;
        string wrapA = GameUtils.WrapRgb(sset.SingleWallPlaceholder, (200, 100, 50), (0, 0, 0));
        var fromMi = mh.EnsureMapInfo(area, 0);
        foreach (var key in fromMi.PreGrid.Keys.ToList())
        {
            if (fromMi.PreGrid[key] == sset.SingleWallPlaceholder) fromMi.SetPreCell(key, wrapA);
        }
        string coreA = GameUtils.StripAnsi(wrapA);
        string prefixA = wrapA[..wrapA.IndexOf(coreA, StringComparison.Ordinal)];
        string suffixA = wrapA[(wrapA.IndexOf(coreA, StringComparison.Ordinal) + coreA.Length)..];
        // Target level below carries different double walls in another color.
        string wrapB = GameUtils.WrapRgb(sset.DoubleWallPlaceholder, (10, 200, 30), (1, 2, 3));
        var toMi = mh.EnsureMapInfo(area, -1);
        using (toMi.BatchUpdate())
        {
            toMi.UpdateGrid((5, 5), sset.RoomPlaceholder);
            toMi.PlaceWalls((5, 5), wrapB);
        }
        string coreB = GameUtils.StripAnsi(wrapB);
        string prefixB = wrapB[..wrapB.IndexOf(coreB, StringComparison.Ordinal)];
        string suffixB = wrapB[(wrapB.IndexOf(coreB, StringComparison.Ordinal) + coreB.Length)..];
        Assert.NotEqual(prefixA, prefixB);

        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["d"]));

        var down = Assert.Single(fromMi.LegendEntries, e => e.Desc == "down");
        Assert.True(down.Symbol is not null && down.Symbol.StartsWith(prefixA, StringComparison.Ordinal)
            && down.Symbol.EndsWith(suffixA, StringComparison.Ordinal));
        Assert.Contains(GameUtils.StripAnsi(down.Symbol!), DigCommand.DownMarkerGlyphs);
        var up = Assert.Single(toMi.LegendEntries, e => e.Desc == "up");
        Assert.True(up.Symbol is not null && up.Symbol.StartsWith(prefixB, StringComparison.Ordinal)
            && up.Symbol.EndsWith(suffixB, StringComparison.Ordinal));
        Assert.Contains(GameUtils.StripAnsi(up.Symbol!), DigCommand.UpMarkerGlyphs);
    }

    [Fact]
    public void DigDown_FreshMaps_MarkersBare()
    {
        using var env = GlobalTestEnv.Enter();
        const string area = "digstairbare";
        var (_, mh, _, caller) = SetupWalled(area, "single");
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["d"]));
        var down = Assert.Single(mh.EnsureMapInfo(area, 0).LegendEntries, e => e.Desc == "down");
        Assert.Equal(GameUtils.StripAnsi(down.Symbol!), down.Symbol);
        var up = Assert.Single(mh.EnsureMapInfo(area, -1).LegendEntries, e => e.Desc == "up");
        Assert.Equal(GameUtils.StripAnsi(up.Symbol!), up.Symbol);
    }

    [Fact]
    public void DigDown_MarkersDoNotBreakLaterDigs()
    {
        using var env = GlobalTestEnv.Enter();
        const string area = "digstairchain";
        var (nh, _, _, caller) = SetupWalled(area, "single");
        var parser = new DigCommand().Parser!;
        new DigCommand().Run(caller, parser.ParseArgs(["d"]));
        caller.ClearMessages();
        new DigCommand().Run(caller, parser.ParseArgs(["e"]));
        var created = nh.GetNode(new Coord(area, 1, 0, -1));
        Assert.NotNull(created);
        Assert.Equal(created!.Coord, ((LocationRef.CoordLocation)caller.Location).Coord);
        Assert.Contains(created.GetLinks(), l => l.Name == "west");
    }

    [Fact]
    public void AddStairMarker_PicksFromSetLabelsAndColors()
    {
        var mi = new MapInfo("stairunit");
        string placed = DigCommand.AddStairMarker(mi, (3, 4), "down", DigCommand.DownMarkerGlyphs);
        Assert.Contains(GameUtils.StripAnsi(placed), DigCommand.DownMarkerGlyphs);
        var entry = Assert.Single(mi.LegendEntries);
        Assert.Equal("down", entry.Desc);
        Assert.True(entry.Coord is (3, 4));
        Assert.Equal(placed, entry.Symbol);
        // With wrapped modal walls the marker takes the wrapper.
        var sset = AtherizSettings.Global;
        string wrapped = GameUtils.WrapRgb(sset.DoubleWallPlaceholder, (10, 200, 30), (1, 2, 3));
        mi.SetPreCell((0, 0), wrapped);
        string placed2 = DigCommand.AddStairMarker(mi, (5, 5), "up", DigCommand.UpMarkerGlyphs);
        string core = GameUtils.StripAnsi(wrapped);
        string prefix = wrapped[..wrapped.IndexOf(core, StringComparison.Ordinal)];
        string suffix = wrapped[(wrapped.IndexOf(core, StringComparison.Ordinal) + core.Length)..];
        Assert.StartsWith(prefix, placed2);
        Assert.EndsWith(suffix, placed2);
        Assert.Contains(GameUtils.StripAnsi(placed2), DigCommand.UpMarkerGlyphs);
        Assert.Throws<ArgumentException>(() => DigCommand.AddStairMarker(mi, (0, 0), "down", []));
    }

    [Fact]
    public void DigDown_TwoWaySpot_CollapsesToBidirectionalMarker()
    {
        using var env = GlobalTestEnv.Enter();
        const string area = "digstairtwoway";
        var (nh, mh, start, caller) = SetupWalled(area, "single");
        var parser = new DigCommand().Parser!;
        new DigCommand().Run(caller, parser.ParseArgs(["u"]));
        Assert.True(caller.MoveTo(start));
        caller.ClearMessages();
        new DigCommand().Run(caller, parser.ParseArgs(["d"]));
        Assert.NotNull(nh.GetNode(new Coord(area, 0, 0, -1)));
        var legend = mh.EnsureMapInfo(area, 0).LegendEntries;
        var combined = Assert.Single(legend);
        Assert.Equal("up, down", combined.Desc);
        Assert.True(combined.Coord is (0, 0));
        Assert.Contains(GameUtils.StripAnsi(combined.Symbol!), DigCommand.TwoWayMarkerGlyphs);
    }

    [Fact]
    public void DigDown_ChainedColumn_CollapsesMiddleLevel()
    {
        using var env = GlobalTestEnv.Enter();
        const string area = "digstairmid";
        var (nh, mh, _, caller) = SetupWalled(area, "single");
        var parser = new DigCommand().Parser!;
        new DigCommand().Run(caller, parser.ParseArgs(["d"]));
        caller.ClearMessages();
        new DigCommand().Run(caller, parser.ParseArgs(["d"]));
        Assert.NotNull(nh.GetNode(new Coord(area, 0, 0, -2)));
        var mid = mh.EnsureMapInfo(area, -1).LegendEntries;
        var combined = Assert.Single(mid);
        Assert.Equal("up, down", combined.Desc);
        Assert.Contains(GameUtils.StripAnsi(combined.Symbol!), DigCommand.TwoWayMarkerGlyphs);
        // End levels keep their single markers.
        Assert.Single(mh.EnsureMapInfo(area, 0).LegendEntries, e => e.Desc == "down");
        Assert.Single(mh.EnsureMapInfo(area, -2).LegendEntries, e => e.Desc == "up");
    }

    [Fact]
    public void AddStairMarker_SameSpotTwice_StaysSingle()
    {
        var mi = new MapInfo("stairstwice");
        string first = DigCommand.AddStairMarker(mi, (1, 1), "down", DigCommand.DownMarkerGlyphs);
        string second = DigCommand.AddStairMarker(mi, (1, 1), "down", DigCommand.DownMarkerGlyphs);
        Assert.Equal(first, second);
        Assert.Single(mi.LegendEntries);
    }

    [Fact]
    public void DigDown_RoomWithUpExitButNoMarker_PlacesBidirectional()
    {
        // Stairs built with `build -u` leave exits but no legend markers:
        // digging back down still lands on an up/down tile.
        using var env = GlobalTestEnv.Enter();
        const string area = "digstairexit";
        var (nh, mh, start, caller) = SetupWalled(area, "single");
        var buildPa = new GameArgumentParser.ParsedArgs();
        buildPa["n"] = false; buildPa["e"] = false; buildPa["s"] = false; buildPa["w"] = false;
        buildPa["u"] = true; buildPa["d"] = false; buildPa["x"] = false;
        buildPa["room"] = true; buildPa["road"] = false; buildPa["path"] = false;
        buildPa["desc"] = null; buildPa["single"] = false; buildPa["double"] = false;
        buildPa["round"] = false; buildPa["none"] = false;
        new BuildCommand().Run(caller, buildPa);
        Assert.True(caller.MoveTo(start));
        caller.ClearMessages();
        Assert.Empty(mh.EnsureMapInfo(area, 0).LegendEntries);
        new DigCommand().Run(caller, new DigCommand().Parser!.ParseArgs(["d"]));
        Assert.NotNull(nh.GetNode(new Coord(area, 0, 0, -1)));
        var legend = mh.EnsureMapInfo(area, 0).LegendEntries;
        var combined = Assert.Single(legend);
        Assert.Equal("up, down", combined.Desc);
        Assert.True(combined.Coord is (0, 0));
        Assert.Contains(GameUtils.StripAnsi(combined.Symbol!), DigCommand.TwoWayMarkerGlyphs);
        var down = mh.EnsureMapInfo(area, -1).LegendEntries;
        Assert.Single(down, e => e.Desc == "up");
    }

    [Fact]
    public void Dig_DispatchedEndToEnd_DigsMovesAndAnnounces()
    {
        using var env = GlobalTestEnv.Enter();
        CommandRegistry.Reset();
        var _ = CommandRegistry.LoggedIn;
        CommandDispatcher.SetSettings(new AtherizSettings { AutoCommandAliasing = true });
        try
        {
            var (nh, _, _, caller) = SetupWalled("digdispatch");
            RunJob(CommandDispatcher.DispatchLoggedIn(caller, "dig east", immediate: true));
            var created = nh.GetNode(new Coord("digdispatch", 1, 0, 0));
            Assert.NotNull(created);
            Assert.Equal(created!.Coord, ((LocationRef.CoordLocation)caller.Location).Coord);
            Assert.Contains("You dig east", Joined(caller));
        }
        finally
        {
            CommandDispatcher.SetSettings(new AtherizSettings());
            CommandRegistry.Reset();
        }
    }
}
