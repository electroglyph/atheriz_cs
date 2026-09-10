using System.Reflection;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Cli;

namespace Atheriz.Core.Tests.Features.Simplify;

// One spawn-arg builder (--port/--host/--telnet-port, one order) for the
// daemon/reset/restart spawn paths.
[Collection("Ported")]
public class SpawnArgsBuilderTests
{
    private static List<string> Build(int? port, string? host, int? telnetPort)
    {
        var m = typeof(DaemonSpawner).GetMethod("BuildSpawnArgs", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (List<string>)m.Invoke(null, [port, host, telnetPort])!;
    }

    [Fact]
    public void BuildSpawnArgs_FullSet_InUnifiedOrder()
    {
        Assert.Equal(["--port", "1", "--host", "h", "--telnet-port", "2"], Build(1, "h", 2));
    }

    [Fact]
    public void BuildSpawnArgs_Nulls_Omitted()
    {
        Assert.Empty(Build(null, null, null));
        Assert.Equal(["--host", "h"], Build(null, "h", null));
        Assert.Equal(["--port", "99"], Build(99, "", null));
    }

    [Fact]
    public void SpawnPaths_ShareTheBuilder()
    {
        foreach (var file in new[] { "ResetHandler.cs", "RestartHandler.cs", "DaemonSpawner.cs" })
        {
            var src = SourceScan.Read("src", "Atheriz.Server", "Cli", file);
            Assert.Contains("BuildSpawnArgs(", src);
        }
        var spawner = SourceScan.Read("src", "Atheriz.Server", "Cli", "DaemonSpawner.cs");
        Assert.Equal(1, SourceScan.Count(spawner, "internal static List<string> BuildSpawnArgs("));
    }
}
