// Channel parser reset: SetKey/SetDesc assign directly and the help text
// rebuilds on the new key without throwing.
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class ChannelParserResetTests
{
    [Fact]
    public void SetKey_UpdatesKeyAndHelpShowsNewProg()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("b20ahelp");
        var cmd = chan.GetCommand() as BaseChannelCommand;
        Assert.NotNull(cmd);
        cmd!.SetKey("b20anewkey");
        Assert.Equal("b20anewkey", cmd.Key);
        var help = cmd.Parser!.FormatHelp();
        Assert.Contains("b20anewkey", help);
    }

    [Fact]
    public void SetDesc_UpdatesDescWithoutThrowing()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new BaseChannelCommand();
        var ex = Record.Exception(() => cmd.SetDesc("b20a desc"));
        Assert.Null(ex);
        Assert.Equal("b20a desc", cmd.Desc);
    }

    [Fact]
    public void SetKey_DoesNotThrow()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new BaseChannelCommand();
        var ex = Record.Exception(() => cmd.SetKey("b20a_key"));
        Assert.Null(ex);
        Assert.Equal("b20a_key", cmd.Key);
    }

    [Fact]
    public void SetKey_TwiceInARow_KeepsLatestHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new BaseChannelCommand();
        cmd.SetKey("b20afirst");
        cmd.SetKey("b20asecond");
        Assert.Equal("b20asecond", cmd.Key);
        Assert.Contains("b20asecond", cmd.Parser!.FormatHelp());
    }
}
