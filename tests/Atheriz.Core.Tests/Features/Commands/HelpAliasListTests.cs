// Help alias-list rendering: the shared FormatAliasList helper renders the
// same key-plus-aliases string both help paths used to build inline.
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class HelpAliasListTests
{
    [Fact]
    public void FormatAliasList_CommandWithoutAliases_ReturnsKeyOnly()
    {
        var cmd = new DescCommand();
        Assert.Empty(cmd.Aliases);
        Assert.Equal("desc", HelpHelper.FormatAliasList(cmd));
        Assert.Contains("Aliases: desc", HelpHelper.FormatNoParser(cmd));
    }

    [Fact]
    public void FormatAliasList_CommandWithAliases_JoinsKeyAndAliases()
    {
        var cmd = new LookCommand();
        Assert.NotEmpty(cmd.Aliases);
        Assert.Equal("look, l", HelpHelper.FormatAliasList(cmd));
        Assert.Contains("Aliases: look, l", HelpHelper.FormatNoParser(cmd));
        Assert.Contains($"Aliases: {HelpHelper.FormatAliasList(cmd)}", cmd.PrintHelp());
    }
}
