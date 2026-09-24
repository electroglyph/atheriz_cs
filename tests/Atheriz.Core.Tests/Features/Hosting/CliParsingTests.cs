using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Hosting;

// CLI parsing pins: the only pre-parse rewrite (glued -p1234 split), the
// telnet env fallback, and rejection of bad input before anything runs.
[Collection("Ported")]
public sealed class CliParsingTests
{
    [Fact]
    public void NormalizeArgs_SplitsGluedShortPort()
    {
        Assert.Equal(["-p", "1234"], AtherizCli.NormalizeArgs(["-p1234"]));
        Assert.Equal(["-p", "1234"], AtherizCli.NormalizeArgs(["-p=1234"]));
        Assert.Equal(["-p", "1", "acc"], AtherizCli.NormalizeArgs(["-p", "1", "acc"]));
        Assert.Equal([], AtherizCli.NormalizeArgs([]));
    }

    [Fact]
    public void IsGluedShortPort_Shapes()
    {
        Assert.True(AtherizCli.IsGluedShortPort("-p1"));
        Assert.True(AtherizCli.IsGluedShortPort("-p=2"));
        Assert.False(AtherizCli.IsGluedShortPort("-p"));
        Assert.False(AtherizCli.IsGluedShortPort("--port"));
        Assert.False(AtherizCli.IsGluedShortPort("-f"));
    }

    [Fact]
    public void TelnetPortOrEnv_FlagWinsThenEnvThenNull()
    {
        var old1 = Environment.GetEnvironmentVariable("ATHERIZ_TELNET_PORT");
        var old2 = Environment.GetEnvironmentVariable("Atheriz__TelnetPort");
        try
        {
            Environment.SetEnvironmentVariable("ATHERIZ_TELNET_PORT", "4321");
            Environment.SetEnvironmentVariable("Atheriz__TelnetPort", null);
            Assert.Equal(1, AtherizCli.TelnetPortOrEnv(1));
            Assert.Equal(4321, AtherizCli.TelnetPortOrEnv(null));
            Environment.SetEnvironmentVariable("ATHERIZ_TELNET_PORT", null);
            Assert.Null(AtherizCli.TelnetPortOrEnv(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_TELNET_PORT", old1);
            Environment.SetEnvironmentVariable("Atheriz__TelnetPort", old2);
        }
    }

    [Fact]
    public async Task InvalidPort_RejectedWithoutRunning()
    {
        Assert.NotEqual(0, await AtherizCli.InvokeAsync(["start", "--port", "abc"]));
    }

    [Fact]
    public async Task MissingPositional_Rejected()
    {
        Assert.NotEqual(0, await AtherizCli.InvokeAsync(["create"]));
        Assert.NotEqual(0, await AtherizCli.InvokeAsync(["new"]));
    }
}
