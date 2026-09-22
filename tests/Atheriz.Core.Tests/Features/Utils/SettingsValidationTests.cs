using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Utils;

// Startup settings validation must reject values that silently degrade the
// server: an unknown log level (falls back to Information without a word,
// Logger.cs:47-50) and a non-positive SecondsPerMinute (AtherizSettings.cs:145;
// downstream time math divides by it). Pure checks, no engine globals.
public sealed class SettingsValidationTests
{
    [Fact]
    public void SettingsValidator_UnknownLogLevel_FailsMentioningLogLevel()
    {
        var v = new AtherizSettingsValidator();
        var s = AbsolutePaths(new AtherizSettings { LogLevel = "verbose" });
        var result = v.Validate(null, s);
        Assert.True(result.Failed);
        Assert.Contains("LogLevel", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsValidator_ZeroSecondsPerMinute_Fails()
    {
        var v = new AtherizSettingsValidator();
        var s = AbsolutePaths(new AtherizSettings { SecondsPerMinute = 0 });
        var result = v.Validate(null, s);
        Assert.True(result.Failed);
        Assert.Contains("SecondsPerMinute", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static AtherizSettings AbsolutePaths(AtherizSettings s)
    {
        s.SavePath = Path.Combine(Path.GetTempPath(), "atheriz_shouldbe_save");
        s.SecretPath = Path.Combine(Path.GetTempPath(), "atheriz_shouldbe_secret");
        return s;
    }

    [Fact]
    public void SettingsValidator_NonPositiveTickInterval_Fails()
    {
        // The validator rejects non-positive tick intervals at startup.
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var options = new AtherizSettings { SavePath = dir, SecretPath = dir, TimeUpdateSeconds = 0 };
            var message = new AtherizSettingsValidator().Validate(null, options).FailureMessage ?? "";
            Assert.Contains("TimeUpdateSeconds must be >0 (was 0).", message);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void SettingsValidator_KeyWithoutCert_FailsEvenWhenKeyExists()
    {
        // A key file without a certificate is a config error even when the
        // key file exists — it can never be used.
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var keyFile = Path.Combine(dir, "key.pem");
            File.WriteAllText(keyFile, "fake-key");
            var options = new AtherizSettings { SavePath = dir, SecretPath = dir, SslKeyFile = keyFile, SslCertFile = "" };
            var result = new AtherizSettingsValidator().Validate(null, options);
            Assert.True(result.Failed);
            Assert.Contains("SslKeyFile is set but SslCertFile is empty; a key without a certificate cannot be used.",
                result.FailureMessage ?? "");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
