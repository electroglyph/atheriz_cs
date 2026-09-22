using System.Reflection;
using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Network;

// Throttle holders are per-instance: a burst recorded on one manager,
// protocol, or connection never suppresses logging on another.
[Collection("Ported")]
public sealed class ThrottleIsolationTests
{
    [Fact]
    public void MalformedThrottle_IsPerManagerWorld()
    {
        var first = new ConnectionManager();
        var second = new ConnectionManager();
        try
        {
            var method = typeof(ConnectionManager).GetMethod(
                "ShouldLogMalformed", BindingFlags.NonPublic | BindingFlags.Instance)!;
            const string host = "iso-malformed";
            Assert.True((bool)method.Invoke(first, [host])!);
            Assert.False((bool)method.Invoke(first, [host])!);
            // Same host on a fresh world is unaffected by the first's burst.
            Assert.True((bool)method.Invoke(second, [host])!);
        }
        finally
        {
            first.Atp.Stop(wait: false);
            second.Atp.Stop(wait: false);
        }
    }

    [Fact]
    public void OversizeThrottle_IsPerManagerWorld()
    {
        var first = new ConnectionManager();
        var second = new ConnectionManager();
        try
        {
            const string host = "iso-oversize";
            Assert.True(first.ShouldLogOversize(host));
            Assert.False(first.ShouldLogOversize(host));
            Assert.True(second.ShouldLogOversize(host));
        }
        finally
        {
            first.Atp.Stop(wait: false);
            second.Atp.Stop(wait: false);
        }
    }

    [Fact]
    public void RetryDrainThrottle_IsPerConnection()
    {
        // Fixed-key holder lives on the connection: two connections each own
        // a fresh suppression map, so one connection's full backlog never
        // silences the other's drop warning.
        var first = new FakeConnection("iso-drain-1");
        var second = new FakeConnection("iso-drain-2");
        var field = typeof(BaseConnection).GetField(
            "_retryDrainDropLog", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var firstLog = (ThrottledLog)field.GetValue(first)!;
        var secondLog = (ThrottledLog)field.GetValue(second)!;
        Assert.NotSame(firstLog, secondLog);
        Assert.True(firstLog.ShouldLog("retry-drain", 100.0));
        Assert.True(secondLog.ShouldLog("retry-drain", 100.0));
    }

    [Fact]
    public void OverlongDropThrottle_IsPerConnection()
    {
        var first = new TelnetConnection(new object(), new object(), "iso-overlong-1");
        var second = new TelnetConnection(new object(), new object(), "iso-overlong-2");
        var field = typeof(TelnetConnection).GetField(
            "_overlongDropLog", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var firstLog = (ThrottledLog)field.GetValue(first)!;
        var secondLog = (ThrottledLog)field.GetValue(second)!;
        Assert.NotSame(firstLog, secondLog);
        Assert.True(firstLog.ShouldLog("h", 100.0));
        Assert.True(secondLog.ShouldLog("h", 100.0));
    }
}
