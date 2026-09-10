using System.Reflection;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Simplify;

// One positional filter for the create/new handlers (bare, consumed-value,
// prefix and glued shapes). The filter produces no output; pin positional
// passthrough equality.
[Collection("Ported")]
public class ArgFilterStripOptionsTests
{
    private static string[] Strip(string method, string[] a)
    {
        var m = typeof(ArgumentParser).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string[])m.Invoke(null, [a])!;
    }

    [Fact]
    public void StripPortOptions_RemovesPortFamily_KeepsPositionals()
    {
        Assert.Equal(["acc", "char", "pw"],
            Strip("StripPortOptions", ["acc", "char", "pw", "--port", "1234"]));
        Assert.Equal(["acc"],
            Strip("StripPortOptions", ["--port", "1", "acc"]));
        Assert.Equal(["acc", "char", "pw"],
            Strip("StripPortOptions", ["acc", "--port=12", "char", "-p34", "pw"]));
    }

    [Fact]
    public void StripKnownOptions_RemovesFullFlagSet_KeepsPositionals()
    {
        Assert.Equal(["mygame"],
            Strip("StripKnownOptions", ["mygame", "--port", "1", "--host", "h", "--telnet-port", "2", "--overwrite", "--foreground"]));
        Assert.Equal(["mygame"],
            Strip("StripKnownOptions", ["mygame", "-p1", "-f", "--force"]));
    }

    [Fact]
    public void StripOptions_TrailingBareFlag_NeverBecomesValue()
    {
        Assert.Equal(["acc", "--port"], Strip("StripPortOptions", ["acc", "--port"]));
        Assert.Equal(["mygame", "--host"], Strip("StripKnownOptions", ["mygame", "--host"]));
    }

    [Fact]
    public void StripOptions_LivesInOneCore()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ArgumentParser.cs");
        Assert.Contains("StripPortOptions", src);
        Assert.Contains("StripKnownOptions", src);
        Assert.Equal(1, SourceScan.Count(src, "private static string[] StripOptions("));
    }
}
