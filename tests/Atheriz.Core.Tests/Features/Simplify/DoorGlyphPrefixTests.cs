using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests.Features.Simplify;

// Door-glyph prefix hoist only: all eight defaults stay byte-identical, and
// AllSymbols keeps reflecting live reassignments (no static cache).
public class DoorGlyphPrefixTests
{
    private const string Prefix = "\x1b[1m\x1b[38;2;166;97;0m\x1b[48;2;0;0;0m";

    [Fact]
    public void DoorGlyphDefaults_KeepExactBytes()
    {
        var settings = new AtherizSettings();
        Assert.Equal(Prefix + "━\x1b[0m", settings.NsClosedDoor);
        Assert.Equal(Prefix + "┚\x1b[0m", settings.NsOpenDoor1);
        Assert.Equal(Prefix + "┒\x1b[0m", settings.NsOpenDoor2);
        Assert.Equal(Prefix + "┃\x1b[0m", settings.EwClosedDoor);
        Assert.Equal(Prefix + "┙\x1b[0m", settings.EwOpenDoor1);
        Assert.Equal(Prefix + "┕\x1b[0m", settings.EwOpenDoor2);
        Assert.Equal(Prefix + "╳\x1b[0m", settings.UdClosedDoor);
        Assert.Equal(Prefix + "▽\x1b[0m", settings.UdOpenDoor);
    }

    [Fact]
    public void AllSymbols_ReflectsLiveReassignment()
    {
        var settings = new AtherizSettings { SingleWallPlaceholder = "Z" };
        Assert.Contains("Z", settings.AllSymbols);
        Assert.DoesNotContain("༗", settings.AllSymbols);
    }
}
