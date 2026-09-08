using Atheriz.Core.Settings;
using Atheriz.Server.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

namespace Atheriz.Core.Tests.Features.Hosting;

// TLS must fail closed — a configured-but-broken cert never silently
// serves the admin token over plaintext.
public class TlsFailClosedTests
{
    private static AtherizSettings BaseSettings() => new()
    {
        SavePath = Path.Combine(Path.GetTempPath(), "atheriz_tls_save"),
        SecretPath = Path.Combine(Path.GetTempPath(), "atheriz_tls_secret"),
    };

    private static IConfiguration ConfigFor(string? certFile, string? keyFile, bool fallback)
    {
        var pairs = new Dictionary<string, string?>
        {
            ["Atheriz:AllowInsecureTlsFallback"] = fallback ? "true" : "false",
        };
        if (certFile != null) pairs["Atheriz:SslCertFile"] = certFile;
        if (keyFile != null) pairs["Atheriz:SslKeyFile"] = keyFile;
        return new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
    }

    [Fact]
    public void TlsDefault_IsFailClosed()
    {
        Assert.False(new AtherizSettings().AllowInsecureTlsFallback);
    }

    [Fact]
    public void Validator_RejectsMissingCertFile()
    {
        var v = new AtherizSettingsValidator();
        var s = BaseSettings();
        s.SslCertFile = Path.Combine(Path.GetTempPath(), "no_such_atheriz_cert_xyz.pem");
        var r = v.Validate(null, s);
        Assert.True(r.Failed);
        Assert.Contains("SslCertFile", r.FailureMessage);
    }

    [Fact]
    public void Validator_RejectsMissingKeyFile()
    {
        var cert = Path.GetTempFileName();
        try
        {
            var v = new AtherizSettingsValidator();
            var s = BaseSettings();
            s.SslCertFile = cert;
            s.SslKeyFile = Path.Combine(Path.GetTempPath(), "no_such_atheriz_key_xyz.pem");
            var r = v.Validate(null, s);
            Assert.True(r.Failed);
            Assert.Contains("SslKeyFile", r.FailureMessage);
        }
        finally { File.Delete(cert); }
    }

    [Fact]
    public void Validator_RejectsUnloadableCert()
    {
        var cert = Path.GetTempFileName();
        File.WriteAllText(cert, "not a pem at all");
        try
        {
            var v = new AtherizSettingsValidator();
            var s = BaseSettings();
            s.SslCertFile = cert;
            s.AllowInsecureTlsFallback = false;
            var r = v.Validate(null, s);
            Assert.True(r.Failed);
            Assert.Contains("unloadable", r.FailureMessage);
        }
        finally { File.Delete(cert); }
    }

    [Fact]
    public void Validator_ToleratesUnloadableCertUnderExplicitOptIn()
    {
        var cert = Path.GetTempFileName();
        File.WriteAllText(cert, "not a pem at all");
        try
        {
            var v = new AtherizSettingsValidator();
            var s = BaseSettings();
            s.SslCertFile = cert;
            s.AllowInsecureTlsFallback = true;
            var r = v.Validate(null, s);
            Assert.False(r.Failed);
        }
        finally { File.Delete(cert); }
    }

    [Fact]
    public void Kestrel_MissingCert_ThrowsWhenFailClosed()
    {
        var config = ConfigFor(Path.Combine(Path.GetTempPath(), "no_such_atheriz_cert_xyz.pem"), null, fallback: false);
        Assert.Throws<InvalidOperationException>(() => KestrelConfig.ConfigureKestrel(new KestrelServerOptions(), config));
    }

    [Fact]
    public void Kestrel_MissingCert_WarnsOnlyUnderExplicitOptIn()
    {
        var config = ConfigFor(Path.Combine(Path.GetTempPath(), "no_such_atheriz_cert_xyz.pem"), null, fallback: true);
        var ex = Record.Exception(() => KestrelConfig.ConfigureKestrel(new KestrelServerOptions(), config));
        Assert.Null(ex);
    }

    [Fact]
    public void Kestrel_UnloadableCert_ThrowsWhenFailClosed()
    {
        var cert = Path.GetTempFileName();
        File.WriteAllText(cert, "not a pem at all");
        try
        {
            var config = ConfigFor(cert, null, fallback: false);
            Assert.Throws<InvalidOperationException>(() => KestrelConfig.ConfigureKestrel(new KestrelServerOptions(), config));
        }
        finally { File.Delete(cert); }
    }

    [Fact]
    public void Kestrel_NoCert_ConfiguresPlaintext()
    {
        var config = ConfigFor(null, null, fallback: false);
        var ex = Record.Exception(() => KestrelConfig.ConfigureKestrel(new KestrelServerOptions(), config));
        Assert.Null(ex);
    }
}
