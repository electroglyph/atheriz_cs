using Atheriz.Core.Settings;
using Atheriz.Server.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;

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

    private static AtherizSettings TlsSettings(string? certFile, string? keyFile, bool fallback)
    {
        var s = BaseSettings();
        s.AllowInsecureTlsFallback = fallback;
        if (certFile != null) s.SslCertFile = certFile;
        if (keyFile != null) s.SslKeyFile = keyFile;
        return s;
    }

    [Fact]
    public void TlsDefault_IsFailClosed()
    {
        Assert.False(new AtherizSettings().AllowInsecureTlsFallback);
    }

    [Fact]
    public void WebserverDisabled_BindsNothing()
    {
        // WebserverEnabled=false must not bind any interface — not even to fail
        // fast: an unparseable interface with the server off is a no-op, while the
        // same interface with it on throws.
        var off = BaseSettings();
        off.WebserverEnabled = false;
        off.WebserverInterface = "!!!unparseable!!!";
        var ex = Record.Exception(() => KestrelConfig.ConfigureKestrel(new KestrelServerOptions(), off));
        Assert.Null(ex);

        var on = BaseSettings();
        on.WebserverEnabled = true;
        on.WebserverInterface = "!!!unparseable!!!";
        Assert.Throws<InvalidOperationException>(() => KestrelConfig.ConfigureKestrel(new KestrelServerOptions(), on));
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
    public void Validator_AcceptsPresentCert_LoadFailsAtHostStartup()
    {
        // Validation is file-exists-only (no crypto load): a present but
        // unloadable cert passes here and fails fail-fast at host startup
        // (Kestrel_UnloadableCert_ThrowsWhenFailClosed pins that half).
        var cert = Path.GetTempFileName();
        File.WriteAllText(cert, "not a pem at all");
        try
        {
            var v = new AtherizSettingsValidator();
            var s = BaseSettings();
            s.SslCertFile = cert;
            s.AllowInsecureTlsFallback = false;
            var r = v.Validate(null, s);
            Assert.False(r.Failed);
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
        var config = TlsSettings(Path.Combine(Path.GetTempPath(), "no_such_atheriz_cert_xyz.pem"), null, fallback: false);
        Assert.Throws<InvalidOperationException>(() => KestrelConfig.ConfigureKestrel(new KestrelServerOptions(), config));
    }

    [Fact]
    public void Kestrel_MissingCert_WarnsOnlyUnderExplicitOptIn()
    {
        var config = TlsSettings(Path.Combine(Path.GetTempPath(), "no_such_atheriz_cert_xyz.pem"), null, fallback: true);
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
            var config = TlsSettings(cert, null, fallback: false);
            Assert.Throws<InvalidOperationException>(() => KestrelConfig.ConfigureKestrel(new KestrelServerOptions(), config));
        }
        finally { File.Delete(cert); }
    }

    [Fact]
    public void Kestrel_NoCert_ConfiguresPlaintext()
    {
        var config = TlsSettings(null, null, fallback: false);
        var ex = Record.Exception(() => KestrelConfig.ConfigureKestrel(new KestrelServerOptions(), config));
        Assert.Null(ex);
    }

    [Fact]
    public void CertSelector_ServesStartupLoadedCert()
    {
        // The endpoint serves the startup-loaded cert through the selector
        // (fail-fast load stays in KestrelConfig); selection itself never
        // reads the filesystem and ignores the SNI name.
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=selectortest", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var selector = new KestrelCertSelector(cert);
        Assert.Same(cert, selector.Select(null, null));
        Assert.Same(cert, selector.Select(null, "any-sni-name"));
    }
}
