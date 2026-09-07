using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// Admin probes must not learn whether the token file exists.
[Collection("Ported")]
public class AdminTokenLoopbackTests
{
    [Fact]
    public void CheckAdmin_MissingTokenFileFromRemoteIp_RefusesAsRemoteNotLoopback()
    {
        // Behavior: a remote (non-loopback) admin probe must be refused as
        // remote, without revealing whether the token file exists. Today the
        // missing-file message at AdminToken.cs:136-137 returns before the
        // loopback check at AdminToken.cs:142, so a remote prober can
        // distinguish "no token file" from "invalid token".
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_adm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var result = AdminToken.CheckAdmin(dir, "8.8.8.8", "whatever", "shutdown");
            Assert.Equal("Remote shutdown not allowed.", result);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
