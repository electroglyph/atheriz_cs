using Atheriz.Core.Settings;
using Atheriz.Core.Tests.Features.Regression;
using Microsoft.Extensions.Logging;

namespace Atheriz.Core.Tests.Features.Simplify;

// The level map is built once (frozen, case-insensitive); lock takes stay
// split (coalescing around the factory refresh would self-deadlock).
[Collection("Ported")]
public class LoggerLevelMapTests
{
    [Fact]
    public void LevelMap_CaseInsensitive_DebugEnables()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_lvlmap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "DEBUG", SavePath = dir });
            Assert.True(AtherizLogger.GetLogger("t").IsEnabled(LogLevel.Debug));
        }
        finally
        {
            AtherizLogger.ApplySettings();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void LevelMap_Unknown_FallsBackToInformation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_lvlmap2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "bogus", SavePath = dir });
            Assert.False(AtherizLogger.GetLogger("t").IsEnabled(LogLevel.Debug));
            Assert.True(AtherizLogger.GetLogger("t").IsEnabled(LogLevel.Information));
        }
        finally
        {
            AtherizLogger.ApplySettings();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void LevelMap_BuiltOnce_LocksStaySplit()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Logger.cs");
        Assert.Contains("FrozenDictionary<string, LogLevel> LevelMap", src);
        Assert.Contains("ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)", src);
        Assert.DoesNotContain("var map = new Dictionary<string, LogLevel>", src);
    }
}
