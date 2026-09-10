using Atheriz.Core.Commands;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// The collapsed session lookup renders identical tables for every caller
// shape: a session-less puppet reads (null ?? 80) - 2 = 78, exactly like a
// default-width session, and the table width stays TermWidth (tw + 2) with
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
        // TermWidth 40 -> tw = 38 -> Format width 40 -> 38-dash separator.
        // Pins the -2/+2 arithmetic exactly (dropping either changes the run).
        var puppet = new GameObject { Name = "Hero" };
        puppet.Session = new Session { TermWidth = 40, ScreenReader = false };
        var output = RunHelp(puppet);
        Assert.Contains(new string('-', 38), output);
        Assert.DoesNotContain(new string('-', 40), output);
    }

    [Fact]
    public void NarrowSession_ClampsWidth()
    {
        // TermWidth 10 -> tw collapses to 20 -> width-22 table (20-dash
        // separator), which must differ from the width-80 render (60 dashes).
        var narrow = new GameObject { Name = "Hero" };
        narrow.Session = new Session { TermWidth = 10, ScreenReader = false };
        var wide = new GameObject { Name = "Hero" };
        wide.Session = new Session { TermWidth = 80, ScreenReader = false };
        Assert.NotEqual(RunHelp(wide), RunHelp(narrow));
        Assert.Contains("\n" + new string('-', 20) + "\n", "\n" + RunHelp(narrow) + "\n");
    }
}
