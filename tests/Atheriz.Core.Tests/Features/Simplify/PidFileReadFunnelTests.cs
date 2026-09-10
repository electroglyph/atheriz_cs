using System.Reflection;
using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Simplify;

// One pid-file read (ReadAllText + Trim + TryParse; failure = dead claim) for
// the CLI liveness probes, plus the live-claim wrapper.
[Collection("Ported")]
public class PidFileReadFunnelTests
{
    private static int? TryReadPid(string path)
    {
        var m = typeof(PidFile).GetMethod("TryReadPid", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (int?)m.Invoke(null, [path]);
    }

    [Fact]
    public void TryReadPid_ValidParses_InvalidAndMissingAreNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_pidfunnel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var good = Path.Combine(dir, "a.pid");
            File.WriteAllText(good, "  1234\n");
            Assert.Equal(1234, TryReadPid(good));
            var bad = Path.Combine(dir, "b.pid");
            File.WriteAllText(bad, "not-a-pid");
            Assert.Null(TryReadPid(bad));
            var empty = Path.Combine(dir, "c.pid");
            File.WriteAllText(empty, "");
            Assert.Null(TryReadPid(empty));
            Assert.Null(TryReadPid(Path.Combine(dir, "missing.pid")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Probes_FunnelThroughSharedRead()
    {
        foreach (var file in new[] { "Cli/ResetHandler.cs", "Cli/RestartHandler.cs", "Cli/StopHandler.cs" })
        {
            var src = SourceScan.Read("src", "Atheriz.Server", file);
            Assert.Contains("TryReadPid(", src);
            Assert.DoesNotContain("File.ReadAllText(pid", src);
        }
        var create = SourceScan.Read("src", "Atheriz.Server", "Cli", "CreateHandler.cs");
        Assert.Contains("IsLiveClaim(", create);
        var gen = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "GameTemplateGenerator.cs");
        var region = SourceScan.Region(gen, "private static bool IsLiveServerFolder(");
        Assert.Contains("TryReadPid(", region);
        Assert.DoesNotContain("File.ReadAllText", region);
        var pid = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        Assert.Equal(1, SourceScan.Count(pid, "internal static int? TryReadPid("));
        Assert.Equal(1, SourceScan.Count(pid, "internal static bool IsLiveClaim("));
    }
}
