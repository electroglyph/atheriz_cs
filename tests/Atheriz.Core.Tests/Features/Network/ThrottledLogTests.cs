using System.Reflection;
using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Network;

// The six dict+Lock throttle pairs (ConnectionManager x2, WebSocketProtocol,
// TelnetProtocol, BaseConnection retry-drain, WebSocketHandler) share one
// holder: per-host accept→suppress→re-accept windows, per-instance isolation,
// and the fixed retry-drain key keep their exact behavior behind it.
[Collection("Ported")]
public sealed class ThrottledLogTests
{
    [Fact]
    public void ShouldLog_FirstCallForHost_Accepts()
    {
        var log = new ThrottledLog(5.0);
        Assert.True(log.ShouldLog("h1", 100.0));
    }

    [Fact]
    public void ShouldLog_SecondCallWithinWindow_Suppresses()
    {
        var log = new ThrottledLog(5.0);
        Assert.True(log.ShouldLog("h1", 100.0));
        Assert.False(log.ShouldLog("h1", 102.0));
    }

    [Fact]
    public void ShouldLog_CallAfterWindow_ReAccepts()
    {
        var log = new ThrottledLog(5.0);
        Assert.True(log.ShouldLog("h1", 100.0));
        Assert.False(log.ShouldLog("h1", 104.9));
        Assert.True(log.ShouldLog("h1", 105.0));
    }

    [Fact]
    public void ShouldLog_DifferentHosts_TrackedIndependently()
    {
        var log = new ThrottledLog(5.0);
        Assert.True(log.ShouldLog("h1", 100.0));
        Assert.True(log.ShouldLog("h2", 101.0));
        Assert.False(log.ShouldLog("h1", 102.0));
        Assert.False(log.ShouldLog("h2", 103.0));
    }

    [Fact]
    public void ShouldLog_SeparateInstances_DoNotShareState()
    {
        var first = new ThrottledLog(5.0);
        var second = new ThrottledLog(5.0);
        Assert.True(first.ShouldLog("h1", 100.0));
        Assert.True(second.ShouldLog("h1", 100.0));
        Assert.False(first.ShouldLog("h1", 101.0));
        Assert.False(second.ShouldLog("h1", 101.0));
    }

    [Fact]
    public void ShouldLog_CustomWindow_PreservedPerInstance()
    {
        var narrow = new ThrottledLog(5.0);
        var wide = new ThrottledLog(10.0);
        Assert.True(narrow.ShouldLog("h1", 100.0));
        Assert.True(wide.ShouldLog("h1", 100.0));
        Assert.True(narrow.ShouldLog("h1", 107.0));
        Assert.False(wide.ShouldLog("h1", 107.0));
        Assert.True(wide.ShouldLog("h1", 110.0));
    }

    [Fact]
    public void ShouldLog_FixedRetryDrainKey_ThrottlesLikePerHostKey()
    {
        var log = new ThrottledLog(5.0);
        Assert.True(log.ShouldLog("retry-drain", 100.0));
        Assert.False(log.ShouldLog("retry-drain", 102.0));
        Assert.True(log.ShouldLog("retry-drain", 105.0));
    }

    [Fact]
    public void ShouldLog_MonotonicOverload_AcceptsThenSuppresses()
    {
        var log = new ThrottledLog(5.0);
        Assert.True(log.ShouldLog("h1"));
        Assert.False(log.ShouldLog("h1"));
    }

    [Fact]
    public void ShouldLogMalformed_FreshHost_AcceptsThenSuppresses()
    {
        var method = typeof(ConnectionManager).GetMethod(
            "ShouldLogMalformed", BindingFlags.NonPublic | BindingFlags.Static)!;
        var host = "throttle-pin-" + Guid.NewGuid().ToString("N");
        Assert.True((bool)method.Invoke(null, [host])!);
        Assert.False((bool)method.Invoke(null, [host])!);
    }
}
