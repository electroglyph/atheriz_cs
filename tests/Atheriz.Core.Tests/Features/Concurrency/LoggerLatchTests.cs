using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Concurrency;

// A transient file failure must not mute file logging forever.
[Collection("Ported")]
public class LoggerLatchTests
{
    // the write-only _fileEnabled latch was deleted;
    // the failure witness is now the lock-free last-failure tick stamp.
    private static bool ReadFileFailureRecorded()
    {
        var field = typeof(AtherizLogger).GetField("_lastFileFailureTicks", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (long)field!.GetValue(null)! != 0;
    }

    [Fact]
    public void TransientWriteFailure_DoesNotSilenceLaterWrites()
    {
        // File logging (Logger.cs:118,144) must keep retrying after a transient
        // I/O error instead of staying muted: write, break the directory, heal
        // it, and the post-heal line must still land in server.log.
        string tag = Guid.NewGuid().ToString("N");
        string dir = Path.Combine(Path.GetTempPath(), $"atheriz_logger_latch_{tag}");
        Directory.CreateDirectory(dir);
        AtherizLogger.ApplySettings(new AtherizSettings { SavePath = dir, LogLevel = "info" });
        try
        {
            string before = $"latch-before-{tag}";
            AtherizLogger.LogInformation(before);
            string logFile = Path.Combine(dir, "server.log");
            Assert.True(File.Exists(logFile) && File.ReadAllText(logFile).Contains(before),
                "precondition: file logging is not writing before the failure");
            // Break the log directory by replacing it with a same-named file, so
            // every append fails deterministically on any OS and user (ENOTDIR).
            Directory.Delete(dir, recursive: true);
            File.WriteAllText(dir, "blocking file");
            try
            {
                AtherizLogger.LogInformation($"latch-during-{tag}");
            }
            finally
            {
                File.Delete(dir);
                Directory.CreateDirectory(dir);
            }
            // Guard against a vacuous pass: the failure must have been recorded.
            Assert.True(ReadFileFailureRecorded(), "precondition: the failed write was not recorded");
            string after = $"latch-after-{tag}";
            AtherizLogger.LogInformation(after);
            string healed = Path.Combine(dir, "server.log");
            Assert.True(File.Exists(healed) && File.ReadAllText(healed).Contains(after),
                "post-heal line missing: transient failure permanently muted file logging");
        }
        finally
        {
            // No latch reset needed: a successful write clears the stamp itself.
            AtherizLogger.ApplySettings();
            try { if (File.Exists(dir)) File.Delete(dir); } catch { }
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
