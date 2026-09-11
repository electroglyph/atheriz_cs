using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Help text reads the shared Default instead of allocating per call:
// read-only use, same value as a fresh instance.
[Collection("Ported")]
public class HelpDefaultPortTests
{
    [Fact]
    public void DefaultWebserverPort_MatchesFreshInstance()
    {
        Assert.Equal(new AtherizSettings().WebserverPort, AtherizSettings.Default.WebserverPort);
    }

    [Fact]
    public void PrintCommandHelp_ReadsSharedDefault()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Program.cs");
        Assert.Contains("var defPort = AtherizSettings.Default.WebserverPort;", src);
        Assert.DoesNotContain("new AtherizSettings().WebserverPort", src);
    }
}
