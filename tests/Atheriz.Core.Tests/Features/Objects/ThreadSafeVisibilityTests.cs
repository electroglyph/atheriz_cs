using System.Reflection;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Thread safety is structural, not a marker: every entity carries the base
// SyncRoot lock, so the old _is_thread_safe patch flags are gone and these
// pins assert the lock surface plus a live round-trip instead.
[Collection("Ported")]
public sealed class ThreadSafeVisibilityTests
{
    [Theory]
    [InlineData(typeof(GameObject))]
    [InlineData(typeof(Channel))]
    [InlineData(typeof(Account))]
    [InlineData(typeof(Script))]
    [InlineData(typeof(Node))]
    public void ThreadSafeMarker_IsGone(Type type)
    {
        Assert.Null(type.GetField("_is_thread_safe",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy));
    }

    [Theory]
    [InlineData(typeof(GameObject))]
    [InlineData(typeof(Channel))]
    [InlineData(typeof(Account))]
    [InlineData(typeof(Script))]
    [InlineData(typeof(Node))]
    public void SyncRoot_InheritedFromGameObject(Type type)
    {
        var prop = type.GetProperty("SyncRoot");
        Assert.NotNull(prop);
        Assert.Equal(typeof(GameObject), prop.DeclaringType);
        Assert.Equal(typeof(ReaderWriterLockSlim), prop.PropertyType);
    }

    [Fact]
    public void SyncRoot_LockRoundTrip_Works()
    {
        var obj = new GameObject();
        obj.SyncRoot.EnterReadLock();
        try
        {
            Assert.True(obj.SyncRoot.IsReadLockHeld);
        }
        finally { obj.SyncRoot.ExitReadLock(); }
        using (obj.WriteScope()) { }
        using (obj.ReadScope()) { }
    }
}
