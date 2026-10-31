using Atheriz.Server.Cli;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Hosting;

// Child side of the handover: the replacement binds only after the parent
// is gone and the ports are free; any stuck leg aborts without binding and
// cleans up its waiting marker.
[Collection("Ported")]
public class TakeoverTests
{
    private static AtherizSettings TempSettings(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "atheriz_takeover_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new AtherizSettings { SavePath = dir };
    }

    [Fact]
    public async Task Takeover_ParentGone_BootsReplacement()
    {
        var settings = TempSettings(out var dir);
        try
        {
            int? seenPort = -1;
            var rc = await TakeoverHandler.TakeoverAsync(
                settings, parentPid: 12345, port: 9999,
                waitParent: (_, _) => Task.FromResult(true),
                waitPortFree: (_, _) => Task.FromResult(true),
                start: (port, _, _) => { seenPort = port; return Task.FromResult(7); });
            Assert.Equal(7, rc);
            Assert.Equal(9999, seenPort);
            Assert.False(File.Exists(Path.Combine(dir, "restart.waiting")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Takeover_ParentStuck_AbortsWithoutBinding()
    {
        var settings = TempSettings(out var dir);
        try
        {
            var started = false;
            var rc = await TakeoverHandler.TakeoverAsync(
                settings, parentPid: 12345,
                waitParent: (_, _) => Task.FromResult(false),
                start: (_, _, _) => { started = true; return Task.FromResult(0); });
            Assert.Equal(1, rc);
            Assert.False(started);
            Assert.False(File.Exists(Path.Combine(dir, "restart.waiting")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Takeover_PortHeld_AbortsWithoutBinding()
    {
        var settings = TempSettings(out var dir);
        try
        {
            var started = false;
            var rc = await TakeoverHandler.TakeoverAsync(
                settings, parentPid: 12345,
                waitParent: (_, _) => Task.FromResult(true),
                waitPortFree: (_, _) => Task.FromResult(false),
                start: (_, _, _) => { started = true; return Task.FromResult(0); });
            Assert.Equal(1, rc);
            Assert.False(started);
            Assert.False(File.Exists(Path.Combine(dir, "restart.waiting")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
