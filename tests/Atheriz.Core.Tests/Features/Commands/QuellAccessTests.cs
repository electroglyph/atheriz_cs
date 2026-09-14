using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Features.Commands;

// The quell access gate must not fold quelled state, or the already-quelled
// branch is unreachable through dispatch.
[Collection("Ported")]
public class QuellAccessTests
{
    [Fact]
    public void QuellCommand_QuelledBuilder_ReachesAlreadyQuelledBranch()
    {
        using var env = GlobalTestEnv.Enter();
        var c = Ported.PortedHelpers.MakeCaller("Alice", builder: true);
        c.Quelled = true;
        Assert.True(new QuellCommand().Access(c));
        c.ClearMessages();
        new QuellCommand().Run(c, null);
        Assert.Contains(c.PeekMessages(), m => m == "You are already quelled!");
    }
}
