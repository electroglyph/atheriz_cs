using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// The help table keeps its pinned outputs byte-identical and keeps the
// description column aligned even for over-long keys.
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

    [Fact]
    public void HelpFormatter_PinnedOutputsStayByteIdentical()
    {
        Command[] screenreaderCmds = [new OpenCommand(), new CloseCommand()];
        Assert.Equal(
            "General      close        Close doors.\n" +
            "General      open         Open doors.\n",
            HelpFormatter.Format(screenreaderCmds, screenreader: true, termWidth: 80));
        Command[] tableCmds = [new OpenCommand()];
        Assert.Equal(
            "Category     Command      Description\n" +
            new string('-', 60) + "\n" +
            "General      open         Open doors.\n",
            HelpFormatter.Format(tableCmds, screenreader: false, termWidth: 80));
        Assert.Equal(
            "Category     Command      Description\n" +
            new string('-', 28) + "\n" +
            "General      open         O...\n",
            HelpFormatter.Format(tableCmds, screenreader: false, termWidth: 30));
    }

    [Fact]
    public void HelpFormatter_LongKeyKeepsDescriptionColumnAligned()
    {
        Command[] cmds = [new LongKeyCommand(), new OpenCommand()];
        string table = HelpFormatter.Format(cmds, screenreader: false, termWidth: 80);
        var lines = table.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var header = lines.First(l => l.Contains("Description"));
        var longRow = lines.First(l => l.Contains("superlongcommandkey"));
        // The full key survives (never cut to fit) and both descriptions
        // start in the same column: the header widened with the data.
        Assert.Contains("superlongcommandkey Long key.", longRow);
        Assert.Equal(header.IndexOf("Description", StringComparison.Ordinal),
            longRow.IndexOf("Long key.", StringComparison.Ordinal));
        string list = HelpFormatter.Format(cmds, screenreader: true, termWidth: 80);
        var listRows = list.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var openListRow = listRows.First(l => l.Contains("Open doors."));
        var longListRow = listRows.First(l => l.Contains("superlongcommandkey"));
        Assert.Equal(
            openListRow.IndexOf("Open doors.", StringComparison.Ordinal),
            longListRow.IndexOf("Long key.", StringComparison.Ordinal));
    }
}
