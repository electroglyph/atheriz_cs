// Pins for U13 (_is_thread_safe narrowed to internal): same-assembly tests read
// the flags directly with no reflection.
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify3;

[Collection("Ported")]
public sealed class B25bU13ThreadSafeTests
{
    [Fact]
    public void IsThreadSafe_AllTypes_TrueByDefault()
    {
        Assert.True(GameObject._is_thread_safe);
        Assert.True(Channel._is_thread_safe);
        Assert.True(Account._is_thread_safe);
        Assert.True(Script._is_thread_safe);
        Assert.True(Node._is_thread_safe);
    }

    [Fact]
    public void IsThreadSafe_DirectWrite_RoundTrips()
    {
        GameObject._is_thread_safe = false;
        Channel._is_thread_safe = false;
        Account._is_thread_safe = false;
        Script._is_thread_safe = false;
        Node._is_thread_safe = false;
        try
        {
            Assert.False(GameObject._is_thread_safe);
            Assert.False(Channel._is_thread_safe);
            Assert.False(Account._is_thread_safe);
            Assert.False(Script._is_thread_safe);
            Assert.False(Node._is_thread_safe);
        }
        finally
        {
            GameObject._is_thread_safe = true;
            Channel._is_thread_safe = true;
            Account._is_thread_safe = true;
            Script._is_thread_safe = true;
            Node._is_thread_safe = true;
        }
        Assert.True(GameObject._is_thread_safe);
        Assert.True(Channel._is_thread_safe);
        Assert.True(Account._is_thread_safe);
        Assert.True(Script._is_thread_safe);
        Assert.True(Node._is_thread_safe);
    }
}
