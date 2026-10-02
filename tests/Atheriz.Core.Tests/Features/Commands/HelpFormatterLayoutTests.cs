using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// The help overview renders each category inside a box-drawing border
// (two independent columns; one empty line between boxes on the same
// side); the screenreader list stays a plain borderless table byte-for-byte.
[Collection("Ported")]
public sealed class HelpFormatterLayoutTests
{
    private sealed class LongKeyCommand : Command
    {
        public override string Key => "superlongcommandkey";
        public override string Desc => "Long key.";
        public override string Category => "General";
        public override void Run(IMessageTarget caller, object? args) { }
    }

    private sealed class CommsCommand : Command
    {
        public override string Key => "chat";
        public override string Desc => "Chat.";
        public override string Category => "Communication";
        public override void Run(IMessageTarget caller, object? args) { }
    }

    private sealed class TinyCommand(string key) : Command
    {
        public override string Key => key;
        public override string Desc => "Tiny.";
        public override string Category => "General";
        public override void Run(IMessageTarget caller, object? args) { }
    }

    private sealed class LoneCommand : Command
    {
        public override string Key => "save";
        public override string Desc => "Save.";
        public override string Category => "Aaa";
        public override void Run(IMessageTarget caller, object? args) { }
    }

    private sealed class SoloCommand(string key, string category) : Command
    {
        public override string Key => key;
        public override string Desc => "Solo.";
        public override string Category => category;
        public override void Run(IMessageTarget caller, object? args) { }
    }

