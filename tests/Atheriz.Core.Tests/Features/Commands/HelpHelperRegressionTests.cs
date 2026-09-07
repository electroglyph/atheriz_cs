using Atheriz.Core.Commands;
using Atheriz.Core.Objects;
using LoggedInCmds = Atheriz.Core.Commands.LoggedIn;
using UnloggedInCmds = Atheriz.Core.Commands.UnloggedIn;

namespace Atheriz.Core.Tests.Features.Commands;

// P1-12: HelpHelper.FormatFor centralizes the three identical help-text copies
// (logged-in HelpCommand.PrintHelpFor, unlogged-in HelpCommand.PrintHelpFor,
// Command.PrintHelp no-parser branch). Exact-equality pins, written first.
public sealed class HelpHelperRegressionTests
{
    private sealed class AliaslessCommand : Command
    {
        public override string Key => "zeta";
        public override string Desc => "Zeta desc.";
        public override string ExtraDesc => "Extra words.";
        public override bool UseParser => false;
        public override void Run(IMessageTarget caller, object? args) { }
    }

    [Fact]
    public void FormatFor_NoParserWithAliases_MatchesTemplateExactly()
    {
        var quit = new UnloggedInCmds.QuitCommand();
        string expected = "\nQuit.\n\nAliases: quit, exit, logout, disconnect\n" + quit.ExtraDesc;
        Assert.Equal(expected, HelpHelper.FormatFor(quit));
    }

    [Fact]
    public void FormatFor_NoParserWithoutAliases_KeyOnly()
    {
        var cmd = new AliaslessCommand();
        Assert.Equal("\nZeta desc.\n\nAliases: zeta\nExtra words.", HelpHelper.FormatFor(cmd));
    }

    [Fact]
    public void FormatFor_TripleEquality_AllThreeCopiesAgree()
    {
        // The two HelpCommands' inline PrintHelpFor and Command.PrintHelp's
        // no-parser branch must all produce exactly what the helper produces.
        var quit = new UnloggedInCmds.QuitCommand();
        Assert.Equal(quit.PrintHelp(), HelpHelper.FormatFor(quit));
        var zeta = new AliaslessCommand();
        Assert.Equal(zeta.PrintHelp(), HelpHelper.FormatFor(zeta));
    }

    [Fact]
    public void FormatFor_ParserCommand_DelegatesToPrintHelp()
    {
        var help = new LoggedInCmds.HelpCommand();
        Assert.NotNull(help.Parser);
        Assert.Equal(help.PrintHelp(), HelpHelper.FormatFor(help));
    }
}
