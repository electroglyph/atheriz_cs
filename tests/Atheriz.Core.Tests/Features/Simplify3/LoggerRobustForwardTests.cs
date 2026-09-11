using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;
using Microsoft.Extensions.Logging;

namespace Atheriz.Core.Tests.Features.Simplify3;

// The Robust compat forwards are one-line LogRobust calls with no outer catch
// armor, so their output is identical to routing through LogRobust directly,
// and they never throw out of save/shutdown paths.
[Collection("Ported")]
public class LoggerRobustForwardTests
{
    private static string Capture(params Action[] emits)
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_robustfwd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = dir });
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                foreach (var emit in emits) emit();
                log = cap.Read();
            }
            return log;
        }
        finally
        {
            AtherizLogger.ApplySettings();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void LogErrorRobust_Output_MatchesLogRobustError()
    {
        var marker = "robust-err-" + Guid.NewGuid().ToString("N");
        var viaForward = Capture(() => AtherizLogger.LogErrorRobust(marker));
        var viaDirect = Capture(() => AtherizLogger.LogRobust(LogLevel.Error, marker));
        Assert.Contains($"ERROR: atheriz: {marker}", viaForward);
        Assert.Contains($"ERROR: atheriz: {marker}", viaDirect);
    }

    [Fact]
    public void LogInformationRobust_Output_MatchesLogRobustInformation()
    {
        var marker = "robust-info-" + Guid.NewGuid().ToString("N");
        var viaForward = Capture(() => AtherizLogger.LogInformationRobust(marker));
        var viaDirect = Capture(() => AtherizLogger.LogRobust(LogLevel.Information, marker));
        Assert.Contains($"INFORMATION: atheriz: {marker}", viaForward);
        Assert.Contains($"INFORMATION: atheriz: {marker}", viaDirect);
    }

    [Fact]
    public void RobustForwards_NeverThrow()
    {
        var marker = "robust-nothrow-" + Guid.NewGuid().ToString("N");
        Assert.Null(Record.Exception(() => AtherizLogger.LogErrorRobust(marker)));
        Assert.Null(Record.Exception(() => AtherizLogger.LogInformationRobust(marker)));
        Assert.Null(Record.Exception(() => AtherizLogger.LogRobust(LogLevel.Error, marker)));
    }

    [Fact]
    public void RobustForwards_AreSingleLineForwardsWithoutCatchArmor()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Logger.cs");
        var errRegion = SourceScan.Region(src, "public static void LogErrorRobust(string message)");
        var infoRegion = SourceScan.Region(src, "public static void LogInformationRobust(string message)");
        Assert.Contains("LogRobust(LogLevel.Error, message)", errRegion);
        Assert.Contains("LogRobust(LogLevel.Information, message)", infoRegion);
        Assert.DoesNotContain("try", errRegion);
        Assert.DoesNotContain("try", infoRegion);
    }
}