    [Fact]
    public void HelpFormatter_LongList_WrapsAcrossLines()
    {
        Command[] cmds = [new TinyCommand("alpha"), new TinyCommand("beta"), new TinyCommand("delta"), new TinyCommand("gamma"), new OpenCommand(), new CloseCommand()];
        string boxes = HelpFormatter.Format(cmds, screenreader: false, termWidth: 80);
        // Inner width is 35: the sorted names wrap after "gamma".
        Assert.Contains("│ alpha, beta, close, delta, gamma    │", boxes);
        var content = boxes.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.StartsWith("│")).ToList();
        Assert.True(content.Count >= 2);
    }

    [Fact]
    public void HelpFormatter_SingleBox_PinnedByteIdentical()
    {
        // Narrow terminal => single column, box exactly the full width.
        Command[] cmds = [new OpenCommand()];
        Assert.Equal(
            "╭─ General ──────────────────╮\n" +
            "│ open                       │\n" +
            "╰────────────────────────────╯\n",
            HelpFormatter.Format(cmds, screenreader: false, termWidth: 30));
    }

    [Fact]
    public void HelpFormatter_Boxes_PairSideBySide_WrapAndDropDescriptions()
    {
        Command[] cmds = [new OpenCommand(), new CloseCommand(), new CommsCommand()];
        string boxes = HelpFormatter.Format(cmds, screenreader: false, termWidth: 80);
        var lines = boxes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Both category titles share the first row, side by side.
        Assert.Contains("╭─ General", lines[0]);
        Assert.Contains("╭─ Communication", lines[0]);
        // Command names are comma-separated; descriptions are gone from
        // the overview (help <command> still shows them).
        Assert.Contains("close, open", boxes);
        Assert.DoesNotContain("Description", boxes);
        Assert.DoesNotContain("Open doors.", boxes);
        // Every row is exactly the terminal width: two equal boxes + gap.
        Assert.All(lines, l => Assert.Equal(80, l.Length));
    }

    [Fact]
    public void HelpFormatter_SameSideBoxes_SeparatedBySingleEmptyLine()
    {
        // Five groups deal Aaa/Ccc/Eee down the left column: between one
        // box's bottom border and the next box's top on that side sits
        // exactly one empty line — never padding rows, never stray │.
        Command[] cmds = [
            new SoloCommand("akey", "Aaa"), new SoloCommand("bkey", "Bbb"),
            new SoloCommand("ckey", "Ccc"), new SoloCommand("dkey", "Ddd"),
            new SoloCommand("ekey", "Eee"),
        ];
        string boxes = HelpFormatter.Format(cmds, screenreader: false, termWidth: 80);
        var lines = boxes.Split('\n');
        Assert.Equal("", lines[^1]);
        var inner = lines[..^1];
        Assert.All(inner, l => Assert.Equal(80, l.Length));
        // Left cell (39 wide): exactly two blank separators, each between
        // a ╯ above and a ╭ below.
        var blanks = inner.Where((l, i) => string.IsNullOrWhiteSpace(l[..39])).ToList();
        Assert.Equal(2, blanks.Count);
        foreach (var blank in blanks)
        {
            int i = Array.IndexOf(inner, blank);
            Assert.Contains("╯", inner[i - 1][..39]);
            Assert.Contains("╭", inner[i + 1][..39]);
        }
        // Every left-cell bordered line carries content — no filler │.
        Assert.All(inner.Where(l => l.StartsWith("│")), l => Assert.True(l[1..38].Trim().Length > 0));
    }

    [Fact]
    public void HelpFormatter_GroupsReloadUnderAdminBox_MapeditUnderBuildingBox()
    {
        // Behavioral pin for the category assignments: the rendered
        // overview must show reload inside the Admin box and mapedit
        // inside the Building box (row 1 pairs Admin left, Building
        // right at width 80 — box 39, gap 2).
        Command[] cmds = [new ReloadCommand(), new DrawCommand(), new OpenCommand()];
        string boxes = HelpFormatter.Format(cmds, screenreader: false, termWidth: 80);
        var lines = boxes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int adminTop = Array.FindIndex(lines, l => l.Contains("─ Admin "));
        int adminBottom = Array.FindIndex(lines, adminTop, l => l.StartsWith("╰"));
        Assert.True(adminTop >= 0 && adminBottom > adminTop);
        Assert.Contains(lines[adminTop..adminBottom], l => l[..39].Contains("reload"));
        int buildTop = Array.FindIndex(lines, l => l.Contains("─ Building "));
        Assert.Equal(adminTop, buildTop);
        Assert.Contains(lines[buildTop..adminBottom], l => l[41..].Contains("mapedit"));
    }

    [Fact]
    public void HelpFormatter_OddBoxOut_StandsAlone_NoEmptyBoxes()
    {
        Command[] cmds = [new OpenCommand()];
        string boxes = HelpFormatter.Format(cmds, screenreader: false, termWidth: 80);
        Assert.Equal(1, boxes.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(l => l.StartsWith("╭")));
        Assert.DoesNotContain("│  │", boxes);
        Assert.Equal("", HelpFormatter.Format([], screenreader: false, termWidth: 80));
    }

    [Fact]
    public void HelpFormatter_LongKey_TruncatesToProtectBorder()
    {
        Command[] cmds = [new LongKeyCommand(), new OpenCommand()];
        string boxes = HelpFormatter.Format(cmds, screenreader: false, termWidth: 30);
        // Inner width is 26: the 19-char key fits, so nothing truncates here...
        Assert.Contains("superlongcommandkey", boxes);
        string tight = HelpFormatter.Format(cmds, screenreader: false, termWidth: 20);
        Assert.DoesNotContain("superlongcommandkey", tight);
        Assert.Contains("…", tight);
        Assert.All(tight.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            l => Assert.Equal(20, l.Length));
    }

    [Fact]
    public void HelpFormatter_ScreenreaderList_Untouched()
    {
        Command[] screenreaderCmds = [new OpenCommand(), new CloseCommand()];
        Assert.Equal(
            "General      close        Close doors.\n" +
            "General      open         Open doors.\n",
            HelpFormatter.Format(screenreaderCmds, screenreader: true, termWidth: 80));
        Command[] cmds = [new LongKeyCommand(), new OpenCommand()];
        string list = HelpFormatter.Format(cmds, screenreader: true, termWidth: 80);
        var listRows = list.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var openListRow = listRows.First(l => l.Contains("Open doors."));
        var longListRow = listRows.First(l => l.Contains("superlongcommandkey"));
        Assert.Equal(
            openListRow.IndexOf("Open doors.", StringComparison.Ordinal),
            longListRow.IndexOf("Long key.", StringComparison.Ordinal));
    }
}
