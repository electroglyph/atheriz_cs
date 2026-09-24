using Atheriz.Server.Hosting;

namespace Atheriz.Core.Tests.Features.Hosting;

// Foreground-start safety: bad interfaces fail before any claim, and the
// bash/nohup spawner is gone (no Unix-only spawn surface left).
[Collection("Ported")]
public sealed class ServerStartSafetyTests
{
    [Fact]
    public async Task Start_InvalidHost_FailsFast()
    {
        Assert.Equal(2, await ServerHost.RunForegroundAsync(null, "bad host!", null));
    }

    [Fact]
    public void DaemonSpawner_Exists()
    {
        Assert.NotNull(Type.GetType("Atheriz.Server.Cli.DaemonSpawner, Atheriz.Server"));
    }
}
