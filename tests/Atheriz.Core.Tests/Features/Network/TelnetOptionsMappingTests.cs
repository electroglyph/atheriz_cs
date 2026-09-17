using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Ported;
using telnet_cs.Server;

namespace Atheriz.Core.Tests.Features.Network;

// Pins for the telnet_cs options/admission mapping: BuildServerOptions must keep
// every Phase-4 decision (parity timeouts, Atheriz-enforced caps, NAWS on,
// charset off), HostOf must label every endpoint shape, and the filter must
// snapshot the peer host into the handoff.
[Collection("Ported")]
public sealed class TelnetOptionsMappingTests
{
    private static Func<EndPoint?, AcceptDecision> AllowAll() =>
        _ => new AcceptDecision(true);

    [Fact]
    public void BuildServerOptions_Plaintext_MapsAllFields()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { TelnetInterface = "127.0.0.1" };
        var filter = AllowAll();
        var options = TelnetProtocol.BuildServerOptions(settings, null, filter);
        Assert.Equal(IPAddress.Parse("127.0.0.1"), options.ListenAddress);
        Assert.Same(Encoding.UTF8, options.TextEncoding);
        Assert.False(options.RequestCharacterSet);
        Assert.True(options.RequestWindowSize);
        Assert.Equal(0, options.MaxConcurrentSessions);
        Assert.Equal(0, options.MaxConnectionsPerIp);
        Assert.Equal(0, options.MaxBufferedTextChars);
        Assert.Equal(Timeout.InfiniteTimeSpan, options.IdleTimeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, options.HandshakeTimeout);
        Assert.Null(options.StatusInterval);
        Assert.Null(options.ServerCertificate);
        Assert.Equal(Timeout.InfiniteTimeSpan, options.TlsAutoDetect);
        Assert.Same(filter, options.AcceptFilterV2);
        Assert.NotNull(options.Log);
    }

    [Fact]
    public void BuildServerOptions_WithCert_BoundsHandshakeAndAutodetect()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { TelnetInterface = "127.0.0.1" };
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("cn=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddDays(1));
        var options = TelnetProtocol.BuildServerOptions(settings, cert, AllowAll());
        Assert.Same(cert, options.ServerCertificate);
        Assert.Equal(TimeSpan.FromSeconds(10), options.HandshakeTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.TlsAutoDetect);
        // Everything else keeps its parity values.
        Assert.Equal(Timeout.InfiniteTimeSpan, options.IdleTimeout);
        Assert.Null(options.StatusInterval);
        Assert.False(options.RequestCharacterSet);
    }

    [Fact]
    public void BuildServerOptions_UnparseableInterface_Throws()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { TelnetInterface = "not-an-ip" };
        Assert.Throws<InvalidOperationException>(() =>
            TelnetProtocol.BuildServerOptions(settings, null, AllowAll()));
    }

    [Fact]
    public void BuildServerOptions_NullArgs_Throw()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { TelnetInterface = "127.0.0.1" };
        Assert.Throws<ArgumentNullException>(() =>
            TelnetProtocol.BuildServerOptions(null!, null, AllowAll()));
        Assert.Throws<ArgumentNullException>(() =>
            TelnetProtocol.BuildServerOptions(settings, null, null!));
    }

    [Fact]
    public void HostOf_LabelsEveryEndpointShape()
    {
        Assert.Equal("127.0.0.1", TelnetProtocol.HostOf(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 4444)));
        Assert.Equal("::1", TelnetProtocol.HostOf(new IPEndPoint(IPAddress.IPv6Loopback, 4444)));
        Assert.Equal("?", TelnetProtocol.HostOf(null));
        Assert.Equal("?", TelnetProtocol.HostOf(new DnsEndPoint("example.com", 4444)));
    }

    [Fact]
    public void BuildAcceptFilter_SnapshotsHostIntoHandoff()
    {
        using var env = GlobalTestEnv.Enter();
        var prevGlobal = ConnectionManager.GlobalInstance;
        var mgr = PortedHelpers.MakeManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            var handoff = new ConcurrentQueue<string?>();
            var filter = TelnetProtocol.BuildAcceptFilter(mgr, handoff);
            var decision = filter(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 1234));
            Assert.True(decision.Allowed);
            Assert.True(handoff.TryDequeue(out var host));
            Assert.Equal("127.0.0.2", host);
        }
        finally
        {
            try { mgr.Atp.Stop(wait: false); } catch { }
            ConnectionManager.GlobalInstance = prevGlobal;
        }
    }

    [Fact]
    public void BuildAcceptFilter_NullArgs_Throw()
    {
        using var env = GlobalTestEnv.Enter();
        var prevGlobal = ConnectionManager.GlobalInstance;
        var mgr = PortedHelpers.MakeManager();
        ConnectionManager.GlobalInstance = mgr;
        try
        {
            Assert.Throws<ArgumentNullException>(() =>
                TelnetProtocol.BuildAcceptFilter(null!, new ConcurrentQueue<string?>()));
            Assert.Throws<ArgumentNullException>(() =>
                TelnetProtocol.BuildAcceptFilter(mgr, null!));
        }
        finally
        {
            try { mgr.Atp.Stop(wait: false); } catch { }
            ConnectionManager.GlobalInstance = prevGlobal;
        }
    }

    [Fact]
    public void ClampNaws_NullSettings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TelnetProtocol.ClampNaws(24, 80, null!));
    }
}
