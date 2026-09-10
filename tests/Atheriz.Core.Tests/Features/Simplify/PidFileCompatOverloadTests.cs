using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Simplify;

// LocateServerPidFile ignores its port argument (always {savePath}/server.pid):
// the two-arg shape stays as a compatibility forwarder. Release funnels
// through the owner-verified delete.
[Collection("Ported")]
public class PidFileCompatOverloadTests
{
    [Fact]
    public void LocateServerPidFile_PortOverload_Forwards()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_pidcompat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(PidFile.LocateServerPidFile(dir), PidFile.LocateServerPidFile(1234, dir));
            Assert.Equal(Path.Combine(dir, "server.pid"), PidFile.LocateServerPidFile(9999, dir));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Release_UsesOwnerVerifiedDelete()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "PidFile.cs");
        var region = SourceScan.Region(src, "public void Release()");
        Assert.Contains("ReleaseIfOwner(PidPath, _ownPid)", region);
        Assert.Equal(1, SourceScan.Count(src, "public static string LocateServerPidFile(string savePath)"));
        Assert.Equal(1, SourceScan.Count(src, "public static string LocateServerPidFile(int port, string savePath)"));
    }
}
