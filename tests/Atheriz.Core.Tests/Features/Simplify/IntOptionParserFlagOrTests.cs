using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Simplify;

// Shared int-option core for the Parse*/Invalid* port pairs (public names and
// messages unchanged) + pure-OR flag helper for the force-like flag sets.
[Collection("Ported")]
public class IntOptionParserFlagOrTests
{
    [Fact]
    public void PortPair_ParsesAndReportsInvalid()
    {
        Assert.Equal(12, ArgumentParser.ParsePort(["--port", "12"]));
        Assert.Null(ArgumentParser.ParsePort(["--port", "abc"]));
        Assert.Equal("abc", ArgumentParser.InvalidPortValue(["--port", "abc"]));
        Assert.Null(ArgumentParser.InvalidPortValue(["--port", "12"]));
        Assert.Null(ArgumentParser.InvalidPortValue([]));
    }

    [Fact]
    public void TelnetPair_ParsesAndReportsInvalid()
    {
        Assert.Equal(23, ArgumentParser.ParseTelnetPort(["--telnet-port", "23"]));
        Assert.Equal("xx", ArgumentParser.InvalidTelnetPortValue(["--telnet-port", "xx"]));
        Assert.Null(ArgumentParser.InvalidTelnetPortValue([]));
    }

    [Fact]
    public void HasAnyFlag_OrSemantics()
    {
        Assert.True(ArgumentParser.HasAnyFlag(["--force"], "--force", "-f", "--yes", "-y"));
        Assert.True(ArgumentParser.HasAnyFlag(["-y"], "--force", "-f", "--yes", "-y"));
        Assert.False(ArgumentParser.HasAnyFlag(["--other"], "--force", "-f", "--yes", "-y"));
        Assert.False(ArgumentParser.HasAnyFlag([], "--force", "-f"));
    }

    [Fact]
    public void Pairs_ShareOneCore_PublicShapesKept()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ArgumentParser.cs");
        Assert.Equal(1, SourceScan.Count(src, "private static int? ParseIntOption("));
        Assert.Equal(1, SourceScan.Count(src, "private static string? InvalidIntOption("));
        Assert.Equal(1, SourceScan.Count(src, "public static bool HasAnyFlag("));
    }
}
