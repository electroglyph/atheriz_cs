using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;
using Microsoft.Extensions.Logging;

namespace Atheriz.Core.Tests.Features.Simplify;

// One echo-and-append tail for both Write branches (per-sink bytes identical);
// the Robust wrappers collapse bodies into LogRobust but keep their names.
[Collection("Ported")]
public class LoggerEchoAppendTests
{
    [Fact]
    public void Write_LoggerBranch_EmitsExactLine()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_echoapp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var marker = "echo-bytes-" + Guid.NewGuid().ToString("N");
        try
        {
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = dir });
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                AtherizLogger.LogInformation(marker);
                log = cap.Read();
            }
            Assert.Contains($"INFORMATION: atheriz: {marker}", log);
            Assert.Contains(marker, File.ReadAllText(Path.Combine(dir, "server.log")));
        }
        finally
        {
            AtherizLogger.ApplySettings();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void RobustWrappers_Forward_WithNamesKept()
    {
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_robust_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var marker = "robust-" + Guid.NewGuid().ToString("N");
        try
        {
            AtherizLogger.ApplySettings(new AtherizSettings { LogLevel = "debug", SavePath = dir });
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                AtherizLogger.LogErrorRobust(marker);
                AtherizLogger.LogInformationRobust(marker);
                AtherizLogger.LogRobust(LogLevel.Warning, marker);
                log = cap.Read();
            }
            Assert.Contains($"ERROR: atheriz: {marker}", log);
            Assert.Contains($"INFORMATION: atheriz: {marker}", log);
            Assert.Contains($"WARNING: atheriz: {marker}", log);
        }
        finally
        {
            AtherizLogger.ApplySettings();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void EchoAndAppend_DefinedOnce_RobustCollapsed()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Logger.cs");
        Assert.Equal(1, SourceScan.Count(src, "private static void EchoAndAppend("));
        Assert.Equal(1, SourceScan.Count(src, "public static void LogRobust("));
    }
}
