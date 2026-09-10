using System.Reflection;
using Atheriz.Core.Network;

namespace Atheriz.Core.Tests.Features.Network;

// Retry-drain/busy naming-only constants keep their exact values.
[Collection("Ported")]
public sealed class BaseConnectionTimingConstantsTests
{
    private static T Static<T>(string name) =>
        (T)typeof(BaseConnection).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    [Fact]
    public void RetryDrainWindows_KeepValues()
    {
        Assert.Equal(5.0, Static<double>("RetryDrainDropWindowSeconds"));
        Assert.Equal(TimeSpan.FromMilliseconds(50), Static<TimeSpan>("RetryDrainDelay"));
        Assert.Equal(TimeSpan.FromMilliseconds(250), Static<TimeSpan>("RetryDrainRearmDelay"));
        Assert.Equal(1.0, Static<double>("InputBusyWindowSeconds"));
        Assert.Equal(1024, Static<int>("MaxOutstandingRetryDrains"));
    }
}
