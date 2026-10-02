using Atheriz.Core.Commands;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// The collapsed session lookup renders identical listings for every caller
// shape: a session-less puppet reads (null ?? 80) - 2 = 78, exactly like a
// default-width session, and the box width stays TermWidth (tw + 2) with
// the below-20 clamp.
[Collection("Ported")]
public sealed class UnloggedHelpWidthTests
{
    private static string RunHelp(GameObject caller)
    {
        caller.ClearMessages();
        new HelpCommand().Run(caller, "");
        return string.Join("\n", caller.PeekMessages());
    }

    [Fact]
    public void SessionlessPuppet_MatchesDefaultWidthSession()
    {
        var bare = new GameObject { Name = "Hero" };
        var seated = new GameObject { Name = "Hero" };
        seated.Session = new Session { TermWidth = 80, ScreenReader = false };
        Assert.Equal(RunHelp(seated), RunHelp(bare));
    }

    [Fact]
    public void TableWidth_EqualsTermWidth()
    {
        // TermWidth 40 -> tw = 38 -> Format width 40 -> every box line is
        // exactly 40 wide. Pins the -2/+2 arithmetic exactly (dropping
        // either changes the run).
        var puppet = new GameObject { Name = "Hero" };
        puppet.Session = new Session { TermWidth = 40, ScreenReader = false };
        var output = RunHelp(puppet);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Only box lines carry the width; the trailing hint line
        // ("Help <command> ...") is exempt.
        var box = lines.Where(l => l.Length > 0 && "╭╰│".Contains(l[0])).ToList();
        Assert.NotEmpty(box);
        Assert.All(box, l => Assert.Equal(40, l.Length));
    }

    [Fact]
    public void NarrowSession_ClampsWidth()
    {
        // TermWidth 10 -> tw collapses to 20 -> width-22 boxes, which must
        // differ from the width-80 render.
        var narrow = new GameObject { Name = "Hero" };
        narrow.Session = new Session { TermWidth = 10, ScreenReader = false };
        var wide = new GameObject { Name = "Hero" };
        wide.Session = new Session { TermWidth = 80, ScreenReader = false };
        Assert.NotEqual(RunHelp(wide), RunHelp(narrow));
        var lines = RunHelp(narrow).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var box = lines.Where(l => l.Length > 0 && "╭╰│".Contains(l[0])).ToList();
        Assert.NotEmpty(box);
        Assert.All(box, l => Assert.Equal(22, l.Length));
    }

    [Fact]
    public void FullListing_EndsWithHint()
    {
        // Bare help closes with a blank line plus the per-command pointer.
        var puppet = new GameObject { Name = "Hero" };
        puppet.Session = new Session { TermWidth = 80, ScreenReader = false };
        Assert.EndsWith("\nHelp <command> for more information.", RunHelp(puppet));
    }
}
