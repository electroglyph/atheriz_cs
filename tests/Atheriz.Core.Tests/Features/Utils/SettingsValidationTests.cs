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
}
