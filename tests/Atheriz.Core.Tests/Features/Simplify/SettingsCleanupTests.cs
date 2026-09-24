using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// Settings without the singleton machinery: lock-free Global publish, no
// shared Default, validator as plain conditionals with file-exists-only
// cert validation (crypto load happens once at host startup).
public class SettingsCleanupTests
{
    [Fact]
    public void Global_IsLockFreePublish_WithNullGuard()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Settings", "AtherizSettings.cs");
        Assert.DoesNotContain("_globalLock", src);
        Assert.DoesNotContain("static AtherizSettings Default", src);
        Assert.Contains("Volatile.Read(ref _global)", src);
    }

    [Fact]
    public void Global_RoundTrips_AndRejectsNull()
    {
        var original = AtherizSettings.Global;
        try
        {
            var fresh = new AtherizSettings { ServerName = "Probe" };
            AtherizSettings.Global = fresh;
            Assert.Same(fresh, AtherizSettings.Global);
            AtherizSettings.Global = null!;
            Assert.NotNull(AtherizSettings.Global);
        }
        finally { AtherizSettings.Global = original; }
    }

    [Fact]
    public void Validator_HasNoClosureFramework_AndNoCryptoLoad()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Settings", "AtherizSettingsValidator.cs");
        Assert.DoesNotContain("FailWhen(", src);
        Assert.DoesNotContain("CollectGuard(", src);
        Assert.DoesNotContain("TlsCertLoader", src);
    }

    [Fact]
    public void Validator_CertFileExistsOnly()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_val_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var v = new AtherizSettingsValidator();
            // Present-but-garbage cert passes validation (the host
            // fail-fasts at startup); a missing file still fails here.
            var garbage = Path.Combine(tmp, "garbage.pem");
            File.WriteAllText(garbage, "not a real certificate");
            var present = new AtherizSettings
            {
                SavePath = tmp, SecretPath = tmp,
                SslCertFile = garbage, AllowInsecureTlsFallback = false,
            };
            Assert.True(v.Validate(null, present).Succeeded);
            var missing = new AtherizSettings
            {
                SavePath = tmp, SecretPath = tmp,
                SslCertFile = Path.Combine(tmp, "missing.pem"),
            };
            Assert.Contains("SslCertFile not found", v.Validate(null, missing).FailureMessage ?? "");
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
