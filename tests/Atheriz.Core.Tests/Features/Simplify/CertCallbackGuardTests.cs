using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// The cert callback keeps only the live null-guard (RequestUri is nullable);
// the always-false `req is not HttpRequestMessage` pattern is gone.
// Loopback-only tolerance is unchanged.
[Collection("Ported")]
public class CertCallbackGuardTests
{
    [Fact]
    public void Callback_KeepsLiveGuard_DropsDeadPattern()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        var region = SourceScan.Region(src, "ServerCertificateCustomValidationCallback");
        Assert.DoesNotContain("req is not HttpRequestMessage", region);
        Assert.Contains("req.RequestUri is not Uri", region);
    }

    [Fact]
    public void Callback_LoopbackSet_Unchanged()
    {
        var src = SourceScan.Read("src", "Atheriz.Server", "Cli", "ShutdownClient.cs");
        Assert.Contains("u.Host == \"localhost\"", src);
        Assert.Contains("u.Host == \"127.0.0.1\"", src);
        Assert.Contains("u.Host == \"::1\"", src);
        Assert.Contains("RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch", src);
    }
}
