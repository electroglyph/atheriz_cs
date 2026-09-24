using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Hosting;

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
    public async Task StartHelp_NamesDefaultPort()
    {
        var orig = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            await Atheriz.Server.Cli.AtherizCli.InvokeAsync(["start", "--help"]);
        }
        finally { Console.SetOut(orig); }
        Assert.Contains(AtherizSettings.Default.WebserverPort.ToString(), sw.ToString(), StringComparison.Ordinal);
    }
}
