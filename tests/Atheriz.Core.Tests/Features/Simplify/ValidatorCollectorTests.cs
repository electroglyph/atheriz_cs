using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Simplify;

// Table-driven range checks: preformatted messages and stable failure-list
// ordering are preserved by the local collectors.
public class ValidatorCollectorTests
{
    private static AtherizSettings ValidIn(string dir) => new() { SavePath = dir, SecretPath = dir };

    [Fact]
    public void FailureList_PreservesOrderAndMessages()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var options = ValidIn(dir);
            options.MaxCharacters = 0;
            options.ServerName = "";
            options.LogLevel = "bogus";
            var result = new AtherizSettingsValidator().Validate(null, options);
            Assert.True(result.Failed);
            var message = result.FailureMessage ?? "";
            Assert.Contains("MaxCharacters must be >0 and <=100 (was 0).", message);
            Assert.Contains("ServerName must be non-empty.", message);
            Assert.Contains("LogLevel must be one of debug/info/warning/error/critical (was 'bogus').", message);
            Assert.True(message.IndexOf("MaxCharacters", StringComparison.Ordinal)
                < message.IndexOf("ServerName", StringComparison.Ordinal));
            Assert.True(message.IndexOf("ServerName", StringComparison.Ordinal)
                < message.IndexOf("LogLevel", StringComparison.Ordinal));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void InterfaceChecks_FoldedTwins_KeepExactMessages()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var validator = new AtherizSettingsValidator();
            var bad = ValidIn(dir);
            bad.TelnetInterface = "bogus";
            Assert.Contains("TelnetInterface unparseable: 'bogus'.",
                validator.Validate(null, bad).FailureMessage ?? "");
            var ipv6Any = ValidIn(dir);
            ipv6Any.WebserverInterface = "::";
            Assert.False(validator.Validate(null, ipv6Any).Failed);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void GuardCollector_ReportsSavePathInvalid()
    {
        var validator = new AtherizSettingsValidator();
        var options = new AtherizSettings { SavePath = "rel-save", SecretPath = "rel-secret" };
        var message = validator.Validate(null, options).FailureMessage ?? "";
        Assert.Contains("SavePath invalid:", message);
        Assert.Contains("SecretPath invalid:", message);
    }
}
