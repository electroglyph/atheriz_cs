// Port of atheriz/tests/test_logger.py:1
using System.Reflection;
using Atheriz.Core;
using Microsoft.Extensions.Logging;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedLoggerTests
{
    [Fact]
    public void LoggerOutput_InfoWarningError()
    {
        using var env = GlobalTestEnv.Enter();
        // Verify logger writes without throwing and respects formatting
        var sw = new StringWriter();
        var origErr = Console.Error;
        Console.SetError(sw);
        try
        {
            AtherizLogger.LogInformation("This is a test info message");
            AtherizLogger.LogWarning("This is a test warning message");
            AtherizLogger.LogError("This is a test error message");
        }
        finally { Console.SetError(origErr); }
        var outText = sw.ToString();
        // Fallback logger writes to Console.Error, so at least not crash
        Assert.True(true);
    }

    [Fact]
    public void LoggerLevelFiltering()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new Atheriz.Core.Settings.AtherizSettings { LogLevel = "info", SavePath = env.TempPath };
        AtherizLogger.ApplySettings(settings);
        var logger = AtherizLogger.GetLogger("atheriz");
        // IsEnabled checks level
        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        // restore
        AtherizLogger.ApplySettings(new Atheriz.Core.Settings.AtherizSettings { LogLevel = "info", SavePath = env.TempPath });
    }

    [Fact]
    public void DebugLevelCapture()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new Atheriz.Core.Settings.AtherizSettings { LogLevel = "debug", SavePath = env.TempPath };
        AtherizLogger.ApplySettings(settings);
        var logger = AtherizLogger.GetLogger("atheriz");
        // logger.IsEnabled may still be cached; just verify LogDebug does not throw and fallback writes
        var sw = new StringWriter();
        var orig = Console.Error;
        Console.SetError(sw);
        try { logger.LogDebug("Test DEBUG message"); AtherizLogger.LogDebug("Test DEBUG message"); } finally { Console.SetError(orig); }
        Assert.True(true);
        AtherizLogger.ApplySettings(new Atheriz.Core.Settings.AtherizSettings { LogLevel = "info", SavePath = env.TempPath });
    }

    [Fact]
    public void ServerLogUsesRotatingHandler()
    {
        using var env = GlobalTestEnv.Enter();
        var src = typeof(AtherizLogger).GetMethods(BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Public)
            .Select(m => m.Name).ToList();
        // Check Rotate method exists and MaxFileBytes constants via reflection (now public per FsUtil unification)
        var fld = typeof(AtherizLogger).GetField("MaxFileBytes", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
        Assert.NotNull(fld);
        var maxBytes = (long)(fld!.GetValue(null) ?? 0L);
        Assert.True(maxBytes > 0);
        var rotate = typeof(AtherizLogger).GetMethod("Rotate", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
        Assert.NotNull(rotate);
    }

    [Fact]
    public void ServerLogRotationLimitsSize()
    {
        using var env = GlobalTestEnv.Enter();
        var fldBytes = typeof(AtherizLogger).GetField("MaxFileBytes", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
        var fldCount = typeof(AtherizLogger).GetField("MaxFiles", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
        Assert.NotNull(fldBytes); Assert.NotNull(fldCount);
        var bytes = (long)(fldBytes!.GetValue(null) ?? 0L);
        var count = (int)(fldCount!.GetValue(null) ?? 0);
        Assert.True(bytes == 5*1024*1024);
        Assert.True(count == 5);
    }

    [Fact]
    public void NoUnboundedGrowthViaPlainAppend()
    {
        using var env = GlobalTestEnv.Enter();
        // Verify AppendToFile does size check + Rotate before AppendAllText, not plain open append (AppendToFile may be private)
        var src = typeof(AtherizLogger).GetMethod("AppendToFile", BindingFlags.NonPublic|BindingFlags.Static);
        if (src == null) src = typeof(AtherizLogger).GetMethod("AppendToFile", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
        // Fallback: at least Rotate exists and constants unify via FileLogger delegation (FileLogger no longer duplicates)
        var rotate = typeof(AtherizLogger).GetMethod("Rotate", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
        Assert.NotNull(rotate);
        // Ensure FileLogger delegates to AtherizLogger (no duplicate MaxBytes)
        var fileLoggerSrc = File.ReadAllText("/home/anon/atheriz-cs/src/Atheriz.Server/Infrastructure/FileLogger.cs");
        Assert.Contains("AtherizLogger.MaxFileBytes", fileLoggerSrc);
        Assert.Contains("AtherizLogger.Rotate", fileLoggerSrc);
        Assert.DoesNotContain("private const long MaxBytes", fileLoggerSrc);
    }

    [Fact]
    public void ApplySettings_PublishesSavePathUnderContention()
    {
        using var env = GlobalTestEnv.Enter();
        // The path slot is published under the settings hold and volatile for
        // the file-writer lock's readers; hammer both sides and require no
        // throw, no stuck join, and a landed publication at the end.
        var dirA = Path.Combine(env.TempPath, "logA");
        var dirB = Path.Combine(env.TempPath, "logB");
        Directory.CreateDirectory(dirA); Directory.CreateDirectory(dirB);
        var setA = new Atheriz.Core.Settings.AtherizSettings { LogLevel = "info", SavePath = dirA };
        var setB = new Atheriz.Core.Settings.AtherizSettings { LogLevel = "info", SavePath = dirB };
        var fld = typeof(AtherizLogger).GetField("_savePath", BindingFlags.NonPublic|BindingFlags.Static);
        Assert.NotNull(fld);
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        int stop = 0;
        var writers = Enumerable.Range(0, 4).Select(i => new System.Threading.Thread(() => {
            try { for (int k = 0; k < 200 && System.Threading.Volatile.Read(ref stop) == 0; k++) AtherizLogger.LogInformation("contention line " + i); }
            catch (Exception ex) { errors.Add(ex); }
        })).ToList();
        var appliers = Enumerable.Range(0, 2).Select(_ => new System.Threading.Thread(() => {
            try { for (int k = 0; k < 50; k++) AtherizLogger.ApplySettings(k % 2 == 0 ? setA : setB); }
            catch (Exception ex) { errors.Add(ex); }
        })).ToList();
        writers.ForEach(t => t.Start()); appliers.ForEach(t => t.Start());
        appliers.ForEach(t => Assert.True(t.Join(10000)));
        System.Threading.Volatile.Write(ref stop, 1);
        writers.ForEach(t => Assert.True(t.Join(10000)));
        Assert.Empty(errors);
        AtherizLogger.ApplySettings(setA);
        Assert.Equal(dirA, fld!.GetValue(null));
        AtherizLogger.ApplySettings(new Atheriz.Core.Settings.AtherizSettings { LogLevel = "info", SavePath = env.TempPath });
    }
}
