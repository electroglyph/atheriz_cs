using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// Command help is a command-to-row table with one printer: the start/restart
// and stop/reload pairs share one row instance each. Every string verbatim;
// table order matches the --help listing order.
[Collection("Ported")]
public class CommandHelpTableTests
{
    private static string Src()
        => SourceScan.Read("src", "Atheriz.Server", "Program.cs");

    [Fact]
    public void HelpTable_SharesPairRows()
    {
        var src = Src();
        Assert.Contains("[\"restart\"] = startRow", src);
        Assert.Contains("[\"reload\"] = stopRow", src);
        Assert.Equal(1, SourceScan.Count(src, "sealed record CommandHelpRow("));
    }

    [Fact]
    public void HelpTable_PrintsViaOnePrinter()
    {
        var src = Src();
        var region = SourceScan.Region(src, "void PrintCommandHelp(string cmd)");
        Assert.DoesNotContain("switch (cmd)", region);
        Assert.Contains("table.TryGetValue(cmd, out var row)", region);
        Assert.Contains("row.Usage(cmd)", region);
        Assert.Contains("row.Description(cmd)", region);
        Assert.Contains("foreach (var line in row.Options)", region);
    }

    [Fact]
    public void HelpTable_StringsVerbatim_InHelpOrder()
    {
        var src = Src();
        Assert.Contains("Usage: Atheriz.Server {c} [--port N] [--host HOST] [--foreground|-f]", src);
        Assert.Contains("  Start the AtheriZ server", src);
        Assert.Contains("  Restart the AtheriZ server", src);
        Assert.Contains("  Stop the AtheriZ server", src);
        Assert.Contains("  Hot reload game logic", src);
        Assert.Contains("Usage: Atheriz.Server test [core] [args...]", src);
        int last = -1;
        foreach (var key in new[] { "[\"start\"]", "[\"restart\"]", "[\"stop\"]", "[\"reload\"]", "[\"reset\"]", "[\"create\"]", "[\"new\"]", "[\"test\"]" })
        {
            int at = src.IndexOf(key, StringComparison.Ordinal);
            Assert.True(at > last, key + " out of --help order");
            last = at;
        }
    }
}
