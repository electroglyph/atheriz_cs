using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// An explicit --help flag surfaces the help text exactly once: the message
// IS the help, not diagnosis-plus-help.
[Collection("Ported")]
public class CommandHelpSingleMessageTests
{
    [Fact]
    public void Execute_HelpFlag_SendsSingleMessage()
    {
        var puppet = new GameObject { Name = "Hero" };
        puppet.ClearMessages();
        var cmd = new FollowCommand();
        var (func, _, _) = cmd.Execute(puppet, "--help");
        Assert.Null(func);
        Assert.Single(puppet.PeekMessages());
    }
}
