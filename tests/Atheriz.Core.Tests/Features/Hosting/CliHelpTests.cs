using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Hosting;

// CLI help comes from the command tree: top-level lists every command,
// per-command help names its options. Replaces the hand-table pins.
[Collection("Ported")]
public sealed class CliHelpTests
{
    private static async Task<string> Invoke(params string[] args)
    {
        var orig = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            await AtherizCli.InvokeAsync(args);
            return sw.ToString();
        }
        finally { Console.SetOut(orig); }
    }

    [Fact]
    public async Task TopHelp_ListsEveryCommand()
    {
        var help = await Invoke("--help");
        foreach (var cmd in new[] { "start", "stop", "restart", "reload", "reset", "create", "new", "test" })
            Assert.Contains(cmd, help, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandHelp_NamesOptions()
    {
        var help = await Invoke("start", "--help");
        Assert.Contains("--port", help, StringComparison.Ordinal);
        Assert.Contains("--host", help, StringComparison.Ordinal);
    }
}
