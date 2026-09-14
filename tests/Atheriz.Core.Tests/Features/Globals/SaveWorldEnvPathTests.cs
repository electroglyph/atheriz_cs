using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Globals;

// The shutdown checkpoint must honor the ATHERIZ_SAVE_PATH environment
// override like autosave does, instead of writing to the raw settings path.
[Collection("Ported")]
public class SaveWorldEnvPathTests
{
    [Fact]
    public void SaveWorld_HonorsSavePathEnvOverride()
    {
        using var env = GlobalTestEnv.Enter();
        var envDir = Path.Combine(Path.GetTempPath(), "atheriz_saveenv_" + Guid.NewGuid().ToString("N"));
        var settingsDir = Path.Combine(Path.GetTempPath(), "atheriz_saveset_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(envDir);
        Directory.CreateDirectory(settingsDir);
        var oldEnv = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH");
        Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", envDir);
        try
        {
            var settings = new AtherizSettings { SavePath = settingsDir };
            var mi = typeof(StartStop).GetMethod("SaveWorld", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(mi);
            mi.Invoke(null, new object[] { settings });
            Assert.True(File.Exists(Path.Combine(envDir, "database.sqlite3")));
            Assert.False(File.Exists(Path.Combine(settingsDir, "database.sqlite3")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SAVE_PATH", oldEnv);
            try { Directory.Delete(envDir, true); } catch { }
            try { Directory.Delete(settingsDir, true); } catch { }
        }
    }
}
