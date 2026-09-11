using Atheriz.Core.Tests.Features.Regression;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// One IsIPv4MappedToIPv6 block with a single MapToIPv4: the same loopback
// table, one allocation on the mapped path.
[Collection("Ported")]
public class AdminLoopbackHoistTests
{
    private static string NewSecretDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_looptab_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:127.0.0.2")]
    public void CheckAdmin_LoopbackAddress_AllowsRequest(string ip)
    {
        var dir = NewSecretDir();
        try
        {
            var token = AdminToken.EnsureToken(dir);
            Assert.Null(AdminToken.CheckAdmin(dir, ip, token, "shutdown"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("192.168.1.1")]
    [InlineData("::ffff:8.8.8.8")]
    public void CheckAdmin_NonLoopbackAddress_RefusesAsRemote(string ip)
    {
        var dir = NewSecretDir();
        try
        {
            var token = AdminToken.EnsureToken(dir);
            Assert.Equal("Remote shutdown not allowed.", AdminToken.CheckAdmin(dir, ip, token, "shutdown"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void IsLoopbackIp_TestsMappedOnce_MapsOnce()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Infrastructure", "AdminToken.cs");
        Assert.Equal(1, SourceScan.Count(src, "IsIPv4MappedToIPv6"));
        Assert.Equal(1, SourceScan.Count(src, "MapToIPv4()"));
        Assert.Contains("IsLoopback(v4)", src);
        Assert.Contains("v4.GetAddressBytes()[0] == 127", src);
        // The plain-IPv4 arm keeps its position after the mapped block.
        int mapped = src.IndexOf("IsIPv4MappedToIPv6", StringComparison.Ordinal);
        int plain = src.IndexOf("AddressFamily.InterNetwork", StringComparison.Ordinal);
        Assert.True(mapped >= 0 && plain > mapped);
    }
}
